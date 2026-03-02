using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RefugeMeetingAssistant.Api.Services;

namespace RefugeMeetingAssistant.Api.Controllers;

/// <summary>
/// Summary endpoints — reads from LMA's AppSync API.
/// LMA handles all summarization via Bedrock Claude. We just expose it.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class SummariesController : ControllerBase
{
    private readonly MeetingService _meetingService;
    private readonly ILogger<SummariesController> _logger;
    private readonly IHostEnvironment _environment;

    public SummariesController(MeetingService meetingService, ILogger<SummariesController> logger, IHostEnvironment environment)
    {
        _meetingService = meetingService;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Get AI summary for a meeting (from LMA).
    /// </summary>
    [HttpGet("meetings/{meetingId:guid}/summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(Guid meetingId)
    {
        var userId = GetUserId();
        var detail = await _meetingService.GetMeetingDetailAsync(userId, meetingId);

        if (detail?.Summary == null)
            return NotFound(new { error = "Summary not available — meeting may still be processing" });

        return Ok(detail.Summary);
    }

    /// <summary>
    /// Get transcript for a meeting (from LMA).
    /// </summary>
    [HttpGet("meetings/{meetingId:guid}/transcript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTranscript(Guid meetingId)
    {
        var userId = GetUserId();
        var detail = await _meetingService.GetMeetingDetailAsync(userId, meetingId);

        if (detail?.Transcript == null)
            return NotFound(new { error = "Transcript not available — meeting may still be processing" });

        return Ok(detail.Transcript);
    }

    private Guid GetUserId()
    {
        // Dev mode: allow header override and fallback to test user
        if (_environment.IsDevelopment())
        {
            if (Request.Headers.TryGetValue("X-User-Id", out var h) && Guid.TryParse(h.FirstOrDefault(), out var id))
                return id;
            var devClaim = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
            return Guid.TryParse(devClaim, out var devUid) ? devUid : Guid.Parse("00000000-0000-0000-0000-000000000001");
        }

        // Production: require authentication
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var uid))
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }
        return uid;
    }
}
