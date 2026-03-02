using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefugeMeetingAssistant.Api.Data;
using RefugeMeetingAssistant.Api.Models;

namespace RefugeMeetingAssistant.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ActionItemsController : ControllerBase
{
    private readonly MeetingAssistantDbContext _db;
    private readonly ILogger<ActionItemsController> _logger;
    private readonly IHostEnvironment _environment;

    public ActionItemsController(MeetingAssistantDbContext db, ILogger<ActionItemsController> logger, IHostEnvironment environment)
    {
        _db = db;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// List all action items across meetings for the current user.
    /// </summary>
    [HttpGet("action-items")]
    [ProducesResponseType(typeof(List<ActionItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllActionItems(
        [FromQuery] bool? completed = null,
        [FromQuery] string? owner = null)
    {
        var userId = GetUserId();

        var query = _db.ActionItems
            .Where(a => a.UserId == userId)
            .AsQueryable();

        if (completed.HasValue)
            query = query.Where(a => a.IsCompleted == completed.Value);

        if (!string.IsNullOrEmpty(owner))
            query = query.Where(a => a.Owner != null && a.Owner.Contains(owner));

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Include(a => a.Meeting)
            .Select(a => new ActionItemDto
            {
                ActionItemId = a.ActionItemId,
                MeetingId = a.MeetingId,
                Description = a.Description,
                Owner = a.Owner,
                DueDate = a.DueDate,
                IsCompleted = a.IsCompleted,
                CompletedAt = a.CompletedAt,
                CreatedAt = a.CreatedAt,
                MeetingTitle = a.Meeting != null ? a.Meeting.Title : null
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>
    /// Get action items for a specific meeting.
    /// </summary>
    [HttpGet("meetings/{meetingId:guid}/action-items")]
    [ProducesResponseType(typeof(List<ActionItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMeetingActionItems(Guid meetingId)
    {
        var userId = GetUserId();

        var items = await _db.ActionItems
            .Where(a => a.MeetingId == meetingId && a.UserId == userId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ActionItemDto
            {
                ActionItemId = a.ActionItemId,
                MeetingId = a.MeetingId,
                Description = a.Description,
                Owner = a.Owner,
                DueDate = a.DueDate,
                IsCompleted = a.IsCompleted,
                CompletedAt = a.CompletedAt,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>
    /// Update an action item (mark complete, edit description, etc).
    /// </summary>
    [HttpPatch("action-items/{id:guid}")]
    [ProducesResponseType(typeof(ActionItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateActionItem(Guid id, [FromBody] UpdateActionItemRequest request)
    {
        var userId = GetUserId();
        var item = await _db.ActionItems
            .FirstOrDefaultAsync(a => a.ActionItemId == id && a.UserId == userId);

        if (item == null)
            return NotFound(new { error = "Action item not found" });

        if (request.IsCompleted.HasValue)
        {
            item.IsCompleted = request.IsCompleted.Value;
            item.CompletedAt = request.IsCompleted.Value ? DateTime.UtcNow : null;
        }
        if (request.Description != null) item.Description = request.Description;
        if (request.Owner != null) item.Owner = request.Owner;
        if (request.DueDate.HasValue) item.DueDate = request.DueDate;

        await _db.SaveChangesAsync();

        return Ok(new ActionItemDto
        {
            ActionItemId = item.ActionItemId,
            MeetingId = item.MeetingId,
            Description = item.Description,
            Owner = item.Owner,
            DueDate = item.DueDate,
            IsCompleted = item.IsCompleted,
            CompletedAt = item.CompletedAt,
            CreatedAt = item.CreatedAt
        });
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
