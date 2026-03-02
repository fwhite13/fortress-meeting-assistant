using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RefugeMeetingAssistant.Api.Models;
using RefugeMeetingAssistant.Api.Services;

namespace RefugeMeetingAssistant.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MeetingsController : ControllerBase
{
    private readonly MeetingService _meetingService;
    private readonly ILogger<MeetingsController> _logger;
    private readonly IHostEnvironment _environment;

    public MeetingsController(MeetingService meetingService, ILogger<MeetingsController> logger, IHostEnvironment environment)
    {
        _meetingService = meetingService;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Request VP bot to join a Teams meeting.
    /// </summary>
    [HttpPost("join")]
    [ProducesResponseType(typeof(JoinMeetingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> JoinMeeting([FromBody] JoinMeetingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { error = "Meeting URL is required" });

        var userId = GetUserId();
        var result = await _meetingService.JoinMeetingAsync(userId, request);
        return CreatedAtAction(nameof(GetMeeting), new { id = result.MeetingId }, result);
    }

    /// <summary>
    /// List user's meetings (paginated).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(MeetingListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMeetings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        var userId = GetUserId();
        var result = await _meetingService.GetMeetingsAsync(userId, page, pageSize, status, search);
        return Ok(result);
    }

    /// <summary>
    /// Get meeting details (our metadata + LMA transcript/summary).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MeetingDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMeeting(Guid id)
    {
        var userId = GetUserId();
        var result = await _meetingService.GetMeetingDetailAsync(userId, id);
        if (result == null) return NotFound(new { error = "Meeting not found" });
        return Ok(result);
    }

    /// <summary>
    /// Stop recording.
    /// </summary>
    [HttpPost("{id:guid}/stop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StopMeeting(Guid id)
    {
        var userId = GetUserId();
        var meeting = await _meetingService.StopMeetingAsync(userId, id);
        if (meeting == null) return NotFound(new { error = "Meeting not found" });
        return Ok(new { meetingId = meeting.MeetingId, status = meeting.Status, message = "Stop command sent" });
    }

    /// <summary>
    /// Delete meeting.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMeeting(Guid id)
    {
        var userId = GetUserId();
        var deleted = await _meetingService.DeleteMeetingAsync(userId, id);
        if (!deleted) return NotFound(new { error = "Meeting not found" });
        return NoContent();
    }

    /// <summary>
    /// Update meeting status (internal — called by VP bot worker).
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateMeetingStatusRequest request)
    {
        var meeting = await _meetingService.UpdateStatusAsync(id, request);
        if (meeting == null) return NotFound(new { error = "Meeting not found" });
        return Ok(new { meetingId = meeting.MeetingId, status = meeting.Status });
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
