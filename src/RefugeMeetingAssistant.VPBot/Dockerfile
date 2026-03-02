# Refuge Meeting Assistant — VP Bot
# Multi-stage build: Node.js + Playwright + Chromium + PulseAudio

# Stage 1: Build
FROM node:20-slim AS builder

WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY tsconfig.json ./
COPY src ./src
RUN npm run build

# Stage 2: Production runtime
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive
WORKDIR /app

# Install Node.js 20 + system deps for Playwright + audio
RUN apt-get update && apt-get install -y \
    curl ca-certificates gnupg \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_20.x nodistro main" > /etc/apt/sources.list.d/nodesource.list \
    && apt-get update && apt-get install -y \
    nodejs pulseaudio xvfb ffmpeg dbus \
    libnss3 libatk1.0-0t64 libatk-bridge2.0-0t64 libcups2t64 libdrm2 \
    libxkbcommon0 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 \
    libgbm1 libpango-1.0-0 libcairo2 libasound2t64 libatspi2.0-0t64 \
    fonts-liberation libgl1 \
    && rm -rf /var/lib/apt/lists/*

ENV DISPLAY=:99
ENV PULSE_SERVER=unix:/var/run/pulse/native

# Copy built files
COPY --from=builder /app/node_modules ./node_modules
COPY --from=builder /app/dist ./dist
COPY --from=builder /app/package*.json ./

# Install Playwright Chromium
RUN npx playwright install chromium

# Create directories
RUN mkdir -p /app/recordings && chmod 777 /app/recordings

# Create PulseAudio config and start script
RUN mkdir -p /etc/pulse /var/run/pulse && \
    chown pulse:pulse /var/run/pulse && \
    usermod -aG pulse-access root && \
    echo '#!/bin/bash\n\
set -e\n\
Xvfb :99 -screen 0 1920x1080x24 &\n\
sleep 1\n\
mkdir -p /run/dbus\n\
dbus-daemon --system --fork 2>/dev/null || true\n\
mkdir -p /var/run/pulse\n\
chown pulse:pulse /var/run/pulse\n\
pulseaudio --system --disallow-exit --disallow-module-loading=0 \\\n\
  --exit-idle-time=-1 --daemonize=yes\n\
sleep 2\n\
pactl load-module module-null-sink sink_name=virtual_out sink_properties=device.description=VirtualOutput\n\
pactl set-default-sink virtual_out\n\
echo "[start.sh] PulseAudio ready"\n\
exec node dist/index.js\n\
' > /app/start.sh && chmod +x /app/start.sh

# Environment
ENV PORT=3500
ENV AWS_REGION=us-east-1
ENV BOT_NAME="Refuge Notetaker"
ENV BOT_MODE=worker
ENV RECORDINGS_DIR=/app/recordings

EXPOSE 3500

HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:3500/api/health || exit 1

CMD ["/app/start.sh"]
