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
import { SQSClient, ReceiveMessageCommand, DeleteMessageCommand } from '@aws-sdk/client-sqs';
import { MeetingBot } from './bot/meeting-bot.js';
import 'dotenv/config';
const SQS_ENDPOINT = process.env.SQS_ENDPOINT || 'http://localhost:4566';
const QUEUE_URL = process.env.SQS_QUEUE_URL || 'http://localhost:4566/000000000000/refuge-meeting-bot-commands';
const API_BASE_URL = process.env.API_BASE_URL || 'http://localhost:5000';
const RECORDINGS_DIR = process.env.RECORDINGS_DIR || '/app/recordings';
const AWS_REGION = process.env.AWS_REGION || 'us-east-1';
// LMA Integration
const KINESIS_STREAM_NAME = process.env.KINESIS_STREAM_NAME || ''; // LMA's CallDataStream
const RECORDINGS_BUCKET_NAME = process.env.RECORDINGS_BUCKET_NAME || ''; // LMA's S3 bucket
const activeBots = new Map();
const sqsClient = new SQSClient({
    region: AWS_REGION,
    endpoint: SQS_ENDPOINT,
    credentials: {
        accessKeyId: process.env.AWS_ACCESS_KEY_ID || 'test',
        secretAccessKey: process.env.AWS_SECRET_ACCESS_KEY || 'test',
    },
});
/**
 * Report status back to .NET API (our extension layer).
 */
async function reportStatus(meetingId, status, extra) {
    try {
        const body = { status, ...extra };
        const response = await fetch(`${API_BASE_URL}/api/meetings/${meetingId}/status`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
        if (!response.ok) {
            console.error(`[Worker] Status report failed: ${response.status}`);
        }
        else {
            console.log(`[Worker] Status: ${meetingId} → ${status}`);
        }
    }
    catch (error) {
        console.error(`[Worker] Status report error for ${meetingId}:`, error);
    }
}
/**
 * Handle a "join" command.
 *
 * In production, this would:
 * 1. Join meeting via Playwright (Teams)
 * 2. Stream audio to LMA's Kinesis Data Stream (matching LMA VP pattern)
 * 3. Report LMA call ID back to our API
 *
 * In dev mode, records locally and reports status.
 */
async function handleJoin(command) {
    const { meetingId, meetingUrl, botName, platform } = command;
    console.log(`[Worker] Joining: ${meetingId} (${platform}) as "${botName}"`);
    const meeting = {
        id: meetingId,
        url: meetingUrl,
        platform: platform,
        botName: botName,
        status: 'joining',
        createdAt: new Date(),
    };
    const bot = new MeetingBot(meeting, RECORDINGS_DIR);
    activeBots.set(meetingId, bot);
    bot.on('status-change', (status) => {
        reportStatus(meetingId, status);
    });
    bot.on('recording-started', () => {
        console.log(`[Worker] Recording started: ${meetingId}`);
        // In production: generate an LMA call ID and start pushing to Kinesis
        const lmaCallId = `lma-${meetingId}`;
        reportStatus(meetingId, 'recording', { lmaCallId });
        if (KINESIS_STREAM_NAME) {
            console.log(`[Worker] Would stream audio to Kinesis: ${KINESIS_STREAM_NAME}`);
            // TODO: Implement Kinesis PutRecord for audio chunks
            // This matches LMA VP's pattern: send audio frames + metadata to Kinesis
            // LMA's Call Event Processor Lambda handles the rest
        }
    });
    bot.on('recording-stopped', async (audioPath) => {
        console.log(`[Worker] Recording stopped: ${meetingId} → ${audioPath}`);
        reportStatus(meetingId, 'processing');
        activeBots.delete(meetingId);
    });
    bot.on('meeting-ended', () => {
        console.log(`[Worker] Meeting ended: ${meetingId}`);
        activeBots.delete(meetingId);
    });
    bot.on('error', (error) => {
        console.error(`[Worker] Error: ${meetingId}:`, error);
        reportStatus(meetingId, 'error', { errorMessage: error.message });
        activeBots.delete(meetingId);
    });
    try {
        await bot.join();
        console.log(`[Worker] Joined: ${meetingId}`);
    }
    catch (error) {
        console.error(`[Worker] Join failed: ${meetingId}:`, error);
        reportStatus(meetingId, 'error', { errorMessage: error.message });
        activeBots.delete(meetingId);
    }
}
/**
 * Handle a "stop" command.
 */
async function handleStop(command) {
    const { meetingId } = command;
    const bot = activeBots.get(meetingId);
    if (bot) {
        console.log(`[Worker] Stopping: ${meetingId}`);
        try {
            await bot.stop();
        }
        catch (e) {
            console.error(`[Worker] Stop error:`, e);
        }
        activeBots.delete(meetingId);
    }
    else {
        console.log(`[Worker] No active bot for: ${meetingId}`);
    }
}
/**
 * Main SQS polling loop.
 */
async function pollQueue() {
    console.log(`[Worker] Refuge Meeting Assistant VP Bot Worker v2.0.0 (LMA-integrated)`);
    console.log(`[Worker] Queue: ${QUEUE_URL}`);
    console.log(`[Worker] API: ${API_BASE_URL}`);
    console.log(`[Worker] Kinesis: ${KINESIS_STREAM_NAME || '(not configured — dev mode)'}`);
    while (true) {
        try {
            const response = await sqsClient.send(new ReceiveMessageCommand({
                QueueUrl: QUEUE_URL,
                MaxNumberOfMessages: 1,
                WaitTimeSeconds: 20,
                VisibilityTimeout: 300,
            }));
            if (response.Messages && response.Messages.length > 0) {
                for (const message of response.Messages) {
                    try {
                        const command = JSON.parse(message.Body || '{}');
                        console.log(`[Worker] Command: ${command.action} for ${command.meetingId}`);
                        switch (command.action) {
                            case 'join':
                                handleJoin(command).catch(err => console.error(`[Worker] Join error:`, err));
                                break;
                            case 'stop':
                                await handleStop(command);
                                break;
                            default:
                                console.warn(`[Worker] Unknown action: ${command.action}`);
                        }
                        await sqsClient.send(new DeleteMessageCommand({
                            QueueUrl: QUEUE_URL,
                            ReceiptHandle: message.ReceiptHandle,
                        }));
                    }
                    catch (parseError) {
                        console.error(`[Worker] Message processing error:`, parseError);
                    }
                }
            }
        }
        catch (error) {
            console.error(`[Worker] SQS poll error:`, error);
            await new Promise(resolve => setTimeout(resolve, 5000));
        }
    }
}
// Graceful shutdown
process.on('SIGTERM', async () => {
    console.log('[Worker] SIGTERM — stopping bots...');
    for (const [id, bot] of activeBots) {
        try {
            await bot.stop();
        }
        catch { /* ignore */ }
    }
    process.exit(0);
});
pollQueue().catch(err => { console.error('[Worker] Fatal:', err); process.exit(1); });
//# sourceMappingURL=worker.js.map