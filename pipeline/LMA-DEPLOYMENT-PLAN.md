# LMA Deployment Plan — Get Meeting Assistant AWS to Testable State TODAY

**Priority:** P0 — Fred wants to test joining/transcribing/summarizing TODAY  
**Created:** 2026-02-25 09:46 AM EST  
**Target:** End of day (before Fred's next meeting)

---

## Mission

Transform the Meeting Assistant AWS build from "API serves Swagger" to "actually joins meetings, transcribes, and summarizes."

---

## 3-Part Execution Plan

### Part 1: Deploy LMA CloudFormation Stack ⚡ PRIORITY 1

**What it gives us:**
- Kinesis Data Stream (real-time audio streaming)
- Amazon Transcribe (speech-to-text + speaker diarization)
- Amazon Bedrock (Claude 3.x for summaries)
- S3 (audio storage)
- Cognito (auth for LMA web UI)
- AppSync + DynamoDB (GraphQL API for transcripts/summaries)
- Chrome extension + React web UI

**Why first:** This is the REAL pipeline. Everything else wires into it.

**Steps:**
1. Clone LMA repo: `https://github.com/aws-samples/amazon-transcribe-live-meeting-assistant`
2. Review CloudFormation template (`lma-main.yaml` or similar)
3. Deploy to Fred's AWS account (us-east-1)
4. Capture stack outputs (AppSync URL, Cognito Pool ID, Kinesis stream name)
5. Update `meeting-assistant-aws/.env` and `appsettings.json` with LMA values

**Time estimate:** 30-60 minutes  
**Blocker risk:** MEDIUM (CloudFormation can take 20-30 min to deploy, may encounter permission issues)

---

### Part 2: Wire VP Bot into LMA Pipeline

**Current state:** POC bot on port 3500 works end-to-end (standalone flow)  
**Target state:** Bot sends audio to LMA Kinesis → Transcribe → Bedrock

**Steps:**
1. Port POC's working join/record code from `meeting-assistant:v8-audio-fix` into `RefugeMeetingAssistant.VPBot`
2. Replace POC's direct S3→Transcribe flow with Kinesis `PutRecord` calls
3. Test: bot joins Teams → audio streams to Kinesis → transcript appears in LMA DynamoDB
4. Wire .NET API to read transcripts from LMA AppSync (LmaClient service)

**Key files to port:**
- POC: `/home/fredw/.openclaw/workspace/meeting-assistant/src/bot/teams.ts` (join logic)
- POC: `/home/fredw/.openclaw/workspace/meeting-assistant/src/bot/meeting-bot.ts` (audio capture)
- Target: `meeting-assistant-aws/src/RefugeMeetingAssistant.VPBot/src/worker.ts`

**Time estimate:** 1-2 hours  
**Blocker risk:** LOW (POC code proven, just needs integration points changed)

---

### Part 3: Get SQL Server Working (or Swap to PostgreSQL)

**Current blocker:** MCR pulls fail despite DNS fix (`"dns": ["8.8.8.8", "100.100.100.100"]`)  
**Likely cause:** Docker Desktop 4.60 bug with WSL2 + MCR CDN

**Two options:**

#### Option A: Fix MCR (UNCERTAIN timeline)
- Downgrade Docker Desktop to 4.59?
- Try different MCR mirror/region?
- Contact Docker support?

**Risk:** Might not fix today, Fred needs this working TODAY

#### Option B: Swap to PostgreSQL (RECOMMENDED — 1 hour)
- Reuse Inner Sanctum patterns (proven, working)
- Create Postgres container in `docker-compose.yml`
- Port 4 EF Core entities to Postgres provider
- Update API connection string
- Run migrations

**Why PostgreSQL:**
- ✅ Works reliably on WSL2 (proven in Inner Sanctum)
- ✅ Fred's team knows Postgres (Inner Sanctum uses it)
- ✅ Fast to implement (1 hour vs uncertain MCR troubleshooting)
- ✅ Production can still use RDS SQL Server (just local dev change)

**Time estimate:** 1 hour  
**Blocker risk:** LOW (proven pattern, clear path)

---

## Execution Order

### Immediate (Next 2 Hours)
1. **Part 1:** Deploy LMA CloudFormation (START NOW — longest lead time)
2. **Part 3 (Option B):** While CloudFormation deploys, swap to Postgres (parallel work)
3. **Part 2:** Wire VP bot to LMA pipeline

### Critical Path
```
[09:46] Start LMA CloudFormation deploy
[09:46] START Postgres swap (parallel)
[10:30] Postgres complete → API can save meetings to DB
[10:30] LMA stack complete → Kinesis/Transcribe/Bedrock ready
[10:45] START VP bot integration
[12:00] Bot wired to LMA → TESTABLE STATE ACHIEVED
```

---

## Success Criteria

**Minimum testable state (by end of day):**
- [ ] LMA CloudFormation stack deployed and healthy
- [ ] API can write meetings to database (Postgres)
- [ ] VP bot joins Teams meeting (port POC code)
- [ ] Bot sends audio to LMA Kinesis stream
- [ ] Transcript appears in LMA DynamoDB
- [ ] API can read transcript from LMA AppSync
- [ ] End-to-end: Fred pastes Teams URL → bot joins → transcript generated

**Not required for "testable today":**
- ❌ Blazor web UI (can test via API/Swagger)
- ❌ Chrome extension setup (LMA native, Fred can test later)
- ❌ Calendar auto-join (Phase 2)
- ❌ Production ECS deployment (local Docker OK for today)

---

## Rollback Plan

**If Part 1 (LMA CloudFormation) blocked/fails:**
- Continue with Part 2+3 → get bot working with POC's standalone flow
- Fred can test joining/transcribing with POC architecture
- Deploy LMA stack tomorrow when unblocked

**If Part 3 (Postgres) fails:**
- Keep API in mock mode (no DB writes)
- Bot still works, just can't persist meeting metadata
- Revisit SQL Server MCR issue tomorrow

---

## Resources

- **LMA repo:** https://github.com/aws-samples/amazon-transcribe-live-meeting-assistant
- **Spec:** `/home/fredw/.openclaw/workspace/memory/projects/meeting-assistant-aws-spec.md`
- **Source:** `/home/fredw/.openclaw/workspace/meeting-assistant-aws/`
- **POC reference:** Container `meeting-assistant` on port 3500, image `meeting-assistant:v8-audio-fix`
- **Pipeline reports:** `/home/fredw/.openclaw/workspace/meeting-assistant-aws/pipeline/`

---

## Agent Assignment

**Part 1 (LMA CloudFormation):** DevOps (Rhodey) — infrastructure deployment  
**Part 2 (VP Bot Integration):** Software Engineer (Tony) — code porting + wiring  
**Part 3 (Postgres Swap):** Software Engineer (Tony) — database refactor

**Orchestrator:** Maria Hill (pipeline-manager) — task sequencing, gate decisions, status updates to Fred

---

*The mission is clear: get this thing DOING something by EOD. Fred's tired of seeing Swagger pages.*
