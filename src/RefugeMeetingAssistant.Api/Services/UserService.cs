using Microsoft.EntityFrameworkCore;
using RefugeMeetingAssistant.Api.Data;
using RefugeMeetingAssistant.Api.Data.Entities;

namespace RefugeMeetingAssistant.Api.Services;

/// <summary>
/// Manages user provisioning and Entra ID mapping.
/// Auto-provisions users on first login.
/// </summary>
public class UserService
{
    private readonly MeetingAssistantDbContext _db;
    private readonly ILogger<UserService> _logger;

    public UserService(MeetingAssistantDbContext db, ILogger<UserService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get or create a user by Entra Object ID. Called on every authenticated request.
    /// Creates a default BotConfig for new users.
    /// </summary>
    public async Task<User> GetOrCreateUserAsync(string entraObjectId, string email, string displayName)
    {
        var user = await _db.Users
            .Include(u => u.BotConfig)
            .FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId);

        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return user;
        }

        // Auto-provision new user
        user = new User
        {
            EntraObjectId = entraObjectId,
            Email = email,
            DisplayName = displayName,
            LastLoginAt = DateTime.UtcNow
        };

        // Create default bot config
        var botConfig = new BotConfig
        {
            UserId = user.UserId,
            BotName = $"{displayName}'s Notetaker"
        };

        _db.Users.Add(user);
        _db.BotConfigs.Add(botConfig);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Provisioned new user: {Email} ({EntraId})", email, entraObjectId);
        return user;
    }

    /// <summary>
    /// Get user by ID
    /// </summary>
    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _db.Users
            .Include(u => u.BotConfig)
            .FirstOrDefaultAsync(u => u.UserId == userId);
    }

    /// <summary>
    /// Get user by Entra Object ID
    /// </summary>
    public async Task<User?> GetUserByEntraIdAsync(string entraObjectId)
    {
        return await _db.Users
            .Include(u => u.BotConfig)
            .FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId);
    }
}
