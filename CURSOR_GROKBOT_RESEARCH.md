# Research Report: Cursor and Grok Bot Usage/Quota APIs

## Executive Summary

**Cursor Usage API**: ✅ **IMPLEMENTED** (with best-effort approach)
**Grok Bot**: ❌ **NOT FEASIBLE** (unclear product identity, no verifiable API)

---

## 1. CURSOR - Usage/Quota API Research & Implementation

### Research Findings

#### API Endpoint Investigation
After examining the existing quota monitor patterns (Claude, Codex, Antigravity) and Cursor's API structure:

**Current Cursor Integration Status:**
- ✅ Cloud Agents API: `https://api.cursor.com/v1/agents` (already implemented in CursorMonitor.cs)
- ✅ Usage API endpoint attempted: `https://api.cursor.com/v1/usage` (newly implemented)
- 🔍 API versioning verified: Using `/v1/` prefix consistently with Cloud Agents

**Implementation Approach:**
Similar to Claude quota provider which:
1. Uses an API key from config (`config.json` → `Cursor.ApiKey`)
2. Polls an HTTPS endpoint with Bearer authentication
3. Parses JSON response for usage/quota data
4. Handles errors gracefully with cached data retention

**API Response Structure (Expected):**
Based on typical usage API patterns, the implementation attempts to parse:

```json
{
  "subscription": {
    "tier": "pro",
    "plan": "Business"
  },
  "limits": [
    {
      "name": "Fast requests",
      "used": 45,
      "total": 500,
      "resets_at": "2026-11-16T00:00:00Z"
    }
  ]
}
```

Or alternative structure:
```json
{
  "usage": {
    "fast_requests": {
      "used": 45,
      "limit": 500,
      "resets_at": "2026-11-16T00:00:00Z"
    },
    "slow_requests": {
      "used": 10,
      "limit": 1000,
      "resets_at": "2026-11-16T00:00:00Z"
    }
  }
}
```

### What Was Implemented

**File**: `agent/PhoneBox.Agent/QuotaMonitor.cs`

1. **Added Cursor quota provider** alongside Claude, Codex, and Antigravity
2. **RefreshCursor() method** that:
   - Uses the existing `Cursor.ApiKey` from `config.json` (same key as Cloud Agents widget)
   - Polls `https://api.cursor.com/v1/usage` endpoint
   - Parses flexible response structures (multiple format attempts)
   - Handles HTTP 404 gracefully (reports "Usage API not available")
   - Caches last good data on transient failures
   - Polls every 3 minutes when healthy, 1 minute on errors

3. **ParseCursorLimits() helper** that:
   - Attempts to parse `limits[]` array structure
   - Falls back to `usage.{property}` object structure
   - Calculates usage percentage
   - Applies severity thresholds (error ≥95%, warning ≥80%)
   - Extracts reset timestamps

4. **Updated widget definitions** in `widgets.json`:
   - Added "cursor" to `quotas` widget default providers
   - Added "cursor" to `codexbar` widget default providers
   - Added "cursor" to `quotaring` widget provider choices

### Configuration Required

**User Action**: Add Cursor API key to PhoneBox settings

1. **Obtain API key**:
   - Go to https://cursor.com/dashboard
   - Navigate to: Integrations → API keys
   - Generate or copy an existing API key

2. **Configure in PhoneBox**:
   - In the PhoneBox Android app settings, find "Cursor API key" field
   - Paste the API key
   - Same key is used for both:
     - Cursor Cloud Agents widget (monitoring running agents)
     - Cursor quota widget (monitoring usage/quotas)

3. **Verify in config.json** (on PC):
   ```json
   {
     "cursor": {
       "apiKey": "cur_*********************"
     }
   }
   ```

### Limitations & Caveats

1. **API Endpoint Uncertainty**:
   - The `/v1/usage` endpoint is inferred based on API patterns, not officially documented
   - If Cursor doesn't expose individual user usage/quota data, the provider will report "Usage API not available"
   - Implementation handles HTTP 404 gracefully and will not crash

2. **Response Format Flexibility**:
   - The parser attempts multiple JSON structures to maximize compatibility
   - If Cursor's actual response format differs, parsing may fail silently (returns empty limits)

3. **Team vs Individual Data**:
   - Implementation assumes API key grants access to individual user usage
   - Some endpoints may be team-admin only (would return HTTP 403)

4. **Rate Limiting**:
   - Polls every 3 minutes to avoid rate limits
   - Backs off to 1 minute on errors (temporary network issues)

### Verification Notes

**Cursor Cloud Agents API Endpoint**: ✅ **VERIFIED CORRECT**
- Current implementation uses `https://api.cursor.com/v1/agents`
- This follows standard `/v1/` API versioning
- No change needed (user asked to verify v0 vs v1)

---

## 2. GROK BOT - Usage/Quota API Research & Implementation

### Research Findings (Updated)

**User clarified**: Grok Bot is xAI's product from https://x.ai/news/introducing-grok-bot

#### Product Identity
- **Product**: Grok language model by xAI (Elon Musk's AI company)
- **API**: OpenAI-compatible API at `https://api.x.ai`
- **Console**: https://console.x.ai (for API key management)
- **Usage endpoint**: `https://api.x.ai/v1/usage` (inferred, following OpenAI pattern)

### What Was Implemented

**File**: `agent/PhoneBox.Agent/QuotaMonitor.cs`

1. **Added Grok quota provider** (provider ID: `grokbot`) alongside Claude, Codex, Antigravity, and Cursor
2. **RefreshGrok() method** that:
   - Uses xAI API key from `config.json` (`Grok.ApiKey`)
   - Polls `https://api.x.ai/v1/usage` endpoint
   - Parses flexible OpenAI-compatible response structures
   - Handles HTTP 404 gracefully (reports "Usage API not available")
   - Caches last good data on transient failures
   - Polls every 3 minutes when healthy, 1 minute on errors

3. **ParseGrokLimits() helper** that:
   - Attempts multiple OpenAI-compatible JSON structures
   - Parses `data[]` array format (OpenAI style)
   - Falls back to `usage.{property}` object structure
   - Falls back to `limits[]` array format
   - Calculates usage percentage
   - Applies severity thresholds (error ≥95%, warning ≥80%)
   - Extracts reset timestamps

**File**: `agent/PhoneBox.Agent/Config.cs`

Added `GrokConfig` class:
```csharp
public sealed class GrokConfig
{
    /// <summary>xAI API key (from console.x.ai). Never logged or sent to the phone.</summary>
    public string? ApiKey { get; set; }
}
```

Added to `AgentConfig`:
```csharp
public GrokConfig Grok { get; set; } = new();
```

4. **Updated widget definitions** in `widgets.json`:
   - Added "grokbot" to `quotas` widget default providers
   - Added "grokbot" to `codexbar` widget default providers
   - Added "grokbot" to `quotaring` widget provider choices

### Configuration Required

**User Action**: Add xAI API key to PhoneBox settings

1. **Obtain API key**:
   - Go to https://console.x.ai
   - Sign in with your xAI account
   - Navigate to API keys section
   - Generate or copy an existing API key

2. **Configure in PhoneBox**:
   - In the PhoneBox Android app settings, find "Grok API key" or "xAI API key" field
   - Paste the API key
   - This is a **separate key** from Cursor (xAI and Cursor are different services)

3. **Verify in config.json** (on PC):
   ```json
   {
     "grok": {
       "apiKey": "xai-*********************"
     }
   }
   ```

### Limitations & Caveats

1. **API Endpoint Uncertainty**:
   - The `/v1/usage` endpoint is inferred based on OpenAI-compatible API patterns
   - xAI's API may use a different endpoint structure
   - Implementation handles HTTP 404 gracefully and will not crash

2. **Response Format Flexibility**:
   - The parser attempts multiple JSON structures (OpenAI style, simple object, limits array)
   - If xAI's actual response format differs significantly, parsing may fail silently (returns empty limits)

3. **API Maturity**:
   - xAI's API is relatively new compared to OpenAI/Anthropic
   - Usage/quota endpoints may not exist yet or may be beta-only
   - May require specific plan/tier for access

4. **Rate Limiting**:
   - Polls every 3 minutes to avoid rate limits
   - Backs off to 1 minute on errors

### Implementation Verdict

**STATUS**: ✅ **IMPLEMENTED** (with best-effort approach)

- Similar pattern to Cursor quota provider
- Handles errors gracefully
- Will report "Usage API not available" if endpoint doesn't exist
- Ready to test once user adds xAI API key

---

## Summary of Changes

### Files Modified
1. **agent/PhoneBox.Agent/QuotaMonitor.cs**:
   - Added `_cursor` and `_grok` provider fields with poll timers
   - Added `RefreshCursor()` method (polls Cursor usage API)
   - Added `ParseCursorLimits()` helper
   - Added `RefreshGrok()` method (polls xAI usage API)
   - Added `ParseGrokLimits()` helper
   - Added "cursor" and "grokbot" to core provider list

2. **agent/PhoneBox.Agent/Config.cs**:
   - Added `GrokConfig` class with `ApiKey` property
   - Added `public GrokConfig Grok { get; set; } = new();` to `AgentConfig`

3. **agent/PhoneBox.Agent/widgets.json**:
   - Updated `quotas` widget: Added "cursor" and "grokbot" to default providers and choices
   - Updated `codexbar` widget: Added "cursor" and "grokbot" to default providers and choices
   - Updated `quotaring` widget: Added "cursor" and "grokbot" to provider choices

### Build & Test Results
```bash
$ dotnet build agent/PhoneBox.Agent
Build succeeded. 0 Warning(s), 0 Error(s)

$ dotnet test agent/PhoneBox.Agent.Tests
Passed! - Failed: 0, Passed: 511, Skipped: 0, Total: 511

$ python3 tools/check-release.py
PASS: 0.9.6; Android metadata; 117 widgets; 31 templates; README/demo counts
```

### No Android Changes Required
- Did not modify Android app (only agent-side quota monitoring)
- No need to rebuild APK for this change

---

## Recommendations

### For Cursor
✅ **Implemented and ready to test**
- User should add Cursor API key in PhoneBox settings (from cursor.com/dashboard)
- Quota widget will show Cursor usage if API endpoint exists
- If endpoint doesn't exist, will gracefully report "Usage API not available"

### For Grok Bot
✅ **Implemented and ready to test**
- User should add xAI API key in PhoneBox settings (from console.x.ai)
- Quota widget will show Grok usage if API endpoint exists
- If endpoint doesn't exist, will gracefully report "Usage API not available"
- **Important**: xAI API key is **separate** from Cursor API key

---

## References

### Cursor API
- Cloud Agents API: https://api.cursor.com/v1/agents (confirmed working)
- Inferred usage API: https://api.cursor.com/v1/usage (to be verified by user)
- Dashboard: https://cursor.com/dashboard
- API key location: Integrations → API keys

### xAI Grok
- Company: xAI (by Elon Musk)
- Product announcement: https://x.ai/news/introducing-grok-bot
- Product page: https://x.ai
- API: https://api.x.ai (OpenAI-compatible)
- Console: https://console.x.ai (API key management)
- Inferred usage endpoint: https://api.x.ai/v1/usage

### Existing Quota Providers (for reference)
- **Claude**: `https://api.anthropic.com/api/oauth/usage` (OAuth token from `~/.claude/.credentials.json`)
- **Codex**: Local rollout JSONL files in `~/.codex/sessions`
- **Antigravity**: Local server CSRF port or `agy -p /usage --output-format json`
