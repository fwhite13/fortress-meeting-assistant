using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RefugeMeetingAssistant.Api.Models;
using RefugeMeetingAssistant.Api.Services;

namespace RefugeMeetingAssistant.Api.Controllers;

[ApiController]
[Route("api/bot-config")]
[Authorize]
public class BotConfigController : ControllerBase
{
    private readonly BotConfigService _botConfigService;
    private readonly ILogger<BotConfigController> _logger;
    private readonly IHostEnvironment _environment;

    public BotConfigController(BotConfigService botConfigService, ILogger<BotConfigController> logger, IHostEnvironment environment)
    {
        _botConfigService = botConfigService;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Get current user's bot configuration.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(BotConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConfig()
    {
        var userId = GetUserId();
        var config = await _botConfigService.GetConfigAsync(userId);

        if (config == null)
            return NotFound(new { error = "Bot config not found" });

        return Ok(_botConfigService.ToDto(config));
    }

    /// <summary>
    /// Update bot configuration.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(BotConfigDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdateBotConfigRequest request)
    {
        var userId = GetUserId();
        var config = await _botConfigService.UpdateConfigAsync(userId, request);
        return Ok(_botConfigService.ToDto(config));
    }

    private Guid GetUserId()
    {
        // Dev mode: allow header override and fallback to test user
        if (_environment.IsDevelopment())
        {
            if (Request.Headers.TryGetValue("X-User-Id", out var userIdHeader) &&
                Guid.TryParse(userIdHeader.FirstOrDefault(), out var parsedUserId))
                return parsedUserId;

            var devClaim = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
            if (Guid.TryParse(devClaim, out var devUserId))
                return devUserId;

            return Guid.Parse("00000000-0000-0000-0000-000000000001");
        }

        // Production: require authentication
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }
        return userId;
    }
}
