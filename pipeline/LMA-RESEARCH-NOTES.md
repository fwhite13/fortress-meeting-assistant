# LMA Research Notes — What We Learned

## LMA Architecture (from GitHub + AWS blog)

### What LMA Provides (deployed via CloudFormation)
- **Chrome extension** — captures audio + meeting metadata (speaker names, meeting title) from Zoom, Teams, WebEx, Meet, Chime
- **Kinesis Data Stream** — real-time audio/metadata ingestion
- **Amazon Transcribe** — streaming speech-to-text with speaker diarization
- **Amazon Bedrock** — meeting summaries, action items, live assistant ("OK Assistant")
- **Amazon Translate** — real-time translation (75 languages)
- **DynamoDB** — meeting metadata, transcripts, summaries (via AppSync)
- **AppSync (GraphQL)** — the API layer. Real-time subscriptions for live updates
- **Cognito** — user authentication with UBAC (User-Based Access Control, v0.2.0+)
- **React web UI** — hosted on S3/CloudFront. Meeting list, live transcript, summaries, search
- **S3** — audio recordings storage
- **Lambda** — Call Event Processor, Transcript Summarization, etc.
- **VP Bot (Preview)** — ECS Fargate + Step Functions. Supports Chime and Zoom only. Uses Puppeteer.
- **Bedrock Knowledge Base** — stores all transcripts/summaries for cross-meeting search

### LMA Data Flow
```
Chrome Extension / VP Bot
  → Kinesis Data Stream (audio chunks + metadata)
    → Amazon Transcribe (streaming STT)
      → Lambda (Call Event Processor)
        → AppSync GraphQL mutations → DynamoDB (persist)
        → AppSync subscriptions → Web UI (real-time updates)
      → Lambda (Transcript Summarization) on meeting end
        → Bedrock Claude → save summary → DynamoDB
```

### LMA's AppSync GraphQL API
- **Mutations**: create call, add transcript segment, update call status, add summary
- **Queries**: list calls, get call details, get transcript, get summary
- **Subscriptions**: real-time transcript segments, call status changes
- **Auth**: Cognito user pool (UBAC: each user sees only their meetings)

### LMA VP Bot (existing, Chime/Zoom only)
- Uses **Puppeteer** (not Playwright) + headless Chrome
- Deployed as **ECS Fargate task** + **Step Functions** state machine
- Sends audio directly to **LMA's Kinesis Data Stream**
- Environment vars: `KINESIS_STREAM_NAME`, `RECORDINGS_BUCKET_NAME`, `MEETING_PLATFORM`
- Invoked via Step Functions execution with JSON payload

### LMA UBAC (User-Based Access Control, v0.2.0+)
- Each user sees only their own meetings
- Admin user can see all meetings
- Users can share meetings with others (v0.2.5+)
- Users can delete their own meetings (v0.2.7+)

## What This Means for Our Architecture

### We DON'T need to build:
- ❌ Transcription pipeline (LMA has Kinesis → Transcribe)
- ❌ Bedrock summarization service (LMA has Lambda → Bedrock)
- ❌ Chrome extension (LMA has one)
- ❌ Basic web UI for viewing transcripts (LMA has React UI)
- ❌ Meeting list / search (LMA has it, plus Bedrock KB cross-search)
- ❌ Audio storage (LMA uses S3)
- ❌ User auth for LMA UI (Cognito + UBAC)

### We DO need to build:
1. **Teams VP Bot** — LMA's VP only supports Chime/Zoom. We add Teams.
   - Must send audio to **LMA's Kinesis stream** (not our own pipeline)
   - Must use same env vars: `KINESIS_STREAM_NAME`, etc.
   - Pattern: reuse LMA VP's Puppeteer approach but adapt for Teams (our POC proved Teams join works with Playwright)
   
2. **.NET API as extension layer** — wraps/extends LMA for our specific needs:
   - **Entra ID ↔ Cognito bridge** (our users authenticate via Entra, we map to Cognito or bypass)
   - **Per-user bot config** (SQL Server: bot name, summary preferences)
   - **Meeting orchestration** (API receives "join Teams meeting" request, triggers Teams VP bot via Step Functions)
   - **SQL Server data layer** for anything LMA doesn't track (per-user bot config, our-specific meeting metadata)
   - **Read LMA data** via AppSync GraphQL (transcripts, summaries) and present in our custom UI
   
3. **Custom web UI extensions** — on top of LMA's UI or alongside it:
   - Bot configuration page
   - Teams meeting submit form
   - Dashboard that combines LMA data with our SQL Server data

### Key Integration Points
| Our Component | Integrates With | How |
|---|---|---|
| Teams VP Bot | LMA Kinesis Data Stream | Send audio chunks + metadata |
| Teams VP Bot | LMA Step Functions | Launched via state machine |
| .NET API | LMA AppSync | GraphQL queries for transcripts/summaries |
| .NET API | LMA Cognito | Auth bridging (Entra → Cognito) |
| .NET API | SQL Server | Per-user bot config, meeting orchestration metadata |
| Web UI | .NET API | REST endpoints for bot config, meeting submit |
| Web UI | LMA Web UI | Link to LMA UI for detailed transcript view, or embed |
