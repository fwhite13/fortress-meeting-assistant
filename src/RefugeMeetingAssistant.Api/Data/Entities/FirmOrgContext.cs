using System.ComponentModel.DataAnnotations.Schema;

namespace RefugeMeetingAssistant.Api.Data.Entities;

[Table("firm_org_context")]
public class FirmOrgContext
{
    [Column("id")]
    public long Id { get; set; }

    [Column("entra_tenant_id")]
    public string EntraTenantId { get; set; } = "";

    [Column("wiki_content")]
    public string? WikiContent { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("updated_by")]
    public string? UpdatedBy { get; set; }
}
