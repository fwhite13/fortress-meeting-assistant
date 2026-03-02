# Review Report: MEETINGS-PHASE1 (WP-1/2/3)

**Reviewer:** Hawkeye  
**Commit:** `6303698c`  
**Date:** 2026-02-27  
**Priority:** HIGH

### Verdict: NEEDS-CHANGES

One blocking issue before this goes to Rhodey. One important item worth Tony's attention. Everything else is clean.

---

## Consistency Audit

**Files Cross-Referenced:**

| Check | Result |
|---|---|
| `FORTRESS_DB_HOST/PORT/USER/PASS/MEETINGS_DB_NAME` — Web/Program.cs ↔ Api/Program.cs ↔ spec §2.6 | ✅ Match |
| `MySqlServerVersion(8, 0, 28)` — Web ↔ Api ↔ both DbContextFactory and DbContext registrations | ✅ Match (×4) |
| `Auth:CognitoAuthority/ClientId/ClientSecret` — Web/Program.cs ↔ build report | ✅ Match |
| `Auth__Cognito*` spec env var names ↔ code `Auth:Cognito*` config keys | ✅ Correct — ASP.NET Core maps `__` env vars to `:` config keys by convention |
| `[AllowAnonymous]` on `PATCH /api/meetings/{id}/status` — MeetingsController ↔ spec | ✅ Present |
| `ValidateAudience = false` — Api/Program.cs JWT Bearer config ↔ spec | ✅ Present |
| `CallbackPath = "/signin-oidc"` ↔ spec | ✅ Present |
| SQL Server migration files deleted — diff stat shows 3 files removed | ✅ No orphaned refs in `/src/` |
| `ENTRYPOINT ["dotnet", "RefugeMeetingAssistant.Web.dll"]` ↔ spec | ✅ Correct |
| `EXPOSE 8080` ↔ ECS task def spec | ✅ Present |
| Base image — NOT `mcr.microsoft.com` | ✅ `debian:bookworm-slim` used (matching FRED pattern) |

**Undocumented Dependencies:** None found.

---

## Critical Issues — 1

### C1: `/health` endpoint not anonymous — ECS health checks will fail in production

- **File:** `src/RefugeMeetingAssistant.Web/Program.cs` (line 152)
- **Category:** Correctness / auth misconfiguration
- **Issue:** The Web app sets `FallbackPolicy = options.DefaultPolicy`, which requires authentication on every endpoint that doesn't opt out. The `/health` minimal API endpoint has no `.AllowAnonymous()` call, so the ECS/ALB health checker (no auth token) will get a 302 redirect to the Cognito login page instead of a 200. ECS will mark the task unhealthy, replace it, and the new task will fail too. Deployment to production will loop indefinitely.

**Evidence:**
```csharp
// Web/Program.cs — FallbackPolicy requiring auth on all endpoints:
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;  // ← requires auth everywhere
});

// ...and the health endpoint with no exemption:
app.MapGet("/health", () => Results.Ok(new { status = "healthy", ... }));
//                                                                        ^ missing .AllowAnonymous()
```

**Fix:**
```diff
- app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "meetings", timestamp = DateTime.UtcNow }));
+ app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "meetings", timestamp = DateTime.UtcNow }))
+     .AllowAnonymous();
```

---

## Important Issues — 1

### I1: JWT Bearer is dead code in the merged single-container architecture

- **File:** `src/RefugeMeetingAssistant.Api/Program.cs`
- **Category:** Correctness / architectural clarity
- **Issue:** `Api/Program.cs` configures JWT Bearer auth (with `ValidateAudience = false`, the correct Cognito setting), but this file is **never executed** in the merged architecture. The Web project references the API as a class library — only `Web/Program.cs` runs when `dotnet RefugeMeetingAssistant.Web.dll` starts. The merged app has **no JWT Bearer configured**: all endpoints use Cookie auth from the Web app's OIDC setup.

  In practice this is fine for Phase 1 because:
  - VP bot calls the `[AllowAnonymous]` PATCH endpoint — no auth required
  - Portal users hit Blazor pages — Cookie auth works
  
  But it means any future caller that tries to use a JWT Bearer token against the API controllers will get a 302 to Cognito login, not a 401. This is a trap for WP-4+ work.

**Not blocking Phase 1**, but Tony should be aware and either:
- Add JWT Bearer to `Web/Program.cs` alongside the Cookie scheme (so the merged app accepts both), or
- Document that the API only accepts Cookie auth in single-container mode

---

## Nitpicks — 0

Nothing worth noting.

---

## Acceptance Criteria Verification

### WP-1: MySQL Migration
- [x] `MySqlConnectionStringBuilder` used for all connection strings — handles `=` in passwords correctly ✅
- [x] `IRelationalDatabaseCreator.CreateTablesAsync()` called after `app.Build()`, before `app.Run()` ✅
- [x] `HasTablesAsync()` guard in place — won't recreate if tables already exist ✅
- [x] `MySqlServerVersion(new Version(8, 0, 28))` — consistent in both Web and Api Program.cs (×4) ✅
- [x] SQL Server migration files deleted — 3 files removed in diff, no orphaned `UseSqlServer` references in `/src/` ✅
- [x] Env vars match spec §2.6: `FORTRESS_DB_HOST`, `FORTRESS_DB_PORT`, `FORTRESS_DB_USER`, `FORTRESS_DB_PASS`, `MEETINGS_DB_NAME` ✅

### WP-2: Authentication
- [x] Every non-anonymous endpoint protected — `FallbackPolicy = DefaultPolicy` in Web app ✅
- [ ] `/health` endpoint protected — **BLOCKED**: FallbackPolicy applies, missing `.AllowAnonymous()` ❌ (see C1)
- [x] `PATCH /api/meetings/{id}/status` is `[AllowAnonymous]` — VP bot requirement met ✅
- [x] `cognito:groups` → `ClaimTypes.Role` mapping in `OnTokenValidated` event ✅
- [x] No hardcoded secrets — all credentials from `IConfiguration` / env vars ✅
- [x] `/signin-oidc` callback path ✅
- [x] `ValidateAudience = false` in JWT Bearer config ✅ (note: this config is in dead code per I1, but the Web app doesn't need it for portal use case)
- [x] Auth config keys: `Auth:CognitoAuthority`, `Auth:CognitoClientId`, `Auth:CognitoClientSecret` — correct ASP.NET Core colon notation, maps from `Auth__Cognito*` env vars per spec ✅

### WP-3: Docker
- [x] Dockerfile builds both Web + API — `COPY src/ .` brings both projects in; Web.csproj has ProjectReference to Api.csproj ✅
- [x] COPY pattern includes `src/RefugeMeetingAssistant.Web/` and `src/RefugeMeetingAssistant.Api/` csproj files ✅
- [x] `EXPOSE 8080` ✅
- [x] `ENTRYPOINT ["dotnet", "RefugeMeetingAssistant.Web.dll"]` ✅
- [x] MCR not used — `debian:bookworm-slim` + `dotnet-install.sh` (FRED pattern) ✅

---

## Positive Observations

- **`MySqlConnectionStringBuilder` pattern** is exactly right. String interpolation with `=` in passwords is a classic bug and Tony dodged it cleanly.
- **`HasTablesAsync()` → `CreateTablesAsync()`** pattern with a try/catch that lets the app start anyway is solid defensive coding. Matches FRED exactly.
- **`[AllowAnonymous]` on method + `[Authorize]` on class** for the VP bot endpoint is the right layered approach — the class-level default is secure, the one exception is explicit.
- **Dockerfile** is correct. The two-stage build with `COPY src/ .` is the right pattern for a multi-project solution. Runtime image has `ASPNETCORE_URLS=http://+:8080` matching the `EXPOSE`.
- **`cognito:groups` → `ClaimTypes.Role` mapping** is clean and correct. Belt-and-suspenders with both `RoleClaimType` and the manual `AddClaim` is fine.

---

## Summary

One-line fix needed on the `/health` endpoint. Everything else in WP-1 and WP-3 is clean. Auth is correct where it matters (VP bot anonymous, portal protected, no hardcoded secrets). The JWT Bearer dead code is a heads-up for Tony, not a blocker.

**Fix C1, re-submit.**
