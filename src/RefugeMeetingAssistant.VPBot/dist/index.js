/**
 * VP Bot Entry Point
 *
 * Can be run as:
 * 1. SQS Worker (default): Polls SQS for bot commands
 * 2. HTTP Server: Express API for direct bot control (dev mode)
 */
import 'dotenv/config';
const mode = process.env.BOT_MODE || 'worker';
if (mode === 'worker') {
    // Import worker module which starts SQS polling
    await import('./worker.js');
}
else {
    // HTTP mode for development/testing
    const express = await import('express');
    const { MeetingBot } = await import('./bot/meeting-bot.js');
    const { v4: uuidv4 } = await import('uuid');
    const app = express.default();
    app.use(express.json());
    const PORT = process.env.PORT || 3500;
    const RECORDINGS_DIR = process.env.RECORDINGS_DIR || '/app/recordings';
    const activeMeetings = new Map();
    app.get('/api/health', (_req, res) => {
        res.json({
            status: 'healthy',
            mode: 'http',
            activeMeetings: activeMeetings.size,
            timestamp: new Date().toISOString(),
        });
    });
    app.post('/api/meetings/join', async (req, res) => {
        const { url, name } = req.body;
        if (!url) {
            return res.status(400).json({ error: 'Meeting URL is required' });
        }
        const meetingId = uuidv4();
        const platform = MeetingBot.detectPlatform(url);
        const meeting = {
            id: meetingId,
            url,
            platform,
            botName: name || process.env.BOT_NAME || 'Refuge Notetaker',
            status: 'joining',
            createdAt: new Date(),
        };
        const bot = new MeetingBot(meeting, RECORDINGS_DIR);
        activeMeetings.set(meetingId, bot);
        bot.on('error', (error) => {
            console.error(`Bot error for ${meetingId}:`, error.message);
            activeMeetings.delete(meetingId);
        });
        bot.on('meeting-ended', () => {
            activeMeetings.delete(meetingId);
        });
        // Start join asynchronously
        bot.join().catch(err => {
            console.error(`Join failed for ${meetingId}:`, err);
            activeMeetings.delete(meetingId);
        });
        res.status(201).json({ meetingId, status: 'joining', platform });
    });
    app.listen(PORT, () => {
        console.log(`[VPBot HTTP] Listening on port ${PORT}`);
    });
}
//# sourceMappingURL=index.js.map