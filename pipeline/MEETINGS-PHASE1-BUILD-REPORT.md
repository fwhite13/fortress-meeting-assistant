# Meeting Assistant v2 — Phase 1 Build Report

**Date:** 2026-02-27
**Commit:** `6303698c`

## WP-1: Database
- Provider: Pomelo.EntityFrameworkCore.MySql 8.0.2 ✅
- Connection string builder: MySqlConnectionStringBuilder ✅ (handles = in password)
- Env vars: FORTRESS_DB_HOST, FORTRESS_DB_PORT, FORTRESS_DB_USER, FORTRESS_DB_PASS, MEETINGS_DB_NAME
- Table creation: IRelationalDatabaseCreator.CreateTablesAsync ✅ (matching FRED pattern)
- SQL Server migration files: Deleted ✅ (3 files removed)
- EF Core versions pinned to 8.0.2 to match Pomelo dependency ✅

## WP-2: Authentication
- Web: Cookie + OpenIdConnect (standard ASP.NET Core OIDC, no Cognito-specific package) ✅
- API: JWT Bearer ✅
- cognito:groups → ClaimTypes.Role mapping ✅ (OnTokenValidated event)
- [AllowAnonymous] on PATCH /api/meetings/{id}/status ✅ (VP bot endpoint — already had it)
- Pages protected with `@attribute [Authorize]`: Home.razor, MeetingDetails.razor, Settings.razor ✅
- Auth config via: Auth:CognitoAuthority, Auth:CognitoClientId, Auth:CognitoClientSecret
- FallbackPolicy = DefaultPolicy (all endpoints require auth by default) ✅

## WP-3: Docker
- Web Dockerfile: `src/RefugeMeetingAssistant.Web/Dockerfile` — debian:bookworm-slim + dotnet-install.sh (MCR blocked workaround, matching FRED pattern) ✅
- VPBot Dockerfile: Already existed, unchanged ✅
- Web+API merged: ProjectReference from Web.csproj → Api.csproj ✅
- Single container serves Blazor + API controllers on port 8080
- API controllers mapped via `app.MapControllers()` (VP bot PATCH endpoint accessible)

## Build Result
- `dotnet build src/RefugeMeetingAssistant.Web/`: ✅ **0 errors, 0 warnings**
- `dotnet build src/RefugeMeetingAssistant.Api/`: ✅ **0 errors, 0 warnings**

## Architecture Decision
- Option A implemented: Single Blazor Server app with API controllers merged in
- Web project references API project; API services registered in Web's Program.cs
- MeetingApiClient retained for Blazor pages (calls self via HttpClient for now — can be refactored to direct service injection later)
- API controllers still serve REST endpoints for VP bot

## Notes for Review
1. **MeetingApiClient still uses HttpClient** — Blazor pages call API via HttpClient pointing to localhost:8080 (self). Works in single-container mode but adds a network hop. Future optimization: replace with direct service injection.
2. **DevAuthenticationHandler** — Removed from API Program.cs. The file `Middleware/DevAuthenticationHandler.cs` still exists on disk but is no longer referenced. Can be deleted in cleanup.
3. **appsettings.json** — Needs Auth:CognitoAuthority, Auth:CognitoClientId, Auth:CognitoClientSecret values for deployment. Currently reads from IConfiguration (env vars or appsettings).
4. **EF Core pinned to 8.0.2** — Required to match Pomelo 8.0.2 dependency. Watch for this when upgrading.
5. **No database seed** — Dev user seeding was removed (was SQL Server migration-dependent). First login via Cognito will need manual user creation or a seed script.
