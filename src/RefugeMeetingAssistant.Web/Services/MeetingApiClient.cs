using System.Net.Http.Json;

namespace RefugeMeetingAssistant.Web.Services;

/// <summary>
/// HTTP client for the Refuge Meeting Assistant API.
/// Handles both dev auth (X-User-Id header) and Entra ID (Bearer token).
/// </summary>
public class MeetingApiClient
{
    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    public MeetingApiClient(
        HttpClient http,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration)
    {
        _http = http;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    private void PrepareRequest()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var useDevAuth = _configuration.GetValue<bool>("Auth:UseDev", false);

        if (useDevAuth)
        {
            // Dev mode: pass user ID header that the API's DevAuthHandler reads
            var userId = user?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "00000000-0000-0000-0000-000000000001";
            _http.DefaultRequestHeaders.Remove("X-User-Id");
            _http.DefaultRequestHeaders.Add("X-User-Id", userId);
            // Also set a bearer token so the API dev auth handler accepts
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "dev-token");
        }
        // In prod mode, token will be injected via Microsoft.Identity.Web downstreamAPI or manually
    }

    // ---- Meetings ----

    public async Task<MeetingListResponse?> GetMeetingsAsync(int page = 1, int pageSize = 20, string? status = null)
    {
        PrepareRequest();
        var url = $"/api/meetings?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return await _http.GetFromJsonAsync<MeetingListResponse>(url);
    }

    public async Task<MeetingDetailDto?> GetMeetingAsync(Guid meetingId)
    {
        PrepareRequest();
        return await _http.GetFromJsonAsync<MeetingDetailDto>($"/api/meetings/{meetingId}");
    }

    public async Task<JoinMeetingResponse?> JoinMeetingAsync(string url, string? title = null)
    {
        PrepareRequest();
        var response = await _http.PostAsJsonAsync("/api/meetings/join", new { url, title });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JoinMeetingResponse>();
    }

    public async Task StopMeetingAsync(Guid meetingId)
    {
        PrepareRequest();
        await _http.PostAsync($"/api/meetings/{meetingId}/stop", null);
    }

    public async Task DeleteMeetingAsync(Guid meetingId)
    {
        PrepareRequest();
        await _http.DeleteAsync($"/api/meetings/{meetingId}");
    }

    // ---- Summaries ----

    public async Task<SummaryDto?> GetSummaryAsync(Guid meetingId)
    {
        PrepareRequest();
        try
        {
            return await _http.GetFromJsonAsync<SummaryDto>($"/api/meetings/{meetingId}/summary");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // ---- Action Items ----

    public async Task<List<ActionItemDto>?> GetActionItemsAsync(bool? completed = null)
    {
        PrepareRequest();
        var url = "/api/action-items";
        if (completed.HasValue) url += $"?completed={completed.Value}";
        return await _http.GetFromJsonAsync<List<ActionItemDto>>(url);
    }

    public async Task UpdateActionItemAsync(Guid id, bool isCompleted)
    {
        PrepareRequest();
        await _http.PatchAsJsonAsync($"/api/action-items/{id}", new { isCompleted });
    }

    // ---- Bot Config ----

    public async Task<BotConfigDto?> GetBotConfigAsync()
    {
        PrepareRequest();
        try
        {
            return await _http.GetFromJsonAsync<BotConfigDto>("/api/bot-config");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task UpdateBotConfigAsync(UpdateBotConfigRequest request)
    {
        PrepareRequest();
        await _http.PutAsJsonAsync("/api/bot-config", request);
    }

    // ---- Search ----

    public async Task<SearchResponse?> SearchAsync(string query)
    {
        PrepareRequest();
        return await _http.GetFromJsonAsync<SearchResponse>($"/api/search?q={Uri.EscapeDataString(query)}");
    }
}

// ---- DTOs (matching API models) ----

public record JoinMeetingResponse
{
    public Guid MeetingId { get; init; }
    public string Status { get; init; } = "";
    public string Message { get; init; } = "";
}

public record MeetingDto
{
    public Guid MeetingId { get; init; }
    public string? Title { get; init; }
    public string? MeetingUrl { get; init; }
    public string Platform { get; init; } = "";
    public string CaptureMethod { get; init; } = "";
    public string Status { get; init; } = "";
    public int? DurationSeconds { get; init; }
    public int? ParticipantCount { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool HasTranscript { get; init; }
    public bool HasSummary { get; init; }
    public int ActionItemCount { get; init; }
}

public record MeetingDetailDto : MeetingDto
{
    public TranscriptDto? Transcript { get; init; }
    public SummaryDto? Summary { get; init; }
    public List<ActionItemDto> ActionItems { get; init; } = new();
}

public record MeetingListResponse
{
    public List<MeetingDto> Meetings { get; init; } = new();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public record TranscriptDto
{
    public Guid TranscriptId { get; init; }
    public string FullText { get; init; } = "";
    public List<string>? Speakers { get; init; }
    public int? DurationSeconds { get; init; }
    public int? SegmentCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record SummaryDto
{
    public Guid SummaryId { get; init; }
    public string Overview { get; init; } = "";
    public List<string>? KeyDecisions { get; init; }
    public List<string>? KeyTopics { get; init; }
    public List<string>? OpenQuestions { get; init; }
    public List<string>? Participants { get; init; }
    public string? ModelId { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<ActionItemDto> ActionItems { get; init; } = new();
}

public record ActionItemDto
{
    public Guid ActionItemId { get; init; }
    public Guid MeetingId { get; init; }
    public string Description { get; init; } = "";
    public string? Owner { get; init; }
    public DateTime? DueDate { get; init; }
    public bool IsCompleted { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? MeetingTitle { get; init; }
}

public record BotConfigDto
{
    public Guid ConfigId { get; init; }
    public string BotName { get; init; } = "Refuge Notetaker";
    public bool AutoJoin { get; init; }
    public string SummaryStyle { get; init; } = "standard";
    public bool IncludeActionItems { get; init; } = true;
    public bool IncludeKeyDecisions { get; init; } = true;
    public bool IncludeKeyTopics { get; init; } = true;
    public bool IncludeOpenQuestions { get; init; } = true;
}

public record UpdateBotConfigRequest
{
    public string? BotName { get; init; }
    public string? SummaryStyle { get; init; }
    public bool? IncludeActionItems { get; init; }
    public bool? IncludeKeyDecisions { get; init; }
    public bool? IncludeKeyTopics { get; init; }
    public bool? IncludeOpenQuestions { get; init; }
}

public record SearchResponse
{
    public List<SearchResult> Results { get; init; } = new();
    public int TotalCount { get; init; }
}

public record SearchResult
{
    public Guid MeetingId { get; init; }
    public string? Title { get; init; }
    public string Snippet { get; init; } = "";
    public string Source { get; init; } = "";
    public DateTime Date { get; init; }
}
