# Revised Architecture — LMA Extension Layer

## What Changes From My First Build

### KEEP (already correct)
The .NET API + SQL Server data layer I built is actually the right shape for the **extension layer**:
- ✅ EF Core + SQL Server for **our data** (Users, BotConfigs — things LMA doesn't track)
- ✅ Entra ID auth middleware (we need this — LMA uses Cognito, we use Entra)
- ✅ SQS integration for VP bot orchestration
- ✅ Blazor web UI skeleton (dashboard, settings)
- ✅ Docker Compose for local dev

### CHANGE (integration approach)
1. **SummaryService** — remove standalone Bedrock client. Instead:
   - Read summaries from LMA via AppSync GraphQL
   - Our SQL Server stores a *reference* (LMA call ID), not duplicate data
   
2. **MeetingService** — redefine:
   - Meeting join → triggers LMA VP Step Functions (not just SQS to our bot)
   - Meeting data comes from LMA (AppSync query) + our extensions (SQL Server)
   - Status tracking reads from LMA's DynamoDB (via AppSync subscription)

3. **VP Bot** — rewrite integration:
   - Must send audio to **LMA's Kinesis stream** (not store locally)
   - Must integrate with LMA's Step Functions state machine pattern
   - Platform: Teams (Playwright, from our POC patterns)

4. **Database schema** — simplify:
   - REMOVE: Transcripts, TranscriptSegments, Summaries tables (LMA owns this data)
   - KEEP: Users, BotConfigs tables (our extension data)
   - KEEP: Meetings table BUT change to be a **bridge** table (our meeting ID ↔ LMA call ID)
   - KEEP: ActionItems table (value-add on top of LMA's raw summaries)

### ADD (new)
1. **LmaClient service** — AppSync GraphQL client for querying LMA data
2. **StepFunctionsService** — trigger VP bot via AWS Step Functions
3. **Auth bridging** — Entra ID user → Cognito user mapping

## Revised SQL Server Schema

```sql
-- Our data only. LMA owns transcripts, summaries, etc.

CREATE TABLE Users (
    user_id         UNIQUEIDENTIFIER PRIMARY KEY,
    entra_object_id NVARCHAR(128) NOT NULL UNIQUE,
    cognito_user_id NVARCHAR(128) NULL,  -- Bridge to LMA Cognito
    email           NVARCHAR(256) NOT NULL UNIQUE,
    display_name    NVARCHAR(256) NOT NULL,
    is_active       BIT NOT NULL DEFAULT 1,
    created_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    last_login_at   DATETIME2 NULL
);

CREATE TABLE BotConfigs (
    config_id       UNIQUEIDENTIFIER PRIMARY KEY,
    user_id         UNIQUEIDENTIFIER NOT NULL REFERENCES Users(user_id),
    bot_name        NVARCHAR(100) NOT NULL DEFAULT 'Refuge Notetaker',
    summary_style   NVARCHAR(50) NOT NULL DEFAULT 'standard',
    include_action_items    BIT NOT NULL DEFAULT 1,
    include_key_decisions   BIT NOT NULL DEFAULT 1,
    include_key_topics      BIT NOT NULL DEFAULT 1,
    include_open_questions  BIT NOT NULL DEFAULT 1,
    created_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Bridge table: maps our meeting orchestration to LMA calls
CREATE TABLE Meetings (
    meeting_id      UNIQUEIDENTIFIER PRIMARY KEY,
    user_id         UNIQUEIDENTIFIER NOT NULL REFERENCES Users(user_id),
    lma_call_id     NVARCHAR(256) NULL,  -- LMA's call ID in DynamoDB
    title           NVARCHAR(512) NULL,
    meeting_url     NVARCHAR(2048) NULL,
    platform        NVARCHAR(50) NOT NULL DEFAULT 'teams',
    capture_method  NVARCHAR(50) NOT NULL DEFAULT 'virtual-participant',
    status          NVARCHAR(50) NOT NULL DEFAULT 'pending',
    step_function_execution_arn NVARCHAR(512) NULL,
    error_message   NVARCHAR(MAX) NULL,
    created_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Our value-add: user-managed action items extracted from LMA summaries
CREATE TABLE ActionItems (
    action_item_id  UNIQUEIDENTIFIER PRIMARY KEY,
    meeting_id      UNIQUEIDENTIFIER NOT NULL REFERENCES Meetings(meeting_id),
    user_id         UNIQUEIDENTIFIER NOT NULL REFERENCES Users(user_id),
    description     NVARCHAR(MAX) NOT NULL,
    owner           NVARCHAR(256) NULL,
    due_date        DATE NULL,
    is_completed    BIT NOT NULL DEFAULT 0,
    completed_at    DATETIME2 NULL,
    created_at      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
```

## Revised Service Architecture

```
.NET API (Extension Layer)
├── Controllers/
│   ├── MeetingsController    — Submit Teams URL, list meetings (our DB + LMA data)
│   ├── BotConfigController   — Per-user bot configuration (our DB)
│   ├── ActionItemsController — Action items CRUD (our DB)
│   └── HealthController      — Health checks
├── Services/
│   ├── MeetingService        — Orchestration: create meeting → trigger Step Functions
│   ├── LmaClient             — NEW: AppSync GraphQL client for LMA data
│   ├── StepFunctionsService  — NEW: Trigger Teams VP bot via AWS Step Functions
│   ├── UserService           — Auto-provision, Entra → Cognito bridge
│   └── BotConfigService      — Per-user bot config CRUD
└── Data/
    ├── MeetingAssistantDbContext (4 tables, not 7)
    └── Entities: User, BotConfig, Meeting, ActionItem
```

## Next Steps

1. Refactor the existing codebase:
   - Remove Transcripts, TranscriptSegments, Summaries entities
   - Add `lma_call_id` and `step_function_execution_arn` to Meeting entity
   - Add `cognito_user_id` to User entity
   - Remove standalone SummaryService Bedrock code
   - Add LmaClient (AppSync GraphQL)
   - Add StepFunctionsService
   
2. Update the VP Bot:
   - Change audio output: local file → Kinesis stream
   - Match LMA's VP bot patterns (env vars, metadata format)
   - Adapt for Teams platform (our POC + LMA VP patterns)
