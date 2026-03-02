using Microsoft.EntityFrameworkCore;
using RefugeMeetingAssistant.Api.Data;
using RefugeMeetingAssistant.Api.Data.Entities;
using RefugeMeetingAssistant.Api.Models;

namespace RefugeMeetingAssistant.Api.Services;

/// <summary>
/// Per-user bot configuration management.
/// </summary>
public class BotConfigService
{
    private readonly MeetingAssistantDbContext _db;
    private readonly ILogger<BotConfigService> _logger;

    public BotConfigService(MeetingAssistantDbContext db, ILogger<BotConfigService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<BotConfig?> GetConfigAsync(Guid userId)
    {
        return await _db.BotConfigs.FirstOrDefaultAsync(b => b.UserId == userId);
    }

    public async Task<BotConfig> UpdateConfigAsync(Guid userId, UpdateBotConfigRequest request)
    {
        var config = await _db.BotConfigs.FirstOrDefaultAsync(b => b.UserId == userId);

        if (config == null)
        {
            // Auto-create if missing (shouldn't happen with auto-provision)
            config = new BotConfig { UserId = userId };
            _db.BotConfigs.Add(config);
        }

        if (request.BotName != null) config.BotName = request.BotName;
        if (request.SummaryStyle != null) config.SummaryStyle = request.SummaryStyle;
        if (request.IncludeActionItems.HasValue) config.IncludeActionItems = request.IncludeActionItems.Value;
        if (request.IncludeKeyDecisions.HasValue) config.IncludeKeyDecisions = request.IncludeKeyDecisions.Value;
        if (request.IncludeKeyTopics.HasValue) config.IncludeKeyTopics = request.IncludeKeyTopics.Value;
        if (request.IncludeOpenQuestions.HasValue) config.IncludeOpenQuestions = request.IncludeOpenQuestions.Value;

        config.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated bot config for user {UserId}", userId);
        return config;
    }

    public BotConfigDto ToDto(BotConfig config)
    {
        return new BotConfigDto
        {
            ConfigId = config.ConfigId,
            BotName = config.BotName,
            SummaryStyle = config.SummaryStyle,
            IncludeActionItems = config.IncludeActionItems,
            IncludeKeyDecisions = config.IncludeKeyDecisions,
            IncludeKeyTopics = config.IncludeKeyTopics,
            IncludeOpenQuestions = config.IncludeOpenQuestions
        };
    }
}
