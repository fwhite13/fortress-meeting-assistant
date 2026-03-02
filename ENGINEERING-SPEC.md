# Fortress Meeting Assistant v2 — Engineering Specification

**Version:** 2.0  
**Date:** February 27, 2026  
**Author:** Engineering Pipeline  
**Status:** Ready for Implementation

---

## 1. Current State

This is an existing working POC running on SteamServer. The codebase is at `/meeting-assistant-aws/`. This spec covers getting it into the standard Fortress DEV/PROD deployment model (matching FRED and FormIQ) and adding v2 features.

### 1.1 VP Bot — `src/RefugeMeetingAssistant.VPBot/`

**Runtime:** Node.js 20 + Playwright + headless Chromium (headed mode required for Teams)

**Platform Support:**

| Platform | Status | Handler |
|----------|--------|---------|
| Microsoft Teams | ✅ Working | `teams.ts` — full join flow: launcher page click-through, pre-join screen, name entry, device toggle, waiting room handling |
| Zoom | ❌ Stub | `zoom.ts` — throws "not yet supported" |
| Google Meet | ❌ Stub | `google-meet.ts` — throws "not yet supported" |

**Audio Capture Mechanism:**
- Uses **PulseAudio virtual sink + FFmpeg** — not DOM-based capture
- Dockerfile creates a `virtual_out` null sink via `pactl load-module module-null-sink`
- Browser routes all WebRTC audio through PulseAudio
- FFmpeg records from `virtual_out.monitor` source → WAV (16kHz mono, optimal for speech-to-text)
- Xvfb provides a virtual X11 display (`:99`, 1920×1080)

**Operation Modes:**
- **Worker mode (default):** Polls SQS for `BotCommand` messages (join/stop), launches Playwright sessions, reports status back to .NET API via `PATCH /api/meetings/{id}/status`
- **HTTP mode:** Express server on port 3500 with `POST /api/meetings/join` and `GET /api/health` — used for dev/testing

**Current Deployment:** Docker container via `docker-compose.yml`. The VP bot Dockerfile is a multi-stage build: Node.js builder → Ubuntu 24.04 runtime with Playwright, PulseAudio, Xvfb, FFmpeg, and dbus.

**LMA Integration (vestigial):** Worker has TODO placeholders for streaming audio to Kinesis Data Stream. Currently records locally to `/app/recordings/{meetingId}.wav`. The Kinesis/LMA integration is **not implemented** — this is dead code from the original LMA-based architecture.

### 1.2 API — `src/RefugeMeetingAssistant.Api/`

**Runtime:** .NET 8 ASP.NET Core Web API

**Database Provider:** `Microsoft.EntityFrameworkCore.SqlServer` (8.0.x) — configured for SQL Server in `appsettings.json`:
```
Server=localhost,1433;Database=RefugeMeetingAssistant;User Id=sa;Password=${DB_PASSWORD}
```

**Current EF Core Entities (4 tables):**

| Entity | Table | Purpose |
|--------|-------|---------|
| `User` | `Users` | User identity — has `EntraObjectId`, `CognitoUserId` (nullable), `Email`, `DisplayName` |
| `BotConfig` | `BotConfigs` | Per-user bot settings (1:1 with User) — bot name, summary style, inclusion toggles |
| `Meeting` | `Meetings` | Meeting orchestration — URL, platform, status, `LmaCallId` (cross-ref to LMA DynamoDB) |
| `ActionItem` | `ActionItems` | User-managed action items extracted from summaries |

**Key observation:** The data model is a **bridge layer** designed to sit on top of LMA. It stores orchestration metadata and cross-references LMA via `LmaCallId`. Transcripts and summaries are fetched from LMA's AppSync/DynamoDB, not stored locally. For v2, we need to **own the full pipeline** — transcripts, summaries, and audio references must live in our database.

**Controllers:**

| Controller | Endpoints | Notes |
|------------|-----------|-------|
| `MeetingsController` | `POST /api/meetings/join`, `GET /api/meetings`, `GET /api/meetings/{id}`, `POST /api/meetings/{id}/stop`, `DELETE /api/meetings/{id}`, `PATCH /api/meetings/{id}/status` | Status update endpoint is `[AllowAnonymous]` (called by VP bot) |
| `SummariesController` | `GET /api/meetings/{id}/summary`, `GET /api/meetings/{id}/transcript` | Delegates to `LmaClient` — reads from AppSync (mock data in dev) |
| `ActionItemsController` | `GET /api/action-items`, `GET /api/meetings/{id}/action-items`, `PATCH /api/action-items/{id}` | Direct EF Core queries against local DB |
| `BotConfigController` | `GET /api/bot-config`, `PUT /api/bot-config` | Per-user bot configuration |
| `HealthController` | `/api/health/live` | Health check |

**Services:**
- `MeetingService` — orchestration: creates Meeting record, sends `BotCommand` to SQS, merges local data with LMA data
- `SqsService` — sends JSON bot commands to SQS (LocalStack in dev)
- `LmaClient` — GraphQL client for LMA AppSync. **Currently returns mock data** (`LMA:UseMock=true`). The real AppSync integration is coded but never connected.
- `BotConfigService`, `UserService` — CRUD for their respective entities

**Authentication:**
- **Dev mode (current):** `DevAuthenticationHandler` — accepts all requests, creates stub claims with hardcoded dev user GUID
- **Production mode (configured but unused):** JWT Bearer auth with `Authority` and `Audience` config pointing to Entra ID (Azure AD) — `https://login.microsoftonline.com/{tenant-id}/v2.0`
- No Cognito integration exists in the API

**Dockerfile:** Debian bookworm-slim, multi-stage build, .NET 8 SDK (build) → ASP.NET Core 8.0 runtime. Exposes port 5000.

### 1.3 Web — `src/RefugeMeetingAssistant.Web/`

**Runtime:** .NET 8 Blazor Server (Interactive Server rendering)

**UI Framework:** Bootstrap 5 (vanilla) — **not MudBlazor**. Uses standard Bootstrap cards, tables, badges, modals, and form controls. No component library.

**Pages:**

| Page | Route | Features |
|------|-------|----------|
| `Home.razor` | `/` | Meeting list with stats (total, active, completed, pending actions), active recording cards, "Join Meeting" button/modal, pagination, 30s auto-refresh |
| `MeetingDetails.razor` | `/meeting/{id}` | Tabs: Summary (overview, key decisions, topics, open questions, action items), Transcript (full text with search highlighting), Action Items (toggle complete). 10s auto-refresh while active. |
| `Settings.razor` | `/settings` | Bot name, summary style dropdown, include/exclude toggles for summary sections |

**Authentication:**
- **Dev mode (current):** Cookie auth with auto-login at `/auth/dev-login` (hardcoded dev user claims)
- **Production mode (configured but unused):** `Microsoft.Identity.Web` for Entra ID (Azure AD) OIDC — `AddMicrosoftIdentityWebApp` with `AzureAd` config section
- No Cognito integration exists in the Web app

**API Client:** `MeetingApiClient` — HttpClient wrapper targeting `http://localhost:5000` (configurable via `ApiBaseUrl`).

**Dockerfile:** Debian bookworm-slim, multi-stage, .NET 8. Exposes port 5001.

### 1.4 How It Currently Runs

**Docker Compose (`docker-compose.yml`):**
- `localstack` — mock SQS/S3 on port 4566, with init scripts in `localstack-init/`
- `api` — .NET API on port 5000, dev auth, LocalStack SQS
- `vpbot` — VP bot worker, LocalStack SQS

**Note from docker-compose.yml:** "SQL Server removed to avoid MCR dependency. The API starts gracefully without a database (logs a warning, DB operations will fail but API runs)."

**Current state:** The docker-compose stack provides the VP bot + API + LocalStack, but has no database. The API has a SQL Server connection string configured but no SQL Server instance running. The system is semi-functional: the VP bot can join Teams meetings and record audio, but the full pipeline (transcription, summarization) is not wired up — LMA mock data is returned instead.

### 1.5 LMA Dependency Assessment

The codebase was originally designed as an extension layer on top of AWS LMA (Live Meeting Assistant). Evidence:
- `LmaClient` with AppSync GraphQL queries for transcripts/summaries
- `Meeting.LmaCallId` field for cross-referencing LMA's DynamoDB
- Worker TODO comments about Kinesis audio streaming
- Swagger description: "Extension layer on top of AWS LMA"
- Comments throughout: "LMA owns transcripts, summaries, and call metadata in DynamoDB"

**For v2, LMA is being abandoned entirely.** We own the full pipeline:
1. VP bot captures audio → S3
2. Amazon Transcribe processes audio → transcript stored in our DB
3. Amazon Bedrock (Claude) generates summary → stored in our DB
4. Web app displays everything from our own data

The `LmaClient`, LMA config, and Kinesis references will be replaced with direct AWS SDK calls to Transcribe and Bedrock.

---

## 2. AWS Deployment Plan

Target: Match the FRED and FormIQ deployment pattern on the existing `fortress-tools-cluster` ECS cluster.

### 2.1 ECR Repositories

| Repository | Image | Notes |
|------------|-------|-------|
| `fortress-meetings-web` | Blazor Server + API (combined) | Single container serving both web UI and API endpoints — simplifies deployment, matches FRED pattern |
| `fortress-meetings-vpbot` | VP Bot worker | Separate container — needs Chromium, PulseAudio, Xvfb, FFmpeg. Different scaling profile. |

**Architecture decision: Combine Web + API into one image.** Currently they're separate projects, but the Web app is just a Blazor Server frontend that calls the API via HTTP. For deployment simplicity (matching FRED, which is a single Blazor Server app with everything in-process), we should either:
- **(Option A — Recommended)** Merge the API controllers into the Web project, eliminate the separate API, and have one ECS service. The Web app already has EF Core access patterns; the API is thin.
- **(Option B)** Keep them separate but deploy as a single ECS task with two containers (sidecar pattern). More complex for marginal benefit.

### 2.2 ECS Task Definitions & Services

**Service 1: `meetings-web-dev`**
- Task: `meetings-web-dev-task`
- Container: `fortress-meetings-web` image
- Port: 8080 (match FRED pattern)
- CPU: 512, Memory: 1024 (0.5 vCPU, 1 GB — same as FRED)
- Desired count: 1
- Health check: `GET /health`
- Environment variables: see §2.6

**Service 2: `meetings-vpbot-dev`**
- Task: `meetings-vpbot-dev-task`
- Container: `fortress-meetings-vpbot` image
- No ALB target (worker, not web-facing)
- CPU: 1024, Memory: 2048 (1 vCPU, 2 GB — Chromium is memory-hungry)
- Desired count: 1 (scale based on meeting queue depth later)
- Health check: process-level (container stays alive polling SQS)
- Environment variables: see §2.6

### 2.3 ALB Listener Rule

| Priority | Condition | Target Group |
|----------|-----------|-------------|
| (next available) | Host = `meetings.dev.fortressam.ai` | `meetings-web-dev-tg` (port 8080) |

Same ALB as FRED (`fred.dev.fortressam.ai`) and FormIQ (`formiq.dev.fortressam.ai`).

### 2.4 Route53

| Record | Type | Value |
|--------|------|-------|
| `meetings.dev.fortressam.ai` | CNAME | ALB DNS name (`fortress-tools-alb-*.us-east-1.elb.amazonaws.com`) |

### 2.5 S3 Bucket

| Bucket | Purpose | Encryption |
|--------|---------|------------|
| `fortress-meetings-dev` | Audio recordings, transcripts, summaries for KB push | SSE-KMS |

Key structure:
```
meetings/{meetingId}/audio.wav          — raw audio from VP bot
meetings/{meetingId}/transcript.json    — Transcribe output
meetings/{meetingId}/summary.json       — Bedrock summary output
meetings/{meetingId}/summary.md         — Formatted summary for KB push
```

### 2.6 Environment Variables

**Web/API container:**

| Variable | Value | Notes |
|----------|-------|-------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | |
| `ASPNETCORE_URLS` | `http://+:8080` | Match FRED |
| `FORTRESS_DB_HOST` | Aurora cluster endpoint | Shared with FRED/FormIQ |
| `FORTRESS_DB_PORT` | `3306` | |
| `FORTRESS_DB_USER` | `fortress_mysql` | Shared user |
| `FORTRESS_DB_PASS` | (from Secrets Manager) | |
| `MEETINGS_DB_NAME` | `meetings_dev` | |
| `AWS__Region` | `us-east-1` | |
| `AWS__SQS__BotCommandsQueueUrl` | SQS queue URL | |
| `AWS__S3__BucketName` | `fortress-meetings-dev` | |
| `Auth__CognitoAuthority` | `https://cognito-idp.us-east-1.amazonaws.com/{userPoolId}` | |
| `Auth__CognitoClientId` | Cognito app client ID | |
| `Auth__CognitoClientSecret` | (from Secrets Manager) | |

**VP Bot container:**

| Variable | Value |
|----------|-------|
| `BOT_MODE` | `worker` |
| `AWS_REGION` | `us-east-1` |
| `SQS_QUEUE_URL` | SQS bot commands queue URL |
| `API_BASE_URL` | `http://meetings-web-dev.fortress-tools-cluster:8080` (service discovery) or ALB URL |
| `RECORDINGS_BUCKET_NAME` | `fortress-meetings-dev` |
| `BOT_NAME` | `Fortress Notetaker` |

### 2.7 SQS Queues

| Queue | Purpose |
|-------|---------|
| `fortress-meetings-bot-commands-dev` | API → VP bot: join/stop commands |
| `fortress-meetings-processing-dev` | VP bot → processing: audio ready for transcription |

### 2.8 Cognito

Add callback URLs to the existing Fortress Cognito user pool:
- `https://meetings.dev.fortressam.ai/signin-oidc`
- `https://meetings.dev.fortressam.ai/signout-callback-oidc`

### 2.9 Database

Create `meetings_dev` database on the shared Aurora MySQL cluster:
```sql
CREATE DATABASE meetings_dev CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
GRANT ALL PRIVILEGES ON meetings_dev.* TO 'fortress_mysql'@'%';
FLUSH PRIVILEGES;
```

---

## 3. Database Migration

### 3.1 Current State

- **Provider:** `Microsoft.EntityFrameworkCore.SqlServer` (8.0.x)
- **Connection:** SQL Server on `localhost:1433` (not actually running — docker-compose removed it)
- **Tables:** Users, BotConfigs, Meetings, ActionItems
- **Migrations:** One migration exists: `20260225054738_InitialCreate`
- **Raw SQL:** None in business logic. One `ExecuteSqlRawAsync("SELECT 1 FROM users LIMIT 1")` pattern is used in FRED/FormIQ for table existence checks, but this POC uses `MigrateAsync()` instead.

### 3.2 Migration to Aurora MySQL (Pomelo)

**Package swap:**
```xml
<!-- Remove -->
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.*" />

<!-- Add -->
<PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.*" />
<PackageReference Include="MySqlConnector" Version="2.4.*" />
```

**DbContext registration change (Program.cs):**
```csharp
// Current (SQL Server)
options.UseSqlServer(connectionString);

// New (Aurora MySQL — matching FRED/FormIQ pattern)
var dbHost = builder.Configuration["FORTRESS_DB_HOST"];
var dbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var dbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var dbPass = builder.Configuration["FORTRESS_DB_PASS"] ?? "dev";
var dbName = builder.Configuration["MEETINGS_DB_NAME"] ?? "meetings_dev";
string connectionString;
if (!string.IsNullOrEmpty(dbHost))
{
    var csb = new MySqlConnectionStringBuilder
    {
        Server = dbHost,
        Port = uint.Parse(dbPort),
        Database = dbName,
        UserID = dbUser,
        Password = dbPass,
        ConnectionTimeout = 10
    };
    connectionString = csb.ConnectionString;
}
else
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Server=localhost;Database=meetings_dev;User=root;Password=dev;";
}
var serverVersion = new MySqlServerVersion(new Version(8, 0, 28));
options.UseMySql(connectionString, serverVersion,
    mysqlOptions => mysqlOptions.EnableRetryOnFailure(3));
```

### 3.3 EF Core Compatibility Notes

The current model uses standard EF Core conventions that are MySQL-compatible:
- ✅ `Guid` PKs — Pomelo maps these to `CHAR(36)` by default
- ✅ `string` properties with `[MaxLength]` — maps to `VARCHAR(n)`
- ✅ `DateTime` — maps to `DATETIME(6)`
- ✅ Navigation properties and cascading deletes — standard EF Core
- ✅ Unique indexes on `EntraObjectId` and `Email`
- ⚠️ `string` without `[MaxLength]` (e.g., `ErrorMessage`, `ActionItem.Description`) — maps to `LONGTEXT` in MySQL. Consider adding max lengths.

**No raw SQL is used in business logic**, so there are no MySQL-specific SQL issues.

### 3.4 Migration Steps

1. Swap NuGet packages (SqlServer → Pomelo)
2. Update `Program.cs` DbContext registration (connection string builder pattern from FRED)
3. Delete existing migration (`20260225054738_InitialCreate` and snapshot)
4. Run `dotnet ef migrations add InitialCreate` to generate MySQL-compatible migration
5. Test locally with MySQL container: `docker run -d -p 3306:3306 -e MYSQL_ROOT_PASSWORD=dev -e MYSQL_DATABASE=meetings_dev mysql:8.0`
6. Verify `dotnet ef database update` applies cleanly
7. Match FRED's startup pattern: check table existence → create tables if needed (instead of `MigrateAsync()`)

### 3.5 Data Model Expansion for v2

The current 4-table model is an LMA bridge layer. For v2 (owning the full pipeline), we need:

**New tables:**

| Table | Purpose |
|-------|---------|
| `MeetingTranscripts` | Store transcript segments (speaker, text, timestamps) — replaces LMA DynamoDB |
| `MeetingSummaries` | Store AI summaries (overview, key decisions, action items JSON, etc.) — replaces LMA DynamoDB |
| `MeetingParticipants` | Attendees detected during meeting (display name, speaker label, join time) |

**Modified tables:**

| Table | Changes |
|-------|---------|
| `Meetings` | Add: `AudioS3Key`, `TranscriptS3Key`, `DurationSeconds`, `ParticipantCount`. Remove: `LmaCallId`, `StepFunctionExecutionArn` (LMA artifacts). Rename `CaptureMethod` values if needed. |
| `Users` | Remove `CognitoUserId` (was for LMA Cognito bridge). Add `CognitoSubjectId` (our Cognito user pool). Keep `EntraObjectId` for future Entra migration. |

**Removed references:**
- `LmaCallId` on Meeting
- `StepFunctionExecutionArn` on Meeting
- `CognitoUserId` on User (replace with `CognitoSubjectId`)
- All LMA-related comments and field documentation

---

## 4. Authentication Migration

### 4.1 Current State

**API:**
- Dev: `DevAuthenticationHandler` custom scheme — accepts everything, returns hardcoded claims
- Prod (configured, unused): JWT Bearer with Entra ID authority (`https://login.microsoftonline.com/{tenant-id}/v2.0`)

**Web:**
- Dev: Cookie auth with `/auth/dev-login` endpoint — hardcoded claims
- Prod (configured, unused): `Microsoft.Identity.Web` with `AddMicrosoftIdentityWebApp` for Entra OIDC

Neither has any Cognito integration.

### 4.2 Target: Cognito OIDC

**Design principle:** Use standard ASP.NET Core OIDC middleware. The only Cognito-specific thing is the config values (authority URL, client ID, scopes). Swapping to Entra later means changing config values, not code.

**Web app (Blazor Server) — OIDC flow:**

```csharp
// Replace Microsoft.Identity.Web with standard OIDC
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie()
.AddOpenIdConnect(options =>
{
    options.Authority = builder.Configuration["Auth:Authority"];
    // Cognito: https://cognito-idp.us-east-1.amazonaws.com/{userPoolId}
    // Entra:   https://login.microsoftonline.com/{tenantId}/v2.0
    
    options.ClientId = builder.Configuration["Auth:ClientId"];
    options.ClientSecret = builder.Configuration["Auth:ClientSecret"];
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;
    
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("email");
    options.Scope.Add("profile");
    
    options.CallbackPath = "/signin-oidc";
    options.SignedOutCallbackPath = "/signout-callback-oidc";
    
    options.TokenValidationParameters = new TokenValidationParameters
    {
        NameClaimType = "cognito:username", // or "preferred_username" for Entra
        ValidateIssuer = true,
    };
    
    options.Events = new OpenIdConnectEvents
    {
        OnTokenValidated = async context =>
        {
            // Upsert user in local DB on login
            // Extract sub, email, name from claims
            // Create/update User record
        }
    };
});
```

**API (if kept separate) — JWT Bearer:**

```csharp
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:ClientId"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
        };
    });
```

**Important:** FRED and FormIQ currently use **their own auth** (FRED: bcrypt password hash in DB, FormIQ: no auth). They don't use Cognito OIDC. The Meeting Assistant will be the **first Fortress app using Cognito OIDC**. The pattern established here should eventually be adopted by FRED and FormIQ.

### 4.3 Claims Mapping

| Claim | Cognito | Entra ID | Notes |
|-------|---------|----------|-------|
| Subject | `sub` | `oid` or `sub` | Unique user ID |
| Email | `email` | `email` or `preferred_username` | |
| Name | `cognito:username` or `name` | `name` | |

Map incoming claims to the `User` entity on each login (upsert pattern).

### 4.4 Removing Dev Auth

Keep `DevAuthenticationHandler` for local development but gate it properly:
```csharp
if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Auth:UseDev", false))
{
    // Dev auth...
}
else
{
    // Cognito OIDC...
}
```

---

## 5. Phase 1 Work Packages

### WP-1: Project Restructure & Docker Containerization

**Objective:** Merge Web + API into a single deployable Blazor Server app. Create production Dockerfiles matching FRED/FormIQ patterns.

**Files to modify/create:**
- Merge `RefugeMeetingAssistant.Api/Controllers/` → into Web project (or create shared library)
- Merge `RefugeMeetingAssistant.Api/Services/` → into Web project
- Merge `RefugeMeetingAssistant.Api/Data/` → into Web project
- Update `RefugeMeetingAssistant.Web/Program.cs` — add controllers, EF Core, services
- Remove `MeetingApiClient` HTTP wrapper — call services directly
- Create new `Dockerfile` for combined Web app (match FRED's Debian pattern)
- Update VP bot `Dockerfile` if needed
- Update `docker-compose.yml` for local dev (add MySQL container)

**Acceptance criteria:**
- Single Blazor Server app serves both the UI and API endpoints
- `docker build` produces working images for both web and VP bot
- `docker compose up` starts full local stack (web + vpbot + mysql + localstack)
- All existing pages (Home, MeetingDetails, Settings) work
- API endpoints still accessible at `/api/*`

**Estimated hours:** 8–12  
**Dependencies:** None (first WP)

---

### WP-2: Database Migration to Aurora MySQL

**Objective:** Swap from SQL Server to Pomelo MySQL provider. Expand data model for v2 (own transcripts/summaries). Match FRED/FormIQ connection pattern.

**Files to modify:**
- `.csproj` — swap SqlServer → Pomelo NuGet packages
- `Program.cs` — new connection string builder (FRED pattern)
- `MeetingAssistantDbContext.cs` — add new entities, update config
- `Data/Entities/Meeting.cs` — add AudioS3Key, TranscriptS3Key, DurationSeconds, ParticipantCount; remove LmaCallId, StepFunctionExecutionArn
- `Data/Entities/User.cs` — replace CognitoUserId with CognitoSubjectId
- New: `Data/Entities/MeetingTranscript.cs`
- New: `Data/Entities/MeetingSummary.cs`
- New: `Data/Entities/MeetingParticipant.cs`
- Delete old migration, create new InitialCreate
- `appsettings.json` — MySQL connection string for local dev

**Acceptance criteria:**
- App starts and connects to local MySQL 8.0
- All CRUD operations work (create meeting, list meetings, action items)
- EF Core migration applies cleanly
- No raw SQL anywhere in codebase
- Connection works against Aurora MySQL (test with shared cluster)

**Estimated hours:** 6–8  
**Dependencies:** WP-1 (project restructure)

---

### WP-3: Authentication Swap to Cognito

**Objective:** Replace dev auth and Entra ID config with Cognito OIDC. Users log in via Fortress Cognito user pool.

**Files to modify:**
- `Program.cs` — replace auth setup with standard OIDC middleware
- Remove `DevAuthenticationHandler` from production path (keep for local dev)
- Remove `Microsoft.Identity.Web` dependency
- Add user upsert on login (claims → User entity)
- Update `appsettings.json` with Cognito config structure
- Update all `GetUserId()` methods to read Cognito `sub` claim

**Acceptance criteria:**
- User can log in via Cognito hosted UI
- User record created/updated on first/subsequent logins
- All protected pages require authentication
- All API endpoints validate JWT
- Dev mode still works locally with `Auth:UseDev=true`
- Logout works (redirect to Cognito logout endpoint)

**Estimated hours:** 6–8  
**Dependencies:** WP-1 (project restructure), WP-2 (User entity changes)

---

### WP-4: AWS Deployment (ECR, ECS, ALB, Route53)

**Objective:** Deploy to `meetings.dev.fortressam.ai` on the Fortress ECS cluster.

**Actions:**
- Create ECR repositories: `fortress-meetings-web`, `fortress-meetings-vpbot`
- Build and push Docker images
- Create ECS task definitions with environment variables
- Create ECS services on `fortress-tools-cluster`
- Add ALB listener rule for `meetings.dev.fortressam.ai`
- Create Route53 CNAME
- Create S3 bucket `fortress-meetings-dev`
- Create SQS queues
- Add Cognito callback URLs
- Create `meetings_dev` database on Aurora cluster
- Configure ECS task roles (S3, SQS, Transcribe, Bedrock access)

**Acceptance criteria:**
- `meetings.dev.fortressam.ai` loads the Blazor Server dashboard
- Cognito login works end-to-end
- Database connectivity confirmed (tables created on first boot)
- VP bot starts and connects to SQS
- ALB health checks pass

**Estimated hours:** 8–10  
**Dependencies:** WP-1 (Dockerfiles), WP-2 (database), WP-3 (auth)

---

### WP-5: UI Alignment with Fortress Branding

**Objective:** Replace vanilla Bootstrap with MudBlazor. Apply Fortress navy/gold color scheme and Inter font family.

**Files to modify:**
- `.csproj` — add `MudBlazor` NuGet package
- `_Imports.razor` — add MudBlazor using statements
- `App.razor` / layout — add MudBlazor providers, theme configuration
- `Home.razor` → MudBlazor components (MudDataGrid, MudCard, MudButton, MudDialog)
- `MeetingDetails.razor` → MudBlazor tabs, cards, tables
- `Settings.razor` → MudBlazor form components
- Shared components (MeetingCard, etc.) → MudBlazor
- Add Fortress theme: navy primary (#1B2A4A), gold accent (#D4A843), Inter font
- `wwwroot/css/` — custom CSS overrides for Fortress branding

**Acceptance criteria:**
- All pages render with MudBlazor components
- Fortress navy/gold color scheme applied consistently
- Inter font family loaded and used
- Responsive layout works on mobile
- Visual parity with FRED's look and feel

**Estimated hours:** 10–14  
**Dependencies:** WP-1 (project restructure)

---

### WP-6: Transcription Pipeline

**Objective:** Wire up end-to-end: VP bot uploads audio to S3 → Amazon Transcribe processes it → transcript stored in DB and displayed in UI.

**Files to modify/create:**
- VP bot `worker.ts` — after recording stops, upload WAV to S3 (`meetings/{id}/audio.wav`)
- VP bot — report S3 key back to API via status update
- New: `Services/TranscriptionService.cs` — submit Transcribe job, poll for completion, parse results, store in DB
- New: `Services/S3Service.cs` — upload/download/presigned URL helpers
- New background worker or SQS consumer for processing pipeline
- `MeetingDetails.razor` — display transcript from local DB instead of LMA mock

**Acceptance criteria:**
- VP bot successfully uploads audio to S3 after recording
- Transcribe job starts automatically when audio is uploaded
- Transcript segments with speaker labels are stored in `MeetingTranscripts` table
- Transcript displays in the UI with speaker attribution and timestamps
- End-to-end latency: < 5 minutes for a 30-minute meeting

**Estimated hours:** 12–16  
**Dependencies:** WP-1, WP-2, WP-4 (needs S3 bucket, SQS, IAM roles)

---

### WP-7: Summarization Pipeline

**Objective:** After transcription completes, invoke Bedrock Claude to generate structured summary with action items.

**Files to modify/create:**
- New: `Services/SummarizationService.cs` — send transcript to Bedrock Claude, parse structured response (overview, key decisions, action items, topics, open questions)
- Prompt template for meeting summarization (store as embedded resource or config)
- Store summary in `MeetingSummaries` table
- Auto-extract action items → `ActionItems` table
- Remove `LmaClient` and all LMA mock data
- Update `MeetingDetails.razor` — display summary from local DB

**Acceptance criteria:**
- Summary is generated automatically after transcription completes
- Structured output: overview, key decisions, action items, key topics, open questions
- Action items are automatically extracted and stored
- User can view summary in the dashboard
- Summary quality is acceptable for insurance meeting context
- Bedrock model is configurable (default: Claude 3 Sonnet or Haiku)

**Estimated hours:** 8–12  
**Dependencies:** WP-6 (transcription pipeline must work first)

---

### WP-8: Audio Download Feature

**Objective:** Users can download the original meeting audio recording.

**Files to modify:**
- `Services/S3Service.cs` — generate pre-signed download URL (1-hour expiry)
- New endpoint: `GET /api/meetings/{id}/audio` → returns pre-signed S3 URL
- `MeetingDetails.razor` — add "Download Audio" button (only visible when audio exists)

**Acceptance criteria:**
- Download button visible on completed meetings with audio
- Clicking generates a temporary S3 pre-signed URL
- Audio downloads as WAV file
- URL expires after 1 hour
- Button hidden when no audio exists

**Estimated hours:** 3–4  
**Dependencies:** WP-6 (audio must be in S3)

---

### WP-9: KB Integration (Push to FRED's Bedrock KB)

**Objective:** Users can push curated meeting summaries to Company KB and/or Personal KB for cross-meeting search in FRED.

**Files to modify/create:**
- New: `Services/KnowledgeBaseService.cs` — upload formatted summary to FRED KB S3 data sources
- `MeetingSummaries` table — add `PushedToCompanyKb`, `PushedToPersonalKb` flags
- `MeetingDetails.razor` — add "Push to Company KB" and "Push to My KB" buttons
- Summary formatting: convert structured summary to markdown document suitable for KB ingestion
- S3 upload to FRED's KB S3 data source buckets (with appropriate metadata for personal KB filtering)

**Acceptance criteria:**
- "Push to Company KB" uploads summary to shared KB S3 source
- "Push to My KB" uploads summary to personal KB S3 source with user metadata
- KB re-indexes (verify via FRED search)
- Buttons show pushed status (grey out after push, show timestamp)
- Pushed summaries are searchable in FRED within 15 minutes

**Estimated hours:** 8–10  
**Dependencies:** WP-7 (summaries must exist), WP-4 (deployed to AWS with IAM access to FRED's KB buckets)

---

### WP-10: Speaker Identification Improvements

**Objective:** Phase 1 heuristic matching — use participant list scraping + Transcribe speaker labels for basic speaker identification.

**Files to modify:**
- VP bot `teams.ts` — add participant list scraping (periodically read participant panel)
- VP bot — report participant list to API via callback
- Store participants in `MeetingParticipants` table with join order
- `TranscriptionService.cs` — set `MaxSpeakerLabels` based on participant count
- Heuristic mapping: match Transcribe's `spk_0`, `spk_1` to participants by join order
- `MeetingDetails.razor` — display resolved speaker names in transcript
- Add UI for manual speaker correction (dropdown to reassign speaker labels)

**Acceptance criteria:**
- Participant list captured during Teams meetings
- Transcribe uses correct speaker count hint
- Speaker labels resolved to display names (best-effort)
- User can manually correct speaker assignments in the UI
- Corrections are persisted and applied to transcript display

**Estimated hours:** 10–14  
**Dependencies:** WP-6 (transcription), WP-2 (MeetingParticipants table)

---

### Work Package Dependency Graph

```
WP-1 (Restructure + Docker)
  ├── WP-2 (Database Migration)
  │     ├── WP-3 (Auth → Cognito)
  │     │     └── WP-4 (AWS Deployment)
  │     │           ├── WP-6 (Transcription Pipeline)
  │     │           │     ├── WP-7 (Summarization Pipeline)
  │     │           │     │     └── WP-9 (KB Integration)
  │     │           │     ├── WP-8 (Audio Download)
  │     │           │     └── WP-10 (Speaker ID)
  │     │           └── (deployment gates all runtime features)
  │     └── WP-10 (needs MeetingParticipants table)
  └── WP-5 (UI Branding — can run in parallel after WP-1)
```

**Critical path:** WP-1 → WP-2 → WP-3 → WP-4 → WP-6 → WP-7 → WP-9

**Parallelizable:** WP-5 (branding) can start after WP-1. WP-8 (audio download) is quick after WP-6.

**Total estimated hours:** 79–118 hours (Phase 1 complete)

---

## 6. Future Migration Notes

### 6.1 Database: Aurora MySQL → SQL Server

When Fortress moves to Microsoft stack fully:

**Package swap:**
```xml
<!-- Remove -->
<PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.*" />
<PackageReference Include="MySqlConnector" Version="2.4.*" />

<!-- Add -->
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.*" />
```

**Code change (Program.cs only):**
```csharp
// Replace
var serverVersion = new MySqlServerVersion(new Version(8, 0, 28));
options.UseMySql(connectionString, serverVersion, ...);

// With
options.UseSqlServer(connectionString);
```

**Connection string change:**
```
// MySQL
Server=aurora-host;Port=3306;Database=meetings_dev;User=fortress_mysql;Password=xxx

// SQL Server
Server=sql-host;Database=meetings_dev;User Id=fortress_user;Password=xxx;TrustServerCertificate=true
```

**Migration regeneration:** Delete existing migration, run `dotnet ef migrations add InitialCreate` with new provider.

### 6.2 Authentication: Cognito → Entra ID

**Code change: ZERO.** Same `AddOpenIdConnect` middleware, different config values:

| Config Key | Cognito Value | Entra ID Value |
|------------|---------------|----------------|
| `Auth:Authority` | `https://cognito-idp.us-east-1.amazonaws.com/{poolId}` | `https://login.microsoftonline.com/{tenantId}/v2.0` |
| `Auth:ClientId` | Cognito app client ID | Entra app registration client ID |
| `Auth:ClientSecret` | Cognito app client secret | Entra app client secret |
| `Auth:CallbackPath` | `/signin-oidc` | `/signin-oidc` (same) |

**Claims mapping adjustment:**
```csharp
// Cognito
options.TokenValidationParameters.NameClaimType = "cognito:username";

// Entra
options.TokenValidationParameters.NameClaimType = "preferred_username";
```

This is a config change, not a code change, if we make the claim type configurable:
```csharp
options.TokenValidationParameters.NameClaimType = 
    builder.Configuration["Auth:NameClaimType"] ?? "preferred_username";
```

### 6.3 Things to Avoid (MySQL-Specific Pitfalls)

To ensure the SQL Server migration is trivial, **do not use:**

| Avoid | Why | Alternative |
|-------|-----|-------------|
| `ExecuteSqlRaw` with MySQL syntax | Won't work on SQL Server | Use EF Core LINQ queries |
| MySQL-specific column types (`LONGTEXT`, `ENUM`) | No direct SQL Server equivalent | Use `[MaxLength]` annotations, string columns |
| `FULLTEXT` index creation via raw SQL | Syntax differs | Use EF Core search or skip |
| `MySqlServerVersion` in business logic | Provider-specific | Keep in `Program.cs` only |
| Stored procedures or MySQL functions | Not portable | Business logic in C# |
| `JSON` column type with MySQL JSON functions | Different syntax in SQL Server | Store as `string`, parse in C# |
| `LIMIT` in raw queries | SQL Server uses `TOP` / `OFFSET FETCH` | Use EF Core `.Take()` / `.Skip()` |

**Rule of thumb:** If it's not expressible as EF Core LINQ or Fluent API, don't use it. Every database interaction should go through the `DbContext`.

---

*This document drives the implementation pipeline. Each work package is independently delegatable. Update this spec as implementation reveals new requirements.*

---

## 7. Unified Auth — Cognito Alignment Across All Fortress Apps

### Current State

| App | Current Auth | Status |
|-----|-------------|--------|
| **Portal** | Cognito (ALB-integrated) | ✅ Working |
| **FRED** | Custom bcrypt login (built-in) | ❌ Not Cognito |
| **FormIQ** | None | ❌ No auth |
| **Meeting Assistant** | Dev stub (accepts everything) | ❌ Not Cognito |

### Target State

All apps share **one Cognito User Pool** with group-based RBAC. Single login, SSO across apps.

**Cognito User Pool:** Existing pool (client ID `e3ra6bg1oqji3i1mn2e7g1o1g`)

**Groups (Roles):**

| Group | Description | FRED Access | FormIQ Access | Meeting Assistant Access |
|-------|-------------|-------------|---------------|------------------------|
| `admin` | Full access everywhere | Manage projects, all KB | Manage forms, dictionary, question sets | All meetings, admin settings |
| `manager` | Team-level access | Create projects, company KB push | Review extractions, edit question sets | View team meetings, push to company KB |
| `user` | Standard user | Chat, personal KB | View forms, run extractions | Own meetings, personal KB only |
| `readonly` | View only | View chats | View forms | View assigned meetings |

**JWT Claims:**
- `cognito:groups` → maps to .NET `[Authorize(Roles = "admin")]`
- Custom claims (future): `can_push_company_kb`, `can_delete_meetings`, etc.

### Implementation Pattern (Same for All Apps)

```csharp
// Program.cs — standard OIDC middleware (Cognito today, Entra later = config change only)
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie()
.AddOpenIdConnect(options =>
{
    options.Authority = builder.Configuration["Auth:Authority"];       // Cognito issuer URL
    options.ClientId = builder.Configuration["Auth:ClientId"];         // Shared client ID
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.TokenValidationParameters.RoleClaimType = "cognito:groups";
});
```

```csharp
// Controller/page authorization
[Authorize]                          // Any authenticated user
[Authorize(Roles = "admin")]         // Admin only
[Authorize(Roles = "admin,manager")] // Admin or manager
```

### Work Packages

**WP-AUTH-1: Cognito Group Setup (1 hr)**
- Create groups in Cognito user pool: `admin`, `manager`, `user`, `readonly`
- Add Fred to `admin` group
- Configure app client to include `cognito:groups` in ID token
- Acceptance: Groups exist, Fred's token includes `admin` group claim

**WP-AUTH-2: FRED Cognito Migration (4 hrs)**
- Replace bcrypt login with Cognito OIDC (same pattern as above)
- Remove custom login page, use Cognito hosted UI or redirect
- Map existing users to Cognito (or recreate — only Fred currently)
- Add `[Authorize]` to all pages, `[Authorize(Roles = "admin")]` to admin functions
- Keep existing functionality, just swap the auth layer
- Files: `Program.cs`, `LoginPage.razor` (remove), layout components
- Acceptance: Fred logs in via Cognito, existing features work, admin functions restricted

**WP-AUTH-3: FormIQ Cognito Integration (3 hrs)**
- Add Cognito OIDC auth (currently has none)
- Add `[Authorize]` to all pages
- Admin functions (dictionary management, delete forms) → `admin` or `manager` role
- Files: `Program.cs`, all page components
- Acceptance: Unauthenticated users redirected to login, role-based access works

**WP-AUTH-4: Meeting Assistant Cognito Integration (2 hrs)**
- Replace `DevAuthenticationHandler` with Cognito OIDC
- Add role-based access per spec
- Files: `Program.cs`, remove `DevAuthenticationHandler.cs`
- Acceptance: Auth works, roles enforced

**WP-AUTH-5: Cognito Callback URLs (30 min)**
- Add callback URLs for all apps to Cognito client:
  - `https://fred.dev.fortressam.ai/signin-oidc`
  - `https://formiq.dev.fortressam.ai/signin-oidc`
  - `https://meetings.dev.fortressam.ai/signin-oidc`
  - (Plus PROD equivalents when ready)

### Entra Migration Path (Future)

When moving to Entra ID, the ONLY changes needed:

1. **Config values** (appsettings or env vars):
   - `Auth:Authority` → Entra tenant URL (`https://login.microsoftonline.com/{tenant-id}/v2.0`)
   - `Auth:ClientId` → Entra app registration client ID
   - Add `Auth:ClientSecret` if using confidential client

2. **Role claim mapping**:
   - `TokenValidationParameters.RoleClaimType` → change from `cognito:groups` to `roles` (Entra's claim name)

3. **NuGet packages**:
   - Optional: add `Microsoft.Identity.Web` for richer Entra integration
   - Or keep raw OIDC middleware — works with both

**Zero business logic changes. Zero authorization attribute changes. Zero page changes.**
