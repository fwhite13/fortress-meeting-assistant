using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RefugeMeetingAssistant.Api.Data.Entities;

[Table("OrgWikiEntries")]
public class OrgWikiEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Term { get; set; } = "";

    [Column(TypeName = "text")]
    public string Description { get; set; } = "";

    [MaxLength(50)]
    public string Source { get; set; } = "organization";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
