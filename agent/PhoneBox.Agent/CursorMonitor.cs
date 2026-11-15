using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PhoneBox.Agent;

/// <param name="Id">Cloud agent bcId</param>
/// <param name="Title">Agent name/title</param>
/// <param name="Status">running | finished | error | setup</param>
/// <param name="Repo">Repository name</param>
/// <param name="Branch">Git branch</param>
/// <param name="PrUrl">Pull request URL if created</param>
/// <param name="UpdatedAt">Last update timestamp (ms)</param>
/// <param name="CreatedAt">Agent creation timestamp (ms)</param>
public sealed record CursorAgent(string Id, string Title, string Status, string? Repo, string? Branch, 
    string? PrUrl, long UpdatedAt, long CreatedAt);

/// <summary>
/// Monitors Cursor Cloud Agents via the Cursor API (https://api.cursor.com).
/// Polls at a sensible interval (30-60s) with backoff on errors/429.
/// Also detects if the Cursor IDE is running locally (process check only, no private DB parsing).
/// </summary>
public sealed class CursorMonitor(ConfigStore config, ILogger<CursorMonitor> log) : BackgroundService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private volatile IReadOnlyList<CursorAgent> _agents = [];
    private volatile bool _ideRunning = false;
    private volatile string? _error = null;
    private long _lastUpdate = 0; // Use Interlocked for thread-safe access
    private int _pollInterval = 30_000; // 30 seconds default
    private int _consecutiveErrors = 0;

    public IReadOnlyList<CursorAgent> Agents() => _agents;
    public bool IdeRunning() => _ideRunning;
    public string? Error() => _error;
    public long LastUpdate() => Interlocked.Read(ref _lastUpdate);
    
    public int RunningCount() => _agents.Count(a => a.Status == "running");
    
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PhoneBox/" + AppPaths.Version);
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Check if Cursor IDE is running locally (best-effort process check)
                _ideRunning = IsCursorIdeRunning();
                
                // Poll Cursor Cloud API if API key is configured
                var apiKey = config?.Get().Cursor.ApiKey;
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    await PollCursorApiAsync(apiKey, ct);
                    _error = null;
                    _consecutiveErrors = 0;
                    _pollInterval = 30_000; // Reset to normal interval
                }
                else
                {
                    _error = null; // No error, just not configured
                    _agents = [];
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _error = "Rate limited";
                _consecutiveErrors++;
                _pollInterval = Math.Min(300_000, 30_000 * (1 << Math.Min(_consecutiveErrors, 4))); // Exponential backoff up to 5 min
                log.LogWarning("Cursor API rate limited, backing off to {Interval}s", _pollInterval / 1000);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _error = "Invalid API key";
                _consecutiveErrors++;
                _pollInterval = 120_000; // Check every 2 min in case key is updated
                log.LogWarning("Cursor API: invalid API key");
            }
            catch (Exception ex)
            {
                _error = "API error";
                _consecutiveErrors++;
                _pollInterval = Math.Min(120_000, 30_000 * (1 << Math.Min(_consecutiveErrors, 2))); // Backoff up to 2 min
                log.LogDebug("Cursor API error: {Msg}", ex.Message);
            }
            
            await Task.Delay(_pollInterval, ct);
        }
    }

    private async Task PollCursorApiAsync(string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.cursor.com/v1/agents");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        
        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        var agents = new List<CursorAgent>();
        
        // Parse agents from API response
        // API structure: { "agents": [...] } or { "data": [...] }
        var agentsArray = root.TryGetProperty("agents", out var a) ? a : 
                         root.TryGetProperty("data", out var d) ? d : root;
        
        if (agentsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in agentsArray.EnumerateArray())
            {
                try
                {
                    var id = GetString(item, "id") ?? GetString(item, "bcId") ?? GetString(item, "agentId") ?? "";
                    if (string.IsNullOrEmpty(id)) continue;
                    
                    var title = GetString(item, "name") ?? GetString(item, "title") ?? GetString(item, "chatName") ?? "Untitled";
                    var status = MapStatus(GetString(item, "status") ?? GetString(item, "lifecycle"));
                    var repo = GetString(item, "repo") ?? GetString(item, "repoUrl");
                    var branch = GetString(item, "branch") ?? GetString(item, "gitBranch");
                    var prUrl = GetString(item, "prUrl") ?? GetString(item, "pullRequestUrl");
                    var updatedAt = GetLong(item, "updatedAt") ?? GetLong(item, "lastUpdated") ?? 0;
                    var createdAt = GetLong(item, "createdAt") ?? GetLong(item, "created") ?? updatedAt;
                    
                    // Extract repo name from full URL if needed
                    if (!string.IsNullOrEmpty(repo) && repo.Contains("/"))
                    {
                        var parts = repo.TrimEnd('/').Split('/');
                        if (parts.Length >= 2)
                            repo = $"{parts[^2]}/{parts[^1]}".Replace(".git", "");
                    }
                    
                    agents.Add(new CursorAgent(id, title, status, repo, branch, prUrl, updatedAt, createdAt));
                }
                catch (Exception ex)
                {
                    log.LogDebug("Failed to parse Cursor agent: {Msg}", ex.Message);
                }
            }
        }
        
        _agents = agents;
        Interlocked.Exchange(ref _lastUpdate, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static string MapStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "running" or "active" or "in_progress" => "running",
            "completed" or "done" or "success" => "finished",
            "failed" or "error" or "cancelled" => "error",
            "setup" or "initializing" or "queued" => "setup",
            _ => "idle"
        };
    }

    private static bool IsCursorIdeRunning()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return Process.GetProcesses().Any(p => 
                    p.ProcessName.Equals("Cursor", StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessName.Equals("cursor", StringComparison.OrdinalIgnoreCase));
            }
            else if (OperatingSystem.IsMacOS())
            {
                return Process.GetProcesses().Any(p => 
                    p.ProcessName.Contains("Cursor", StringComparison.OrdinalIgnoreCase));
            }
            else if (OperatingSystem.IsLinux())
            {
                return Process.GetProcesses().Any(p => 
                    p.ProcessName.Equals("cursor", StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessName.Contains("cursor", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch
        {
            // Process enumeration can fail; treat as not running
        }
        return false;
    }

    private static string? GetString(JsonElement el, string prop)
    {
        return el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    private static long? GetLong(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), out var s)) return s;
        return null;
    }
}
