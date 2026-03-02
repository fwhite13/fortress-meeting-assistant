/**
 * Core Meeting Bot - Playwright browser automation for meeting capture
 * 
 * Audio capture: Uses ffmpeg to record from PulseAudio virtual sink.
 * The browser routes all WebRTC audio through PulseAudio, and ffmpeg
 * captures from the monitor source. This works for Teams, Zoom, and Meet.
 */

import { chromium, Browser, BrowserContext, Page } from 'playwright';
import { EventEmitter } from 'events';
import { ChildProcess, spawn } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import { Meeting, MeetingPlatform, MeetingStatus } from '../types.js';
import { TeamsHandler } from './teams.js';
import { ZoomHandler } from './zoom.js';
import { GoogleMeetHandler } from './google-meet.js';

export interface MeetingBotEvents {
  'status-change': (status: MeetingStatus) => void;
  'recording-started': () => void;
  'recording-stopped': (audioPath: string) => void;
  'error': (error: Error) => void;
  'meeting-ended': () => void;
}

export class MeetingBot extends EventEmitter {
  private browser: Browser | null = null;
  private context: BrowserContext | null = null;
  private page: Page | null = null;
  private meeting: Meeting;
  private recordingsDir: string;
  private isRecording = false;
  private ffmpegProcess: ChildProcess | null = null;
  private audioPath: string = '';

  constructor(meeting: Meeting, recordingsDir: string) {
    super();
    this.meeting = meeting;
    this.recordingsDir = recordingsDir;
  }

  /**
   * Detect the meeting platform from the URL
   */
  static detectPlatform(url: string): MeetingPlatform {
    if (url.includes('teams.microsoft.com') || url.includes('teams.live.com')) {
      return 'teams';
    }
    if (url.includes('zoom.us')) {
      return 'zoom';
    }
    if (url.includes('meet.google.com')) {
      return 'google-meet';
    }
    return 'unknown';
  }

  /**
   * Launch browser and join the meeting
   */
  async join(): Promise<void> {
    this.emit('status-change', 'joining');

    try {
      // Platform-specific browser launch args
      // Teams needs fake media devices + kiosk mode (per ScreenApp's working implementation)
      const isTeams = this.meeting.platform === 'teams';
      
      const baseArgs = [
        '--no-sandbox',
        '--disable-setuid-sandbox',
        '--disable-web-security',
        '--autoplay-policy=no-user-gesture-required',
        '--enable-features=MediaRecorder',
        '--enable-audio-service-out-of-process',
      ];

      const teamsArgs = [
        '--use-fake-ui-for-media-stream',   // Auto-grant media permissions
        '--use-fake-device-for-media-stream', // Teams needs fake devices for pre-join toggles
        '--kiosk',                            // Prevents address bar in recording
        '--start-maximized',
      ];

      const otherArgs = [
        '--use-fake-ui-for-media-stream',
      ];

      this.browser = await chromium.launch({
        headless: false, // Must be headed — Teams requires it for audio/video
        args: [
          ...baseArgs,
          ...(isTeams ? teamsArgs : otherArgs),
        ],
      });

      // User agent: Linux X11 Chrome 135 — matches ScreenApp's production config
      // which is confirmed working with New Teams (v2) in 2026.
      // Linux UA avoids Windows-specific Teams desktop app detection.
      const userAgent = isTeams
        ? 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36'
        : 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36';

      this.context = await this.browser.newContext({
        permissions: ['microphone', 'camera'],
        userAgent,
        viewport: { width: 1280, height: 720 },
        ignoreHTTPSErrors: true,
      });

      this.page = await this.context.newPage();

      // Navigate to meeting URL
      // For Teams, process the URL (add query hints, keep original URL — no /_#/ rewriting)
      let navUrl = this.meeting.url;
      if (this.meeting.platform === 'teams') {
        // Grant permissions for the Teams origin specifically
        await this.context.grantPermissions(['microphone', 'camera'], { 
          origin: 'https://teams.microsoft.com' 
        });
        navUrl = await TeamsHandler.processTeamsMeetingUrl(this.meeting.url);
        console.log(`[Bot] Teams processed URL: ${navUrl}`);
      }
      // Teams: use networkidle (heavy JS app). Others: domcontentloaded is fine.
      const waitUntil = this.meeting.platform === 'teams' ? 'networkidle' as const : 'domcontentloaded' as const;
      await this.page.goto(navUrl, { waitUntil, timeout: 30000 });

      // Platform-specific join logic
      switch (this.meeting.platform) {
        case 'teams':
          await TeamsHandler.join(this.page, this.meeting.botName, this.meeting.url);
          break;
        case 'zoom':
          await ZoomHandler.join(this.page, this.meeting.botName);
          break;
        case 'google-meet':
          await GoogleMeetHandler.join(this.page, this.meeting.botName);
          break;
        default:
          throw new Error(`Unsupported platform: ${this.meeting.platform}`);
      }

      // Start recording after joining
      await this.startRecording();
      
    } catch (error) {
      this.emit('error', error as Error);
      throw error;
    }
  }

  /**
   * Start capturing audio from the meeting using ffmpeg + PulseAudio.
   * 
   * Instead of trying to capture from DOM audio/video elements (which don't
   * carry WebRTC audio), we record from PulseAudio's monitor source.
   * The browser outputs all meeting audio through PulseAudio, and ffmpeg
   * captures everything from the default monitor source.
   */
  private async startRecording(): Promise<void> {
    if (!this.page) throw new Error('Page not initialized');

    this.isRecording = true;
    this.emit('status-change', 'recording');
    this.emit('recording-started');

    // Output directly as WAV — no conversion needed for transcription
    this.audioPath = path.join(this.recordingsDir, `${this.meeting.id}.wav`);

    // Spawn ffmpeg to record from PulseAudio monitor source
    // -f pulse: PulseAudio input
    // -i virtual_out.monitor: monitor source of our named virtual sink
    // -ac 1: mono (sufficient for speech)
    // -ar 16000: 16kHz sample rate (optimal for speech-to-text)
    this.ffmpegProcess = spawn('ffmpeg', [
      '-f', 'pulse',
      '-i', 'virtual_out.monitor',
      '-ac', '1',
      '-ar', '16000',
      '-y',  // overwrite output
      this.audioPath,
    ], {
      stdio: ['pipe', 'pipe', 'pipe'],
      env: { ...process.env, PULSE_SERVER: 'unix:/var/run/pulse/native' },
    });

    this.ffmpegProcess.on('error', (err) => {
      console.error(`[Bot] FFmpeg failed to start: ${err.message}`);
      this.emit('error', new Error(`FFmpeg failed: ${err.message}`));
    });

    this.ffmpegProcess.stderr?.on('data', (data: Buffer) => {
      const msg = data.toString().trim();
      // Only log non-progress lines (avoid spamming)
      if (msg && !msg.startsWith('size=') && !msg.startsWith('frame=')) {
        console.log(`[FFmpeg] ${msg}`);
      }
    });

    this.ffmpegProcess.on('exit', (code) => {
      console.log(`[Bot] FFmpeg exited with code ${code}`);
    });

    console.log(`[Bot] FFmpeg recording started → ${this.audioPath} (PID: ${this.ffmpegProcess.pid})`);

    // Set up periodic check for meeting end
    this.monitorMeetingStatus();
  }

  /**
   * Monitor if the meeting has ended
   */
  private monitorMeetingStatus(): void {
    const checkInterval = setInterval(async () => {
      if (!this.page || !this.isRecording) {
        clearInterval(checkInterval);
        return;
      }

      try {
        // Check for meeting end indicators
        const meetingEnded = await this.page.evaluate(() => {
          // Teams end indicators
          const teamsEnd = document.querySelector('[data-tid="call-ended"]') ||
                          document.body.innerText.includes('The meeting has ended') ||
                          document.body.innerText.includes('You left the meeting');
          
          // Zoom end indicators
          const zoomEnd = document.body.innerText.includes('This meeting has been ended') ||
                         document.body.innerText.includes('The host has ended this meeting');
          
          // Google Meet end indicators
          const meetEnd = document.body.innerText.includes('You left the meeting') ||
                         document.body.innerText.includes('The call ended');

          return !!(teamsEnd || zoomEnd || meetEnd);
        });

        if (meetingEnded) {
          clearInterval(checkInterval);
          this.emit('meeting-ended');
          await this.stop();
        }
      } catch (error) {
        // Page might be closed
        clearInterval(checkInterval);
      }
    }, 5000);
  }

  /**
   * Stop recording and save audio.
   * Sends SIGINT (graceful quit) to ffmpeg so it writes the WAV header properly.
   */
  async stop(): Promise<string> {
    if (!this.isRecording) {
      throw new Error('Not currently recording');
    }

    this.isRecording = false;
    this.emit('status-change', 'processing');

    // Gracefully stop ffmpeg with SIGINT (writes proper WAV headers)
    if (this.ffmpegProcess && !this.ffmpegProcess.killed) {
      console.log(`[Bot] Stopping FFmpeg (PID: ${this.ffmpegProcess.pid})...`);
      
      await new Promise<void>((resolve) => {
        const timeout = setTimeout(() => {
          // Force kill if it doesn't stop gracefully
          console.log('[Bot] FFmpeg did not stop gracefully, sending SIGKILL');
          this.ffmpegProcess?.kill('SIGKILL');
          resolve();
        }, 5000);

        this.ffmpegProcess!.on('exit', () => {
          clearTimeout(timeout);
          resolve();
        });

        // SIGINT = 'q' quit for ffmpeg, writes headers
        this.ffmpegProcess!.kill('SIGINT');
      });
    }

    this.ffmpegProcess = null;

    // Verify the recording file exists and has data
    if (fs.existsSync(this.audioPath)) {
      const stats = fs.statSync(this.audioPath);
      console.log(`[Bot] Recording saved: ${this.audioPath} (${(stats.size / 1024).toFixed(1)} KB)`);
      
      if (stats.size < 100) {
        console.warn('[Bot] WARNING: Recording file is very small — audio may not have been captured');
      }
    } else {
      console.error(`[Bot] Recording file not found: ${this.audioPath}`);
      // Create empty file so downstream doesn't crash
      fs.writeFileSync(this.audioPath, Buffer.alloc(0));
    }

    this.emit('recording-stopped', this.audioPath);

    // Close browser
    await this.cleanup();

    return this.audioPath;
  }

  /**
   * Force leave the meeting and clean up
   */
  async cleanup(): Promise<void> {
    try {
      if (this.page) {
        await this.page.close().catch(() => {});
      }
      if (this.context) {
        await this.context.close().catch(() => {});
      }
      if (this.browser) {
        await this.browser.close().catch(() => {});
      }
    } catch (error) {
      // Ignore cleanup errors
    }
    
    this.page = null;
    this.context = null;
    this.browser = null;
  }

  /**
   * Get current recording status
   */
  isCurrentlyRecording(): boolean {
    return this.isRecording;
  }
}
