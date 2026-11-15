# Cursor Cloud Agents Monitoring

This document describes the Cursor Cloud Agents monitoring widget in PhoneBox.

## Overview

The **Cursor** widget monitors Cursor Cloud Agents activity via the Cursor Cloud API, displaying running agents with their status, elapsed time, and details.

## Implementation

### Backend (.NET)

**File**: `agent/PhoneBox.Agent/CursorMonitor.cs`

A background service that:
- Polls the Cursor Cloud Agents API (`https://api.cursor.com/v1/agents`)
- Uses Bearer token authentication with Cursor API key
- Polls every 30-60 seconds with backoff on errors/429
- Detects if Cursor IDE is running locally (process check)
- Exposes agent data via `/api/work` endpoint

**Key Data Structures:**
```csharp
public sealed record CursorAgent(
    string Id, 
    string Title, 
    string Status, 
    string? Repo, 
    string? Branch, 
    string? PrUrl, 
    long UpdatedAt, 
    long CreatedAt
);
```

### Frontend (Android Panel)

**File**: `android/app/src/main/assets/www/widgets/pc.js`

Widget `W.cursor` that:
- Shows running agents first with status dots (running/finished/error)
- Displays elapsed time for each agent
- Shows tap-to-inspect sheet with full details
- Indicates if Cursor IDE is running on the PC
- Handles empty/unconfigured/error states

**Styles**: `android/app/src/main/assets/www/style.css` (`.cu-*` classes)

### Configuration

**Config Structure** (`agent/PhoneBox.Agent/Config.cs`):
```csharp
public sealed class CursorConfig
{
    /// <summary>Cursor Cloud API key (from cursor.com dashboard › Integrations › API keys).</summary>
    public string? ApiKey { get; set; }
}
```

**Settings UI**: 
- Location: `http://127.0.0.1:8765` › General tab
- Section: "Cursor Cloud Agents"
- Field: Cursor API key (password type, masked as `****xyz1` when displayed)
- Link to: https://cursor.com/dashboard › Integrations › API keys

**In config.json**:
```json
{
  "cursor": {
    "apiKey": "cur_*********************"
  }
}
```

### Widget Catalog

**File**: `agent/PhoneBox.Agent/widgets.json`

```json
{
  "type": "cursor",
  "category": "AI & code",
  "name": "Cursor",
  "icon": "🖱️",
  "desc": "Cursor Cloud Agents running now",
  "w": 6,
  "h": 4
}
```

### Page Template

**File**: `agent/PhoneBox.Agent/templates.json`

Template ID: `cursor-agents`
- Includes: `cursor`, `sessions`, `github` widgets
- Purpose: Monitor Cursor activity, coding sessions, and GitHub status

## Features

1. **Agent Monitoring**:
   - Lists recent agents with title and repository
   - Shows status: running (green), finished (blue), error (red)
   - Displays elapsed time since agent started
   - Tap agent for details sheet with repo, branch, PR URL

2. **IDE Detection**:
   - Best-effort detection if Cursor IDE process is running on PC
   - Cross-platform (Windows/macOS/Linux)
   - No fragile sqlite DB parsing

3. **Error Handling**:
   - Empty state: "No Cursor API key configured"
   - No agents: "No recent agents"
   - API errors: Shows error message
   - Network failures: Automatic retry with backoff

## API Endpoint

**Cursor Cloud Agents API**:
- Base URL: `https://api.cursor.com`
- Endpoint: `/v1/agents`
- Auth: Bearer token (Cursor API key)
- Documentation: https://docs.cursor.com/en/background-agent/api/overview

**Verified Correct**: Uses `/v1/agents` (not `/v0/agents`)

## Security

- API key stored in `config.json` on PC (never logged)
- Never sent to Android phone
- Masked in Settings UI (shows `****xyz1`)
- Preserved when saving settings (PreserveSecret logic)
- Similar security model to Home Assistant token and OBS password

## Limitations

1. **Process Detection**: Best-effort only
   - Checks for `cursor` / `Cursor.exe` process
   - May miss some running instances
   - Does not parse private state databases

2. **API Rate Limiting**:
   - Polls every 30-60s to stay within limits
   - Backs off on 429 errors
   - No real-time updates

3. **Individual User Only**:
   - Shows agents for the API key owner
   - Not team-wide or admin view

## Related Files

**Backend**:
- `agent/PhoneBox.Agent/CursorMonitor.cs` - Background service
- `agent/PhoneBox.Agent/Config.cs` - CursorConfig class
- `agent/PhoneBox.Agent/Program.cs` - Service registration, `/api/work` endpoint

**Frontend**:
- `android/app/src/main/assets/www/widgets/pc.js` - Widget rendering
- `android/app/src/main/assets/www/style.css` - Widget styles

**Configuration**:
- `agent/PhoneBox.Agent/widgets.json` - Widget definition
- `agent/PhoneBox.Agent/templates.json` - Page template
- `agent/PhoneBox.Agent/settings.html` - Settings UI

**Documentation**:
- `README.md` - Widget count
- `CHANGELOG.md` - Release notes

## Build & Test

```bash
$ dotnet build agent/PhoneBox.Agent
$ dotnet test agent/PhoneBox.Agent.Tests
$ python3 tools/check-release.py
```

All checks pass with this implementation.
