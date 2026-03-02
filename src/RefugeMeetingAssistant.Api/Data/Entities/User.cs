using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RefugeMeetingAssistant.Api.Data.Entities;

[Table("Users")]
public class User
{
    [Key]
    public Guid UserId { get; set; } = Guid.NewGuid();

    [Required, MaxLength(128)]
    public string EntraObjectId { get; set; } = string.Empty;

    /// <summary>Bridge to LMA's Cognito user pool</summary>
    [MaxLength(128)]
    public string? CognitoUserId { get; set; }

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    // Navigation properties
    public BotConfig? BotConfig { get; set; }
    public ICollection<Meeting> Meetings { get; set; } = new List<Meeting>();
    public ICollection<ActionItem> ActionItems { get; set; } = new List<ActionItem>();
}
