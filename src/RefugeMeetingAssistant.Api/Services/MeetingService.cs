using Microsoft.EntityFrameworkCore;
using RefugeMeetingAssistant.Api.Data;
using RefugeMeetingAssistant.Api.Data.Entities;
using RefugeMeetingAssistant.Api.Models;

namespace RefugeMeetingAssistant.Api.Services;

/// <summary>
/// Core meeting orchestration service.
/// 
/// Manages the meeting lifecycle:
/// 1. User submits Teams URL → create Meeting record
/// 2. Trigger VP bot via Step Functions (or SQS for dev)
/// 3. VP bot joins, streams audio to LMA's Kinesis
/// 4. LMA processes transcript + summary
/// 5. We read results from LMA via AppSync and merge with our data
/// </summary>
public class MeetingService
{
    private readonly MeetingAssistantDbContext _db;
    private readonly SqsService _sqsService;
    private readonly LmaClient _lmaClient;
    private readonly BotConfigService _botConfigService;
    private readonly ILogger<MeetingService> _logger;

    public MeetingService(
        MeetingAssistantDbContext db,
        SqsService sqsService,
        LmaClient lmaClient,
        BotConfigService botConfigService,
        ILogger<MeetingService> logger)
    {
        _db = db;
        _sqsService = sqsService;
        _lmaClient = lmaClient;
        _botConfigService = botConfigService;
        _logger = logger;
    }

    /// <summary>
    /// Join a meeting: create record, dispatch VP bot.
    /// </summary>
    public async Task<JoinMeetingResponse> JoinMeetingAsync(Guid userId, JoinMeetingRequest request)
    {
        var platform = DetectPlatform(request.Url);
        var botConfig = await _botConfigService.GetConfigAsync(userId);
        var botName = botConfig?.BotName ?? "Refuge Notetaker";

        var meeting = new Meeting
        {
            UserId = userId,
            Title = request.Title ?? $"Meeting - {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            MeetingUrl = request.Url,
            Platform = platform,
            CaptureMethod = "virtual-participant",
            Status = MeetingStatus.Joining
        };

        _db.Meetings.Add(meeting);
        await _db.SaveChangesAsync();

        // Dispatch VP bot
        // Production: trigger Step Functions state machine (like LMA's VP pattern)
        // Dev: send SQS message to our worker
        var command = new BotCommand
        {
            Action = "join",
            MeetingUrl = request.Url,
            BotName = botName,
            UserId = userId,
            MeetingId = meeting.MeetingId,
            Platform = platform
        };

        var sent = await _sqsService.SendBotCommandAsync(command);

        _logger.LogInformation("Meeting join initiated: {MeetingId} on {Platform}", meeting.MeetingId, platform);

        return new JoinMeetingResponse
        {
            MeetingId = meeting.MeetingId,
            Status = meeting.Status,
            Message = sent
                ? "Bot is joining the meeting"
                : "Meeting created (bot dispatch pending — configure SQS/Step Functions)"
        };
    }

    /// <summary>
    /// List user's meetings with pagination.
    /// </summary>
    public async Task<MeetingListResponse> GetMeetingsAsync(
        Guid userId, int page = 1, int pageSize = 20, string? status = null, string? search = null)
    {
        var query = _db.Meetings.Where(m => m.UserId == userId).AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(m => m.Status == status);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(m => m.Title != null && m.Title.Contains(search));

        var totalCount = await query.CountAsync();

        var meetings = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MeetingDto
            {
                MeetingId = m.MeetingId,
                LmaCallId = m.LmaCallId,
                Title = m.Title,
                MeetingUrl = m.MeetingUrl,
                Platform = m.Platform,
                CaptureMethod = m.CaptureMethod,
                Status = m.Status,
                ErrorMessage = m.ErrorMessage,
                StartedAt = m.StartedAt,
                EndedAt = m.EndedAt,
                CreatedAt = m.CreatedAt,
                ActionItemCount = m.ActionItems.Count
            })
            .ToListAsync();

        return new MeetingListResponse
        {
            Meetings = meetings,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Get meeting details: our metadata + LMA transcript/summary.
    /// </summary>
    public async Task<MeetingDetailDto?> GetMeetingDetailAsync(Guid userId, Guid meetingId)
    {
        var meeting = await _db.Meetings
            .Include(m => m.ActionItems)
            .FirstOrDefaultAsync(m => m.MeetingId == meetingId && m.UserId == userId);

        if (meeting == null) return null;

        // Fetch transcript and summary from LMA if we have a call ID
        LmaTranscriptDto? transcript = null;
        LmaSummaryDto? summary = null;

        if (!string.IsNullOrEmpty(meeting.LmaCallId))
        {
            transcript = await _lmaClient.GetTranscriptAsync(meeting.LmaCallId);
            summary = await _lmaClient.GetSummaryAsync(meeting.LmaCallId);
        }

        return new MeetingDetailDto
        {
            MeetingId = meeting.MeetingId,
            LmaCallId = meeting.LmaCallId,
            Title = meeting.Title,
            MeetingUrl = meeting.MeetingUrl,
            Platform = meeting.Platform,
            CaptureMethod = meeting.CaptureMethod,
            Status = meeting.Status,
            ErrorMessage = meeting.ErrorMessage,
            StartedAt = meeting.StartedAt,
            EndedAt = meeting.EndedAt,
            CreatedAt = meeting.CreatedAt,
            ActionItemCount = meeting.ActionItems.Count,
            Transcript = transcript,
            Summary = summary,
            ActionItems = meeting.ActionItems.Select(a => new ActionItemDto
            {
                ActionItemId = a.ActionItemId,
                MeetingId = a.MeetingId,
                Description = a.Description,
                Owner = a.Owner,
                DueDate = a.DueDate,
                IsCompleted = a.IsCompleted,
                CompletedAt = a.CompletedAt,
                CreatedAt = a.CreatedAt
            }).ToList()
        };
    }

    /// <summary>
    /// Update meeting status (called by VP bot worker or Step Functions callback).
    /// </summary>
    public async Task<Meeting?> UpdateStatusAsync(Guid meetingId, UpdateMeetingStatusRequest request)
    {
        var meeting = await _db.Meetings.FindAsync(meetingId);
        if (meeting == null) return null;

        meeting.Status = request.Status;
        meeting.UpdatedAt = DateTime.UtcNow;

        if (request.ErrorMessage != null) meeting.ErrorMessage = request.ErrorMessage;
        if (request.LmaCallId != null) meeting.LmaCallId = request.LmaCallId;
        if (request.StepFunctionExecutionArn != null) meeting.StepFunctionExecutionArn = request.StepFunctionExecutionArn;

        if (request.Status == MeetingStatus.Recording && meeting.StartedAt == null)
            meeting.StartedAt = DateTime.UtcNow;
        if (request.Status is MeetingStatus.Processing or MeetingStatus.Completed or MeetingStatus.Error)
            meeting.EndedAt ??= DateTime.UtcNow;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Meeting {MeetingId} status → {Status}", meetingId, request.Status);
        return meeting;
    }

    /// <summary>
    /// Stop a meeting.
    /// </summary>
    public async Task<Meeting?> StopMeetingAsync(Guid userId, Guid meetingId)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.MeetingId == meetingId && m.UserId == userId);
        if (meeting == null) return null;

        var command = new BotCommand
        {
            Action = "stop",
            MeetingUrl = meeting.MeetingUrl ?? "",
            UserId = userId,
            MeetingId = meetingId,
            Platform = meeting.Platform
        };
        await _sqsService.SendBotCommandAsync(command);

        meeting.Status = MeetingStatus.Processing;
        meeting.EndedAt = DateTime.UtcNow;
        meeting.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return meeting;
    }

    /// <summary>
    /// Delete a meeting.
    /// </summary>
    public async Task<bool> DeleteMeetingAsync(Guid userId, Guid meetingId)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.MeetingId == meetingId && m.UserId == userId);
        if (meeting == null) return false;

        _db.Meetings.Remove(meeting);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Deleted meeting {MeetingId}", meetingId);
        return true;
    }

    private static string DetectPlatform(string url)
    {
        if (url.Contains("teams.microsoft.com") || url.Contains("teams.live.com")) return "teams";
        if (url.Contains("zoom.us")) return "zoom";
        if (url.Contains("meet.google.com")) return "google-meet";
        if (url.Contains("webex.com")) return "webex";
        if (url.Contains("chime.aws")) return "chime";
        return "unknown";
    }
}
