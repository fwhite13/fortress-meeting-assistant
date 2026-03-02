using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefugeMeetingAssistant.Api.Data;
using RefugeMeetingAssistant.Api.Models;

namespace RefugeMeetingAssistant.Api.Controllers;

[ApiController]
[Route("api/health")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly MeetingAssistantDbContext _db;
    private readonly ILogger<HealthController> _logger;

    public HealthController(MeetingAssistantDbContext db, ILogger<HealthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Service health check — verifies API is running and DB is reachable.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetHealth()
    {
        var services = new Dictionary<string, string>();
        var isHealthy = true;

        // Check database
        try
        {
            var canConnect = await _db.Database.CanConnectAsync();
            services["database"] = canConnect ? "healthy" : "unhealthy";
            if (!canConnect) isHealthy = false;
        }
        catch (Exception ex)
        {
            services["database"] = $"unhealthy: {ex.Message}";
            isHealthy = false;
        }

        // Check basic stats
        try
        {
            var userCount = await _db.Users.CountAsync();
            var meetingCount = await _db.Meetings.CountAsync();
            services["users"] = userCount.ToString();
            services["meetings"] = meetingCount.ToString();
        }
        catch
        {
            services["stats"] = "unavailable";
        }

        var response = new HealthResponse
        {
            Status = isHealthy ? "healthy" : "unhealthy",
            Timestamp = DateTime.UtcNow,
            Services = services
        };

        return isHealthy ? Ok(response) : StatusCode(503, response);
    }

    /// <summary>
    /// LMA stack health check (placeholder for future integration).
    /// </summary>
    [HttpGet("lma")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult GetLmaHealth()
    {
        return Ok(new HealthResponse
        {
            Status = "not_configured",
            Timestamp = DateTime.UtcNow,
            Services = new Dictionary<string, string>
            {
                ["lma_stack"] = "not_deployed",
                ["note"] = "Deploy LMA via CloudFormation before checking health"
            }
        });
    }
}
