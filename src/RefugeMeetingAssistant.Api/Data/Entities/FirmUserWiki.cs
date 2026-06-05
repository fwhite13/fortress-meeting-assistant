using System.ComponentModel.DataAnnotations.Schema;

namespace RefugeMeetingAssistant.Api.Data.Entities;

[Table("firm_user_wiki")]
public class FirmUserWiki
{
    [Column("id")]
    public long Id { get; set; }

    [Column("entra_oid")]
    public string EntraOid { get; set; } = "";

    [Column("entra_tenant_id")]
    public string EntraTenantId { get; set; } = "";

    [Column("wiki_content")]
    public string? WikiContent { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
