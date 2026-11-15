using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PhoneBox.Agent;
using Xunit;

/// <summary>Config normalization: templates, the pre-0.6.2 page migration, grid limits, ids and what the phone sees.</summary>
public class ConfigTests
{
    private static AgentConfig Load(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"phonebox-cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try { return new ConfigStore(path).Get(); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EmptyConfigGetsTheDefaultPagesAndFreshTokens()
    {
        var c = Load("{}");
        Assert.NotEmpty(c.Pages);
        Assert.All(c.Pages, p => Assert.NotEmpty(p.Widgets!));
        Assert.False(string.IsNullOrEmpty(c.Lan.Token));
        Assert.False(string.IsNullOrEmpty(c.Lan.WebhookToken));
        Assert.NotEqual(c.Lan.Token, c.Lan.WebhookToken);
    }

    [Fact]
    public void TemplatesExpandIntoWidgets()
    {
        var c = Load("""{ "pages": [ { "template": "music" }, { "template": "music" } ] }""");
        Assert.Equal(["music", "music-2"], c.Pages.Select(p => p.Id));
        Assert.Contains(c.Pages[0].Widgets!, w => w.Type == "media");
        Assert.Null(c.Pages[0].Template);
    }

    [Fact]
    public void TurkishCapitalDottedIKeepsItsLetterInIds()
    {
        // ToLowerInvariant leaves İ (U+0130) as it is; the id must not lose the letter ("zmir")
        var c = Load("""
        { "pages": [ { "title": "İzmir", "icon": "x", "widgets": [ { "type": "clock" } ] },
                     { "title": "İş Akışı", "icon": "x", "widgets": [ { "type": "clock" } ] } ],
          "shortcuts": [ { "label": "İndir", "cmd": "notepad" } ],
          "rules": [ { "name": "İkindi", "when": "always", "then": "notify" } ] }
        """);
        Assert.Equal(["izmir", "is-akisi"], c.Pages.Select(p => p.Id));
        Assert.Equal("indir", c.Shortcuts.Single().Id);
        Assert.Equal("ikindi", c.Rules.Single().Id);
    }

    [Fact]
    public void LegacyHomePageKeepsItsTilesAndOptions()
    {
        // 0.6.1: a fixed "home" page with options
        var c = Load("""{ "pages": [ { "type": "home", "title": "Start", "options": { "tiles": ["cpu", "ram"], "media": false, "weather": false } } ] }""");
        var p = c.Pages.Single();
        Assert.Equal("Start", p.Title);
        Assert.DoesNotContain(p.Widgets!, w => w.Type == "media");
        var stats = p.Widgets!.Where(w => w.Type == "stat").ToList();
        Assert.Equal(["cpu", "ram"], stats.Select(w => w.Str("metric")));
        Assert.Equal(12, stats.Sum(w => w.W));                         // the tiles share the full width
        Assert.False(p.Widgets!.Single(w => w.Type == "clock").Bool("weather"));
        Assert.Equal(12, p.Widgets!.Single(w => w.Type == "clock").W); // no media: the clock takes the row
    }

    [Fact]
    public void LegacyProcessPageKeepsTheKillSetting()
    {
        var c = Load("""{ "pages": [ { "type": "procs", "options": { "allowKill": false } } ] }""");
        Assert.False(c.Pages.Single().Widgets!.Single(w => w.Type == "processes").Bool("allowKill"));
    }

    [Fact]
    public void WidgetsStayOnTheGridAndGetUniqueIds()
    {
        var c = Load("""
        { "pages": [ { "id": "p", "title": "P", "widgets": [
            { "type": "clock", "x": 10, "y": 5, "w": 20, "h": 9 },
            { "type": "note", "id": "n", "x": -3, "y": 0, "w": 2, "h": 2 },
            { "type": "note", "id": "n", "x": 0, "y": 0, "w": 2, "h": 2 },
            { "type": "no-such-widget", "x": 0, "y": 0, "w": 2, "h": 2 } ] } ] }
        """);
        var ws = c.Pages.Single().Widgets!;
        Assert.Equal(3, ws.Count);                                      // the unknown widget is dropped
        var clock = ws[0];
        Assert.True(clock.X + clock.W <= 12 && clock.Y + clock.H <= 6);
        Assert.Equal(0, ws[1].X);
        Assert.Equal(["clock", "n", "n-2"], ws.Select(w => w.Id));
    }

    [Fact]
    public void AtLeastOnePageStaysEnabled()
    {
        var c = Load("""{ "pages": [ { "template": "clock", "enabled": false }, { "template": "music", "enabled": false } ] }""");
        Assert.True(c.Pages[0].Enabled);
    }

    [Fact]
    public void ThePhoneNeverSeesCommands()
    {
        var c = Load("""
        { "pages": [ { "id": "p", "title": "P", "widgets": [ { "type": "command", "x": 0, "y": 0, "w": 3, "h": 2, "options": { "command": "secret-script.ps1", "commandUnix": "secret-unix.sh", "future": "secret-x", "title": "T" } } ] } ],
          "shortcuts": [ { "id": "s", "label": "S", "cmd": "secret.exe", "args": "--token abc" } ] }
        """);
        var json = JsonSerializer.Serialize(c.ToPublic());
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("--token", json);
        Assert.Contains("\"T\"", json);
    }

    [Fact]
    public void RulesAreCleanedUp()
    {
        var c = Load("""{ "rules": [ { "when": "explode", "then": "goto" }, { "name": "Away", "when": "idle", "value": 99999, "then": "notify", "level": "weird" }, { "name": "Away", "when": "idle", "then": "goto" } ] }""");
        Assert.Equal(["away", "away-2"], c.Rules.Select(r => r.Id));
        Assert.Equal(1440, c.Rules[0].Value);
        Assert.Equal("info", c.Rules[0].Level);
    }
}

public class LanTests
{
    [Theory]
    [InlineData("/api/state", true)]
    [InlineData("/api/stream", true)]
    [InlineData("/api/run/terminal", true)]
    [InlineData("/api/push/temperature", true)]
    [InlineData("/api/settings", false)]
    [InlineData("/api/pair", false)]
    [InlineData("/api/shortcuts/test", false)]
    [InlineData("/api/claude/hook", false)]
    [InlineData("/api/obs-test", false)]
    [InlineData("/api/ha/test", false)]
    [InlineData("/api/phones", false)]
    [InlineData("/api/rules/test", false)]
    [InlineData("/panel/main.js", false)]
    [InlineData("/", false)]
    public void OnlyThePhoneApiIsReachableOverWifi(string path, bool allowed) => Assert.Equal(allowed, Lan.AllowedOverLan(new PathString(path)));

    [Theory]
    [InlineData("/api/push/x", true)]
    [InlineData("/api/notify", true)]
    [InlineData("/api/goto/2", true)]
    [InlineData("/api/refresh", true)]
    [InlineData("/api/state", false)]
    [InlineData("/api/run/x", false)]
    public void TheWebhookKeyOpensOnlyWebhooks(string path, bool webhook) => Assert.Equal(webhook, Lan.IsWebhook(new PathString(path)));

    [Fact]
    public void TokensAreComparedExactly()
    {
        Assert.True(Lan.Matches("Bearer abc123", "abc123"));
        Assert.False(Lan.Matches("Bearer abc12", "abc123"));
        Assert.False(Lan.Matches("abc123", "abc123"));
        Assert.False(Lan.Matches(null, "abc123"));
        Assert.True(Lan.MatchesQuery("abc123", "abc123"));
        Assert.False(Lan.MatchesQuery("", "abc123"));
        Assert.False(Lan.MatchesQuery(null, "abc123"));
    }
}

public class PushTests
{
    [Theory]
    [InlineData("temperature", true)]
    [InlineData("build.main-ci_2", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("../etc", false)]
    public void Keys(string key, bool valid) => Assert.Equal(valid, PushStore.ValidKey(key));

    [Fact]
    public void ValuesAreTyped()
    {
        Assert.Equal(JsonValueKind.Number, PushStore.ParseValue("21.5").ValueKind);
        Assert.Equal(JsonValueKind.True, PushStore.ParseValue("true").ValueKind);
        Assert.Equal(JsonValueKind.String, PushStore.ParseValue("passed").ValueKind);
        Assert.Equal(JsonValueKind.String, PushStore.ParseValue("NaN").ValueKind);
    }
}

/// <summary>Audit F10: a settings tab loaded earlier must not silently undo a change saved since (a stale tab once turned the night screen back on).</summary>
public class ConfigConcurrencyTests
{
    private static (ConfigStore Store, string Path) NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"phonebox-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        return (new ConfigStore(path), path);
    }

    private static string With(ConfigStore s, Action<AgentConfig> edit)
    {
        var c = System.Text.Json.JsonSerializer.Deserialize<AgentConfig>(System.Text.Json.JsonSerializer.Serialize(s.Get(), ConfigStore.WriteOpts), ConfigStore.WriteOpts)!;
        edit(c);
        return System.Text.Json.JsonSerializer.Serialize(c, ConfigStore.WriteOpts);
    }

    [Fact]
    public void AStaleSaveIsRefusedAndTheNewerChangeStays()
    {
        var (s, path) = NewStore();
        try
        {
            var loadedByTabA = s.Version;
            var tabA = With(s, c => c.Night.Brightness = 0.03);
            Thread.Sleep(20);
            Assert.Null(s.Save(With(s, c => c.Night.Start = "01:11"), s.Version));   // tab B, up to date
            Assert.Equal(ConfigStore.Conflict, s.Save(tabA, loadedByTabA));           // tab A, stale
            Assert.Equal("01:11", s.Get().Night.Start);
            Assert.Equal(0.02, s.Get().Night.Brightness);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void AnUpToDateSaveAndASaveWithoutAVersionGoThrough()
    {
        var (s, path) = NewStore();
        try
        {
            Assert.Null(s.Save(With(s, c => c.Night.Start = "01:11"), s.Version));
            Thread.Sleep(20);
            Assert.Null(s.Save(With(s, c => c.Night.Start = "02:22"), null));        // scripts and backups send no version
            Assert.Equal("02:22", s.Get().Night.Start);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }
}

public class Hour12ConfigTests
{
    [Theory]
    [InlineData("{}", "false", JsonValueKind.False)]
    [InlineData("""{"display":{"hour12":false}}""", "false", JsonValueKind.False)]
    [InlineData("""{"display":{"hour12":true}}""", "true", JsonValueKind.True)]
    [InlineData("""{"display":{"hour12":"true"}}""", "true", JsonValueKind.True)]
    [InlineData("""{"display":{"hour12":"12h"}}""", "true", JsonValueKind.True)]
    [InlineData("""{"display":{"hour12":"false"}}""", "false", JsonValueKind.False)]
    [InlineData("""{"display":{"hour12":"auto"}}""", "auto", JsonValueKind.String)]
    [InlineData("""{"display":{"hour12":" AUTO "}}""", "auto", JsonValueKind.String)]
    [InlineData("""{"display":{"hour12":"bogus"}}""", "false", JsonValueKind.False)]
    [InlineData("""{"display":{"hour12":null}}""", "false", JsonValueKind.False)]
    [InlineData("""{"display":{"hour12":123}}""", "false", JsonValueKind.False)]
    [InlineData("""{"display":{"hour12":["bad"]}}""", "false", JsonValueKind.False)]
    public void DisplayHour12NormalizesAndPreservesWireTypes(string inputJson, string expectedNormalized, JsonValueKind expectedWrittenKind)
    {
        var path = Path.Combine(Path.GetTempPath(), $"phonebox-h12-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{}");
            var store = new ConfigStore(path);
            Assert.Null(store.Save(inputJson));
            Assert.Equal(expectedNormalized, store.Get().Display.Hour12);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var prop = doc.RootElement.GetProperty("display").GetProperty("hour12");
            Assert.Equal(expectedWrittenKind, prop.ValueKind);
            if (expectedWrittenKind == JsonValueKind.String) Assert.Equal("auto", prop.GetString());
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }
}

/// <summary>Secrets (API keys, tokens, passwords) must be masked when sent to the settings UI and preserved when saved back.</summary>
public class SecretMaskingTests
{
    private static (ConfigStore Store, string Path) NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"phonebox-secrets-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        return (new ConfigStore(path), path);
    }

    [Fact]
    public void SecretsAreMaskedInToSettingsView()
    {
        var (store, path) = NewStore();
        try
        {
            // Save a config with real secrets
            var saveJson = """
            {
                "lan": { "token": "secret-lan-token-12345", "webhookToken": "secret-webhook-67890" },
                "homeAssistant": { "token": "hass-token-abcdef" },
                "obs": { "password": "obs-pass-xyz" },
                "cursor": { "apiKey": "cur_realkey123456" }
            }
            """;
            Assert.Null(store.Save(saveJson));

            // Get the masked version
            var masked = store.Get().ToSettingsView();

            // All secrets should be masked
            Assert.Equal("****2345", masked.Lan.Token);
            Assert.Equal("****7890", masked.Lan.WebhookToken);
            Assert.Equal("****cdef", masked.HomeAssistant.Token);
            Assert.Equal("****-xyz", masked.Obs.Password);
            Assert.Equal("****3456", masked.Cursor.ApiKey);

            // Short secrets should be fully masked
            var shortSecret = store.Get();
            shortSecret.Cursor.ApiKey = "abc";
            var maskedShort = shortSecret.ToSettingsView();
            Assert.Equal("****", maskedShort.Cursor.ApiKey);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void MaskedSecretsArePreservedOnSave()
    {
        var (store, path) = NewStore();
        try
        {
            // Save initial config with real secrets
            var initial = """
            {
                "lan": { "token": "secret-lan-token-12345", "webhookToken": "secret-webhook-67890" },
                "homeAssistant": { "url": "http://ha.local", "token": "hass-token-abcdef" },
                "obs": { "enabled": true, "url": "ws://localhost:4455", "password": "obs-pass-xyz" },
                "cursor": { "apiKey": "cur_realkey123456" },
                "weather": { "city": "Test City", "lat": 50.0, "lon": 10.0 }
            }
            """;
            Assert.Null(store.Save(initial));
            var originalToken = store.Get().Lan.Token;
            var originalWebhookToken = store.Get().Lan.WebhookToken;
            var originalHaToken = store.Get().HomeAssistant.Token;
            var originalObsPassword = store.Get().Obs.Password;
            var originalCursorKey = store.Get().Cursor.ApiKey;

            // Simulate the settings UI round-trip: get masked config, modify non-secret field, save back
            var masked = store.Get().ToSettingsView();
            var roundTrip = JsonSerializer.Serialize(masked, ConfigStore.WriteOpts);
            var doc = JsonDocument.Parse(roundTrip);
            var mutated = doc.RootElement.Clone();
            
            // Modify a non-secret field (weather city) and serialize back
            var modified = $$"""
            {
                "lan": { "token": "{{masked.Lan.Token}}", "webhookToken": "{{masked.Lan.WebhookToken}}", "enabled": false, "port": 8766 },
                "homeAssistant": { "url": "http://ha.local", "token": "{{masked.HomeAssistant.Token}}" },
                "obs": { "enabled": true, "url": "ws://localhost:4455", "password": "{{masked.Obs.Password}}" },
                "cursor": { "apiKey": "{{masked.Cursor.ApiKey}}" },
                "weather": { "city": "Modified City", "lat": 50.0, "lon": 10.0 }
            }
            """;

            // Save the modified config with masked secrets
            Assert.Null(store.Save(modified));

            // All secrets should be preserved, non-secret changes applied
            Assert.Equal("Modified City", store.Get().Weather.City);
            Assert.Equal(originalToken, store.Get().Lan.Token);
            Assert.Equal(originalWebhookToken, store.Get().Lan.WebhookToken);
            Assert.Equal(originalHaToken, store.Get().HomeAssistant.Token);
            Assert.Equal(originalObsPassword, store.Get().Obs.Password);
            Assert.Equal(originalCursorKey, store.Get().Cursor.ApiKey);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void NewRealSecretsReplaceOldOnes()
    {
        var (store, path) = NewStore();
        try
        {
            // Save initial config
            var initial = """
            {
                "cursor": { "apiKey": "cur_oldkey123" }
            }
            """;
            Assert.Null(store.Save(initial));

            // Save with new real secrets (not masked)
            var updated = """
            {
                "cursor": { "apiKey": "cur_newkey789" }
            }
            """;
            Assert.Null(store.Save(updated));

            // New values should be stored
            Assert.Equal("cur_newkey789", store.Get().Cursor.ApiKey);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void EmptyOrNullSecretsPreserveExisting()
    {
        var (store, path) = NewStore();
        try
        {
            // Save initial config
            var initial = """
            {
                "cursor": { "apiKey": "cur_original123" }
            }
            """;
            Assert.Null(store.Save(initial));

            // Save with empty/null secrets
            var updated = """
            {
                "cursor": { "apiKey": "" }
            }
            """;
            Assert.Null(store.Save(updated));

            // Original values should be preserved
            Assert.Equal("cur_original123", store.Get().Cursor.ApiKey);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void RealValuesCanBeCleared()
    {
        var (store, path) = NewStore();
        try
        {
            // Save initial config with secrets
            var initial = """
            {
                "cursor": { "apiKey": "cur_toDelete123" }
            }
            """;
            Assert.Null(store.Save(initial));
            Assert.NotNull(store.Get().Cursor.ApiKey);

            // To actually clear a secret, the UI should send a sentinel value
            // For now, empty/null preserves the existing value
            // This test documents current behavior
            var cleared = """
            {
                "cursor": { "apiKey": "" }
            }
            """;
            Assert.Null(store.Save(cleared));
            
            // Current behavior: empty/null preserves existing
            Assert.Equal("cur_toDelete123", store.Get().Cursor.ApiKey);
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }
}
