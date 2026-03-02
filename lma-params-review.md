# LMA CloudFormation Parameters Review

**Template:** `lma/lma-main.yaml`
**Total Parameters:** 71
**Date:** 2026-02-27

> Parameters tagged `[REVIEW]` need Fred's explicit decision before deployment.
> All other parameters use sensible defaults.

---

## 🔐 Authentication & Access (2 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **AdminEmail** | *(none — required)* | `fred.white@refugems.com` | Admin user email — receives initial temp password | `[REVIEW]` ✅ Set |
| **AllowedSignUpEmailDomain** | `""` (empty) | `""` | Email domains allowed for self-signup. Empty = signup disabled, users created via Cognito | `[REVIEW]` — Leave empty for now (manual user creation only), or set to `refugems.com` if others need access |

---

## 🤖 Meeting Assist / AI Service (4 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **MeetingAssistService** | `STRANDS_BEDROCK` | `STRANDS_BEDROCK` | LLM service for meeting assistant. Strands = agentic, lightweight | `[REVIEW]` — Default is the new agentic option. Alternatives: `STRANDS_BEDROCK_WITH_KB (Create)` adds KB doc search |
| **MeetingAssistServiceBedrockModelID** | `global.anthropic.claude-haiku-4-5-20251001-v1:0` | `us.anthropic.claude-sonnet-4-5-20250929-v1:0` | Bedrock model for meeting assist | `[REVIEW]` — Upgraded from Haiku 4.5 to Sonnet 4.5 for better quality. Higher cost. Could use `global.anthropic.claude-sonnet-4-5-20250929-v1:0` for cross-region inference |
| **AssistantWakePhraseRegEx** | `(OK\|Okay)[.,! ]*[Aa]ssistant` | *(default)* | Regex wake phrase for meeting assistant | |
| **Domain** | `Default` | `Default` | Transcription domain (Default or Healthcare) | |

---

## 🔍 Tavily Web Search (1 param)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **TavilyApiKey** | `""` | `""` | API key for Tavily web search in Strands agent | `[REVIEW]` — Optional. Enables web search during meetings. We have a Tavily key already — worth adding? |

---

## 🗣️ Voice Assistant (6 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **VoiceAssistantProvider** | `none` | `none` | Voice AI provider: none / elevenlabs / aws_nova | `[REVIEW]` — Enable `elevenlabs` if you want VP to speak responses. Requires ElevenLabs API key |
| **ElevenLabsApiKey** | `""` | `""` | ElevenLabs TTS API key | `[REVIEW]` — Required if VoiceAssistantProvider = elevenlabs. We have a key |
| **ElevenLabsAgentId** | `""` | `""` | ElevenLabs Conversational AI Agent ID | Optional — custom voice personality |
| **VoiceAssistantActivationMode** | `wake_phrase` | *(default)* | How voice assistant activates: always_active / wake_phrase / strands_tool | |
| **VoiceAssistantWakePhrases** | `hey alex,ok alex,hi alex,hello alex` | *(default)* | Comma-separated wake phrases | |
| **VoiceAssistantActivationDuration** | `30` | *(default)* | Seconds voice stays active after wake phrase (5-300) | |

---

## 🧠 Bedrock Models & Summarization (4 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **EndOfCallTranscriptSummary** | `BEDROCK` | `BEDROCK` | Call summarization engine (BEDROCK or LAMBDA) | |
| **BedrockModelId** | `global.anthropic.claude-haiku-4-5-20251001-v1:0` | `us.anthropic.claude-sonnet-4-5-20250929-v1:0` | Model for end-of-call summaries | `[REVIEW]` — Upgraded to Sonnet 4.5 for better summaries. Cost tradeoff vs Haiku |
| **BedrockGuardrailId** | `""` | `""` | Existing Bedrock Guardrail ID (optional) | |
| **BedrockGuardrailVersion** | `DRAFT` | *(default)* | Guardrail version if ID provided | |

---

## 📚 Knowledge Base (9 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **BedrockKnowledgeBaseId** | `""` | `""` | Existing KB ID (for "Use Existing" modes) | |
| **BedrockKnowledgeBaseS3BucketName** | `""` | `""` | S3 bucket with docs to ingest into KB | |
| **BedrockKnowledgeBaseS3DocumentUploadFolderPrefix** | `""` | `""` | S3 prefix(es) for source documents | |
| **BedrockKnowledgeBaseWebCrawlerURLs** | `https://en.wikipedia.org/wiki/Life_insurance, https://en.wikipedia.org/wiki/Mortgage_loan` | *(default)* | Web URLs to crawl for KB content | `[REVIEW]` — These Wikipedia defaults are examples. Change to relevant URLs or clear if not using KB |
| **BedrockKnowledgeBaseWebCrawlerScope** | `DEFAULT` | *(default)* | Crawl scope: DEFAULT / HOST_ONLY / SUBDOMAINS | |
| **TranscriptKnowledgeBase** | `BEDROCK_KNOWLEDGE_BASE (Create)` | *(default)* | Use meeting transcripts as KB content | `[REVIEW]` — Creates a KB from past transcripts. Good for cross-meeting search. Can disable with `DISABLED` |
| **TranscriptKnowledgeBaseVectorStore** | `S3_VECTORS` | *(default)* | Vector store: S3_VECTORS (cheaper) or OPENSEARCH_SERVERLESS | |
| **MeetingAssistKnowledgeBaseVectorStore** | `S3_VECTORS` | *(default)* | Vector store for meeting assist KB | |
| **MeetingAssistQnABotOpenSearchNodeCount** | `1` | `1` | OpenSearch nodes for QnABot (1/2/4) | `[REVIEW]` — 1 is fine for testing. Production = 4 for fault tolerance. Cost scales with count |

---

## 🤝 Bedrock Agent & Q Business (4 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **BedrockAgentId** | `""` | `""` | Existing Bedrock Agent ID | Only for "BEDROCK_AGENT (Use Existing)" mode |
| **BedrockAgentAliasId** | `TSTALIASID` | *(default)* | Bedrock Agent Alias ID | |
| **AmazonQAppId** | `""` | `""` | Q Business Application ID | Only for Q_BUSINESS mode |
| **IDCApplicationARN** | *(none)* | `""` | Identity Center app ARN for Q Business | Only for Q_BUSINESS mode |

---

## 🔌 MCP Integration (2 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **EnableMCP** | `true` | `true` | Enable MCP server for external app access | `[REVIEW]` — Enables MCP integration (Claude Desktop, QuickSight, etc). Good to have |
| **MCPServerCallbackURLs** | `https://us-east-1.quicksight.aws.amazon.com/sn/oauthcallback,...` | *(default)* | OAuth callback URLs for MCP clients | |

---

## 🎙️ Transcription (11 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **TranscribeLanguageCode** | `en-US` | `en-US` | Transcription language | |
| **TranscribeLanguageOptions** | `en-US, es-US` | *(default)* | Languages for auto-identification mode | Only used with identify-language |
| **TranscribePreferredLanguage** | `None` | *(default)* | Preferred language for identification | |
| **IsPartialTranscriptEnabled** | `true` | *(default)* | Show partial (in-progress) transcripts | |
| **IsContentRedactionEnabled** | `false` | `false` | Enable PII redaction in transcripts | `[REVIEW]` — Consider enabling for compliance. Only works with en-US |
| **TranscribeContentRedactionType** | `PII` | *(default)* | Redaction type (only PII available) | |
| **TranscribePiiEntityTypes** | `BANK_ACCOUNT_NUMBER,...,SSN` | *(default)* | PII entity types to redact | Full list: bank, credit card, CVV, expiry, PIN, email, address, name, phone, SSN |
| **CustomVocabularyName** | `""` | `""` | Custom vocabulary name (must pre-exist) | |
| **CustomLanguageModelName** | `""` | `""` | Custom language model (must pre-exist) | |
| **ModelValidation** | `true` | *(default)* | Validate Bedrock model access on deploy | |
| **TranscriptLambdaHookFunctionNonPartialOnly** | `true` | *(default)* | Hook Lambda for non-partial segments only | |

---

## 🪝 Lambda Hooks (4 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **TranscriptLambdaHookFunctionArn** | `""` | `""` | Lambda ARN for custom transcript processing | |
| **EndOfCallLambdaHookFunctionArn** | `""` | `""` | Lambda ARN for end-of-call processing | |
| **StartOfCallLambdaHookFunctionArn** | `""` | `""` | Lambda ARN for start-of-call processing | |
| **PostCallSummaryLambdaHookFunctionArn** | `""` | `""` | Lambda ARN for post-summary processing | |

---

## 💾 Storage & Recording (5 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **S3BucketName** | *(none)* | `""` | Existing S3 bucket for recordings. Empty = auto-create | |
| **AudioFilePrefix** | `lma-audio-recordings/` | *(default)* | S3 prefix for audio files | |
| **TranscriptFilePrefix** | `lma-transcripts/` | *(default)* | S3 prefix for transcripts | |
| **EnableAudioRecording** | `true` | *(default)* | Record meeting audio | |
| **EnableDataRetentionOnDelete** | `true` | *(default)* | Retain data (DynamoDB, S3, KMS) on stack deletion | `[REVIEW]` — Default true = data survives stack deletion. Good for compliance |

---

## ⏰ Data Retention (4 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **AudioRecordingExpirationInDays** | `90` | *(default)* | Days to keep audio recordings | |
| **MeetingRecordExpirationInDays** | `90` | *(default)* | Days to keep meeting records | |
| **TranscriptionExpirationInDays** | `90` | *(default)* | Days to keep transcriptions | |
| **CloudWatchLogsExpirationInDays** | `90` | *(default)* | Days to keep CloudWatch logs | |

---

## 🌐 CloudFront & Web UI (4 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **CloudFrontPriceClass** | `PriceClass_100` | *(default)* | CloudFront pricing tier (100=cheapest, US/EU/CA only) | |
| **CloudFrontAllowedGeos** | `""` | `""` | Geo-restrict access (e.g. `US,CA`). Empty = no restriction | `[REVIEW]` — Consider setting `US` for security |
| **EnableAppSyncApiCache** | `false` | *(default)* | Enable AppSync API caching | |
| **AppSyncApiCacheInstanceType** | `SMALL` | *(default)* | Cache instance size (only if cache enabled) | |

---

## 🌐 Networking / VPC (6 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **UseExistingVPC** | `false` | `false` | Use existing VPC vs. create new | `[REVIEW]` — false = LMA creates its own VPC. Recommended for initial deploy |
| **VPC** | *(none)* | `""` | VPC ID (only if UseExistingVPC=true) | `[REVIEW]` — Leave empty |
| **PublicSubnet1** | *(none)* | `""` | Public subnet AZ1 | `[REVIEW]` — Leave empty |
| **PublicSubnet2** | *(none)* | `""` | Public subnet AZ2 | `[REVIEW]` — Leave empty |
| **PrivateSubnet1** | *(none)* | `""` | Private subnet AZ1 | `[REVIEW]` — Leave empty |
| **PrivateSubnet2** | *(none)* | `""` | Private subnet AZ2 | `[REVIEW]` — Leave empty |

---

## 🖥️ Virtual Participant Infrastructure (5 params)

| Parameter | Default | Our Value | Description | Notes |
|-----------|---------|-----------|-------------|-------|
| **VPLaunchType** | `EC2` | `EC2` | Container launch type: FARGATE (~$2/mo) or EC2 (~$33/mo, faster startup) | `[REVIEW]` — EC2 gives 85-90% faster startup. FARGATE saves money but slower cold start |
| **VPInstanceType** | `t3.medium` | *(default)* | EC2 instance type for VP containers | |
| **VPMinInstances** | `1` | `1` | Min warm EC2 instances (0 saves cost, 1 = fastest) | `[REVIEW]` — 1 keeps an instance warm (~$33/mo). Set to 0 to save cost (slower startup) |
| **VPMaxInstances** | `10` | *(default)* | Max EC2 instances for scaling | |
| **PermissionsBoundaryArn** | `""` | `""` | IAM permissions boundary ARN (optional) | |

---

## 📋 Summary of `[REVIEW]` Items

| # | Parameter | Recommended Value | Decision Needed |
|---|-----------|-------------------|-----------------|
| 1 | `AdminEmail` | `fred.white@refugems.com` | ✅ Confirm email |
| 2 | `AllowedSignUpEmailDomain` | `""` | Allow self-signup? If so, set domain(s) |
| 3 | `MeetingAssistService` | `STRANDS_BEDROCK` | Confirm service type (with or without KB?) |
| 4 | `MeetingAssistServiceBedrockModelID` | `us.anthropic.claude-sonnet-4-5-20250929-v1:0` | Confirm model (Sonnet 4.5 vs Haiku 4.5 for cost) |
| 5 | `TavilyApiKey` | `""` | Add Tavily key for web search? |
| 6 | `VoiceAssistantProvider` | `none` | Enable ElevenLabs voice? |
| 7 | `ElevenLabsApiKey` | `""` | Add key if enabling voice |
| 8 | `BedrockModelId` | `us.anthropic.claude-sonnet-4-5-20250929-v1:0` | Confirm summary model |
| 9 | `BedrockKnowledgeBaseWebCrawlerURLs` | Wikipedia defaults | Change to relevant URLs or clear |
| 10 | `TranscriptKnowledgeBase` | `BEDROCK_KNOWLEDGE_BASE (Create)` | Enable transcript KB? |
| 11 | `MeetingAssistQnABotOpenSearchNodeCount` | `1` | 1 for testing, 4 for production |
| 12 | `EnableMCP` | `true` | Confirm MCP enabled |
| 13 | `IsContentRedactionEnabled` | `false` | Enable PII redaction? |
| 14 | `CloudFrontAllowedGeos` | `""` | Restrict to US? |
| 15 | `EnableDataRetentionOnDelete` | `true` | Confirm data retained on delete |
| 16 | `UseExistingVPC` | `false` | Confirm new VPC creation |
| 17 | `VPC` / Subnets | `""` | Leave empty (new VPC) |
| 18 | `VPLaunchType` | `EC2` | EC2 ($33/mo) vs FARGATE ($2/mo) |
| 19 | `VPMinInstances` | `1` | 0 (save $) vs 1 (fast startup) |
