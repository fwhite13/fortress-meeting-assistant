using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RefugeMeetingAssistant.Web.Data;

/// <summary>
/// Shared DataProtection key ring — reads from rn_fip DB (owned by RISE portal).
/// RN is a consumer only; DisableAutomaticKeyGeneration is set in Program.cs.
/// </summary>
public class DataProtectionKeyContext : DbContext, IDataProtectionKeyContext
{
    public DataProtectionKeyContext(DbContextOptions<DataProtectionKeyContext> options)
        : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
}
