# User Portal Build Report

**Builder:** Tony Stark (software-engineer)  
**Date:** 2026-02-25  
**Status:** ✅ COMPLETE  
**Build time:** ~1.5 hours  

---

## What Was Built

A complete Blazor Server (.NET 8) web portal for the Refuge Meeting Assistant, implementing all 6 user stories from the task brief.

### User Stories Implemented

| # | Story | Status |
|---|-------|--------|
| 1 | **User Authentication** — Entra ID SSO (+ dev bypass) | ✅ |
| 2 | **Dashboard — My Meetings** — Paginated meeting list with stats | ✅ |
| 3 | **Meeting Detail View** — Summary, decisions, action items, searchable transcript | ✅ |
| 4 | **Bot Configuration** — Settings form with validation, save/cancel | ✅ |
| 5 | **Join Meeting** — Modal with URL validation, redirects to detail | ✅ |
| 6 | **Active Meeting Status** — Live recordings with elapsed timer, stop button, 30s auto-refresh | ✅ |

---

## File Structure

```
src/RefugeMeetingAssistant.Web/
├── Program.cs                          — App entry, auth config, DI
├── RefugeMeetingAssistant.Web.csproj   — Project file with Identity packages
├── appsettings.json                    — Entra ID config (TenantId, ClientId)
├── appsettings.Development.json        — Dev mode with auth bypass
├── Properties/
│   └── launchSettings.json             — Port 5001 config
├── Components/
│   ├── App.razor                       — HTML shell
│   ├── Routes.razor                    — Auth-aware routing
│   ├── _Imports.razor                  — Global usings
│   ├── Layout/
│   │   ├── MainLayout.razor            — Layout with auth header (sign in/out)
│   │   ├── MainLayout.razor.css        — Layout scoped styles
│   │   ├── NavMenu.razor               — Sidebar navigation
│   │   └── NavMenu.razor.css           — Nav scoped styles
│   ├── Pages/
│   │   ├── Home.razor                  — Dashboard (meetings, stats, active recordings)
│   │   ├── MeetingDetails.razor        — Full meeting view (tabs: summary/transcript/actions)
│   │   ├── Settings.razor              — Bot configuration form
│   │   └── Error.razor                 — Error page
│   └── Shared/
│       ├── MeetingCard.razor           — Meeting list item component
│       ├── MeetingStatusBadge.razor    — Color-coded status badge
│       ├── ActiveRecordingCard.razor   — Live recording with elapsed timer
│       ├── JoinMeetingModal.razor      — Join meeting modal form
│       └── Pagination.razor            — Page navigation component
├── Services/
│   └── MeetingApiClient.cs             — API wrapper + all DTOs
└── wwwroot/
    ├── app.css                         — Professional custom styles
    ├── bootstrap/                      — Bootstrap 5 CSS
    ├── favicon.png
    └── js/
        └── site.js                     — JS helpers (clipboard, scroll)
```

---

## How to Run Locally

### Prerequisites
- .NET 8 SDK
- API running on port 5000 (with SQL Server + Docker)

### Quick Start

```bash
# 1. Start infrastructure (SQL Server via Docker)
cd /home/fredw/.openclaw/workspace/meeting-assistant-aws
docker-compose up -d

# 2. Start the API (port 5000)
cd src/RefugeMeetingAssistant.Api
dotnet run

# 3. Start the Web Portal (port 5001) — new terminal
cd src/RefugeMeetingAssistant.Web
dotnet run --urls "http://localhost:5001"

# 4. Open browser: http://localhost:5001
# 5. Dev mode auto-logs in — click "Sign In" or navigate to /auth/dev-login
```

### Dev Auth Bypass
In Development environment (`appsettings.Development.json` has `Auth:UseDev: true`):
- No Entra ID app registration needed
- `/auth/dev-login` auto-authenticates as "Dev User"
- API calls use `X-User-Id` header + dev Bearer token
- `/auth/logout` signs out

---

## Authentication Architecture

### Development Mode
- Cookie-based auth with auto-login endpoint
- No external dependencies (no Azure AD needed)
- Dev user: `dev@refugems.com` / ID `00000000-0000-0000-0000-000000000001`

### Production Mode (Entra ID)
- Microsoft Identity Web (`Microsoft.Identity.Web` + `Microsoft.Identity.Web.UI`)
- OpenID Connect flow to Azure AD
- Requires app registration (see below)

### Entra App Registration Steps (for Fred)

1. **Go to** Azure Portal → Microsoft Entra ID → App registrations → New registration
2. **Name:** `Refuge Meeting Assistant Portal`
3. **Supported account types:** Single tenant (Refuge Group only)
4. **Redirect URI:** Web → `https://<your-domain>/signin-oidc`
5. **After creation:**
   - Copy **Application (client) ID** → paste in `appsettings.json` → `AzureAd:ClientId`
   - Copy **Directory (tenant) ID** → paste in `appsettings.json` → `AzureAd:TenantId`
   - Go to **Certificates & secrets** → New client secret → copy value
   - Go to **Authentication** → Add `https://<your-domain>/signout-callback-oidc` to logout URL
   - Go to **API permissions** → Add permission for your API if needed
6. **Update `appsettings.json`:**
   ```json
   {
     "AzureAd": {
       "Instance": "https://login.microsoftonline.com/",
       "TenantId": "<your-tenant-id>",
       "ClientId": "<your-client-id>",
       "ClientSecret": "<your-client-secret>",
       "CallbackPath": "/signin-oidc"
     }
   }
   ```
7. **Set `Auth:UseDev` to `false`** in production appsettings
8. **Store client secret** in environment variable or Azure Key Vault (never commit)

---

## Key Implementation Decisions

1. **Blazor Web App template** (not Blazor Server) — uses the .NET 8 modern template with `@rendermode InteractiveServer` per-page, giving better SSR + interactive support.

2. **Separate shared components** — `MeetingCard`, `MeetingStatusBadge`, `ActiveRecordingCard`, `JoinMeetingModal`, `Pagination` are reusable components in `/Components/Shared/`.

3. **Auto-refresh** — Dashboard refreshes every 30 seconds via `System.Threading.Timer`. Meeting detail page refreshes every 10 seconds while in active/processing state, then stops.

4. **Elapsed time counter** — `ActiveRecordingCard` has a 1-second timer showing real-time elapsed duration.

5. **Transcript search** — Client-side search with highlight using `<mark>` tags and case-insensitive matching.

6. **Dev auth bypass** — Clean separation between dev mode (cookie auth, auto-login endpoint) and production (Entra ID). Controlled by `Auth:UseDev` config.

7. **API client auth** — In dev mode, passes `X-User-Id` header + dummy Bearer token. In prod mode, ready for Microsoft Identity Web token acquisition.

---

## Known Limitations

1. **No WebSocket/SignalR for real-time updates** — Uses polling (30s dashboard, 10s meeting detail). WebSocket integration is a future enhancement.

2. **Dev auth only** — Entra ID SSO is configured but requires app registration. The `ClientId` in appsettings.json is a placeholder.

3. **No offline/error retry** — API calls fail silently in some cases. Production should add retry policies (Polly).

4. **No file export** — Can't download transcripts or summaries as files yet.

5. **Transcript search is client-side only** — Works for displayed text but doesn't search across pages. Server-side search endpoint exists in the API.

6. **No unit tests** — Blazor component testing requires bUnit; this would be a follow-up task.

---

## Acceptance Criteria Checklist

- [x] Blazor Server project created and added to solution
- [x] Entra ID auth configured (ready for app registration)
- [x] API client service with all endpoints
- [x] Dashboard page shows meetings list with stats
- [x] Meeting detail page shows summary, decisions, action items, transcript (with search)
- [x] Settings page shows bot config form with save/cancel and validation
- [x] Join Meeting modal functional with URL validation
- [x] Active Recordings section on dashboard with live elapsed time
- [x] Professional UI (Bootstrap 5, clean design)
- [x] Responsive layout (works on mobile)
- [x] Zero build warnings/errors
- [x] Runs locally: `dotnet run --urls "http://localhost:5001"`
- [x] Auth flow tested: dev login → cookie → authenticated pages

---

## Next Steps

1. **Configure Entra app registration** (Fred) — Follow steps above
2. **Deploy to ECS** — Dockerfile needed (or add to existing docker-compose)
3. **Add bUnit tests** — Component testing for critical pages
4. **WebSocket/SignalR** — Real-time updates instead of polling
5. **Export features** — Download transcript/summary as PDF or DOCX
6. **Search page** — Full-text search across all meetings (API supports it)
7. **Production hardening** — Error boundaries, retry policies, logging

---

*Built with .NET 8, Blazor Interactive Server, Bootstrap 5, Microsoft.Identity.Web*
