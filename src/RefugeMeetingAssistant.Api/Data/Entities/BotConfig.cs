using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RefugeMeetingAssistant.Api.Data.Entities;

[Table("BotConfigs")]
public class BotConfig
{
    [Key]
    public Guid ConfigId { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    [Required, MaxLength(100)]
    public string BotName { get; set; } = "Refuge Notetaker";

    public bool AutoJoin { get; set; } = false; // Phase 2

    [Required, MaxLength(50)]
    public string SummaryStyle { get; set; } = "standard"; // standard, detailed, brief

    public bool IncludeActionItems { get; set; } = true;
    public bool IncludeKeyDecisions { get; set; } = true;
    public bool IncludeKeyTopics { get; set; } = true;
    public bool IncludeOpenQuestions { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
