using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RefugeMeetingAssistant.Api.Data.Entities;

/// <summary>
/// Our value-add: user-managed action items.
/// LMA generates raw action items in its summaries (DynamoDB).
/// We copy and let users manage them (mark complete, assign owners, set due dates).
/// </summary>
[Table("ActionItems")]
public class ActionItem
{
    [Key]
    public Guid ActionItemId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid MeetingId { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    public string Description { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Owner { get; set; }

    public DateTime? DueDate { get; set; }

    public bool IsCompleted { get; set; } = false;
    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(MeetingId))]
    public Meeting? Meeting { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
