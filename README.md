# Refuge Meeting Assistant — AWS Extension Layer

Multi-user meeting intelligence platform built as an **extension layer on top of [AWS Live Meeting Assistant (LMA)](https://github.com/aws-samples/amazon-transcribe-live-meeting-assistant)**.

## Architecture

**LMA (CloudFormation)** provides the heavy lifting:
- Chrome extension for audio capture
- Kinesis → Amazon Transcribe → real-time transcription
- Amazon Bedrock → meeting summaries + action items
- DynamoDB + AppSync → data storage + GraphQL API
- Cognito → user auth + UBAC
- React web UI → transcript viewer, meeting list

**Our extension layer** adds:
- Teams VP bot (LMA only supports Chime/Zoom)
- .NET API with Entra ID auth (Azure AD)
- SQL Server for per-user bot config and meeting orchestration
- Custom Blazor web portal
- Action item management

```
LMA Stack (CloudFormation)     Our Extensions (.NET + Node.js)
┌─────────────────────┐       ┌────────────────────────────┐
│ Chrome Extension     │       │ .NET API (ASP.NET Core 8)  │
│ Kinesis → Transcribe │←audio─│ Teams VP Bot (Playwright)  │
│ Bedrock → Summaries  │       │ SQL Server (Users, Config) │
│ AppSync ← → DynamoDB │─read─→│ LmaClient (AppSync)        │
│ Cognito (auth)       │       │ Entra ID auth              │
│ React Web UI         │       │ Blazor Web Portal          │
└─────────────────────┘       └────────────────────────────┘
```

## Environment Variables

Create a `.env` file in the project root (copy from `.env.example`):

```bash
cp .env.example .env
```

Required variables:
- `DB_PASSWORD` — SQL Server SA password
- `AWS_ACCESS_KEY_ID` — AWS credentials (use "test" for LocalStack)
- `AWS_SECRET_ACCESS_KEY` — AWS credentials (use "test" for LocalStack)
- `AWS_REGION` — AWS region (default: us-east-1)

> **Note:** In development mode, if `DB_PASSWORD` is not set, the API falls back to the default dev password. In production, the API will refuse to start without `DB_PASSWORD`.

## Quick Start

### Prerequisites
- .NET 8 SDK
- Node.js 20+
- Docker & Docker Compose
- LMA deployed via CloudFormation (for production; mock mode for dev)

### 1. Start Infrastructure
```bash
docker compose up -d sqlserver localstack
```

### 2. Apply Migrations
```bash
cd src/RefugeMeetingAssistant.Api
dotnet ef database update
```

### 3. Start API
```bash
cd src/RefugeMeetingAssistant.Api
dotnet run
# → http://localhost:5000 (Swagger: http://localhost:5000/swagger)
```

### 4. Start Web Portal
```bash
cd src/RefugeMeetingAssistant.Web
dotnet run --urls "http://localhost:5001"
# → http://localhost:5001
```

### 5. Connect to LMA (Production)
After deploying LMA CloudFormation, update `appsettings.json`:
```json
{
  "LMA": {
    "UseMock": false,
    "AppSyncUrl": "https://xxx.appsync-api.us-east-1.amazonaws.com/graphql",
    "CognitoUserPoolId": "us-east-1_xxx",
    "KinesisStreamName": "LMA-CallDataStream-xxx"
  }
}
```
Values are in the LMA CloudFormation stack outputs.

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/meetings/join` | Submit Teams URL → dispatch VP bot |
| GET | `/api/meetings` | List meetings (paginated) |
| GET | `/api/meetings/{id}` | Details + LMA transcript/summary |
| POST | `/api/meetings/{id}/stop` | Stop recording |
| DELETE | `/api/meetings/{id}` | Delete meeting |
| GET | `/api/meetings/{id}/summary` | Summary from LMA |
| GET | `/api/meetings/{id}/transcript` | Transcript from LMA |
| GET | `/api/action-items` | List action items |
| PATCH | `/api/action-items/{id}` | Update action item |
| GET/PUT | `/api/bot-config` | Bot configuration |
| GET | `/api/health` | Health check |

## Database (SQL Server — Our Data Only)

| Table | Purpose |
|-------|---------|
| Users | Entra ID ↔ Cognito mapping |
| BotConfigs | Per-user bot name + preferences |
| Meetings | Bridge: our meeting ID ↔ LMA call ID |
| ActionItems | User-managed action items |

LMA owns: transcripts, summaries, call metadata (in DynamoDB via AppSync).

## Project Structure

```
meeting-assistant-aws/
├── src/
│   ├── RefugeMeetingAssistant.Api/     (.NET 8 API — extension layer)
│   │   ├── Controllers/                (5 controllers)
│   │   ├── Services/                   (5 services incl. LmaClient)
│   │   ├── Data/                       (EF Core, 4 entities)
│   │   └── Middleware/                 (Dev auth)
│   ├── RefugeMeetingAssistant.VPBot/   (Node.js Teams VP bot)
│   │   └── src/worker.ts              (SQS consumer → Kinesis)
│   └── RefugeMeetingAssistant.Web/     (Blazor Server portal)
├── docker-compose.yml
├── pipeline/
│   ├── AWS-BUILD-REPORT.md
│   ├── LMA-RESEARCH-NOTES.md
│   └── REVISED-ARCHITECTURE.md
└── README.md
```
