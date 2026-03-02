/**
 * VP Bot SQS Worker — LMA-Integrated
 *
 * Polls SQS for bot commands from the .NET API.
 * Launches Playwright bots to join Teams meetings.
 * Sends audio to LMA's Kinesis Data Stream (not local storage).
 * Reports status back to the .NET API.
 *
 * Integration with LMA:
 * - Audio → LMA Kinesis stream → Transcribe → Bedrock → DynamoDB
 * - Status → .NET API → SQL Server (our bridge table)
 * - LMA call ID → .NET API (so we can query LMA for transcript/summary)
 */
import 'dotenv/config';
//# sourceMappingURL=worker.d.ts.map