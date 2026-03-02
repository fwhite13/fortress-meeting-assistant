using Microsoft.EntityFrameworkCore;
using RefugeMeetingAssistant.Api.Data.Entities;

namespace RefugeMeetingAssistant.Api.Data;

/// <summary>
/// EF Core DbContext for the Refuge Meeting Assistant extension layer.
/// 
/// We store only OUR data here (4 tables). LMA owns transcripts, summaries, 
/// and call metadata in DynamoDB. We cross-reference via Meeting.LmaCallId.
/// </summary>
public class MeetingAssistantDbContext : DbContext
{
    public MeetingAssistantDbContext(DbContextOptions<MeetingAssistantDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<BotConfig> BotConfigs => Set<BotConfig>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<ActionItem> ActionItems => Set<ActionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- User ----
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.EntraObjectId).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
        });

        // ---- BotConfig (1:1 with User) ----
        modelBuilder.Entity<BotConfig>(e =>
        {
            e.HasIndex(b => b.UserId).IsUnique();
            e.HasOne(b => b.User)
                .WithOne(u => u.BotConfig)
                .HasForeignKey<BotConfig>(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Meeting ----
        modelBuilder.Entity<Meeting>(e =>
        {
            e.HasIndex(m => m.UserId);
            e.HasIndex(m => m.Status);
            e.HasIndex(m => m.CreatedAt).IsDescending();
            e.HasIndex(m => m.LmaCallId);

            e.HasOne(m => m.User)
                .WithMany(u => u.Meetings)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- ActionItem ----
        modelBuilder.Entity<ActionItem>(e =>
        {
            e.HasIndex(a => a.MeetingId);
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => a.IsCompleted);

            e.HasOne(a => a.Meeting)
                .WithMany(m => m.ActionItems)
                .HasForeignKey(a => a.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.User)
                .WithMany(u => u.ActionItems)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
