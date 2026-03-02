using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RefugeMeetingAssistant.Api.Data.Entities;

/// <summary>
/// Bridge table: maps our meeting orchestration to LMA calls.
/// We own the orchestration metadata (who requested it, which URL, bot config).
/// LMA owns the transcript, summary, and audio data (in DynamoDB/S3).
/// We cross-reference via lma_call_id.
/// </summary>
[Table("Meetings")]
public class Meeting
{
    [Key]
    public Guid MeetingId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    /// <summary>LMA's call ID in DynamoDB — used to query transcripts/summaries via AppSync</summary>
    [MaxLength(256)]
    public string? LmaCallId { get; set; }

    [MaxLength(512)]
    public string? Title { get; set; }

    [MaxLength(2048)]
    public string? MeetingUrl { get; set; }

    [Required, MaxLength(50)]
    public string Platform { get; set; } = "teams";

    [Required, MaxLength(50)]
    public string CaptureMethod { get; set; } = "virtual-participant";

    [Required, MaxLength(50)]
    public string Status { get; set; } = MeetingStatus.Pending;

    /// <summary>AWS Step Functions execution ARN for VP bot</summary>
    [MaxLength(512)]
    public string? StepFunctionExecutionArn { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
    public ICollection<ActionItem> ActionItems { get; set; } = new List<ActionItem>();
}

/// <summary>
/// Meeting status constants.
/// Our status tracks the orchestration state. LMA tracks its own call states internally.
/// </summary>
public static class MeetingStatus
{
    public const string Pending = "pending";
    public const string Joining = "joining";
    public const string Recording = "recording";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Error = "error";

    public static readonly string[] AllStatuses =
    {
        Pending, Joining, Recording, Processing, Completed, Error
    };
}
