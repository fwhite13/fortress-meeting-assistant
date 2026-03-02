using System.ComponentModel.DataAnnotations;

namespace RefugeMeetingAssistant.Api.Models;

// ---- Meeting DTOs ----

public record JoinMeetingRequest
{
    [Required]
    public string Url { get; init; } = string.Empty;
    public string? Title { get; init; }
}

public record JoinMeetingResponse
{
    public Guid MeetingId { get; init; }
    public string Status { get; init; } = "joining";
    public string Message { get; init; } = "Bot is joining the meeting";
}

public record MeetingDto
{
    public Guid MeetingId { get; init; }
    public string? LmaCallId { get; init; }
    public string? Title { get; init; }
    public string? MeetingUrl { get; init; }
    public string Platform { get; init; } = string.Empty;
    public string CaptureMethod { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public int ActionItemCount { get; init; }
}

public record MeetingDetailDto : MeetingDto
{
    /// <summary>Transcript data from LMA (via AppSync GraphQL)</summary>
    public LmaTranscriptDto? Transcript { get; init; }
    /// <summary>Summary data from LMA (via AppSync GraphQL)</summary>
    public LmaSummaryDto? Summary { get; init; }
    /// <summary>Action items from our SQL Server (user-managed)</summary>
    public List<ActionItemDto> ActionItems { get; init; } = new();
}

public record MeetingListResponse
{
    public List<MeetingDto> Meetings { get; init; } = new();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public record UpdateMeetingStatusRequest
{
    [Required]
    public string Status { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public string? LmaCallId { get; init; }
    public string? StepFunctionExecutionArn { get; init; }
}

// ---- LMA Data DTOs (read from AppSync) ----

/// <summary>Transcript data as returned from LMA's AppSync API</summary>
public record LmaTranscriptDto
{
    public string? FullText { get; init; }
    public List<LmaTranscriptSegmentDto>? Segments { get; init; }
    public List<string>? Speakers { get; init; }
}

public record LmaTranscriptSegmentDto
{
    public string Speaker { get; init; } = string.Empty;
    public double StartTime { get; init; }
    public double EndTime { get; init; }
    public string Content { get; init; } = string.Empty;
}

/// <summary>Summary data as returned from LMA's AppSync API</summary>
public record LmaSummaryDto
{
    public string? Overview { get; init; }
    public List<string>? KeyDecisions { get; init; }
    public List<string>? ActionItems { get; init; }
    public List<string>? KeyTopics { get; init; }
    public List<string>? OpenQuestions { get; init; }
    public string? ModelId { get; init; }
}

// ---- Action Item DTOs ----

public record ActionItemDto
{
    public Guid ActionItemId { get; init; }
    public Guid MeetingId { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Owner { get; init; }
    public DateTime? DueDate { get; init; }
    public bool IsCompleted { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? MeetingTitle { get; init; }
}

public record UpdateActionItemRequest
{
    public bool? IsCompleted { get; init; }
    public string? Description { get; init; }
    public string? Owner { get; init; }
    public DateTime? DueDate { get; init; }
}

// ---- Bot Config DTOs ----

public record BotConfigDto
{
    public Guid ConfigId { get; init; }
    public string BotName { get; init; } = "Refuge Notetaker";
    public string SummaryStyle { get; init; } = "standard";
    public bool IncludeActionItems { get; init; } = true;
    public bool IncludeKeyDecisions { get; init; } = true;
    public bool IncludeKeyTopics { get; init; } = true;
    public bool IncludeOpenQuestions { get; init; } = true;
}

public record UpdateBotConfigRequest
{
    public string? BotName { get; init; }
    public string? SummaryStyle { get; init; }
    public bool? IncludeActionItems { get; init; }
    public bool? IncludeKeyDecisions { get; init; }
    public bool? IncludeKeyTopics { get; init; }
    public bool? IncludeOpenQuestions { get; init; }
}

// ---- SQS / Bot Command DTOs ----

public record BotCommand
{
    public string Action { get; init; } = "join";
    public string MeetingUrl { get; init; } = string.Empty;
    public string BotName { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public Guid MeetingId { get; init; }
    public string Platform { get; init; } = "teams";
}

// ---- Health Check ----

public record HealthResponse
{
    public string Status { get; init; } = "healthy";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Dictionary<string, string> Services { get; init; } = new();
}
