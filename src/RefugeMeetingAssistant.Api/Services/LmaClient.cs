using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RefugeMeetingAssistant.Api.Models;

namespace RefugeMeetingAssistant.Api.Services;

/// <summary>
/// Client for LMA's AppSync GraphQL API.
/// 
/// LMA stores all call/meeting data in DynamoDB accessed via AppSync.
/// We query transcripts, summaries, and call metadata through here.
/// 
/// Authentication: Uses Cognito JWT or IAM auth to call AppSync.
/// In dev mode, returns mock data.
/// </summary>
public class LmaClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LmaClient> _logger;
    private readonly string? _appSyncUrl;
    private readonly bool _useMock;

    public LmaClient(HttpClient httpClient, IConfiguration config, ILogger<LmaClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _appSyncUrl = config["LMA:AppSyncUrl"];
        _useMock = config.GetValue<bool>("LMA:UseMock", true);
    }

    /// <summary>
    /// Get transcript for an LMA call.
    /// </summary>
    public async Task<LmaTranscriptDto?> GetTranscriptAsync(string lmaCallId)
    {
        if (_useMock || string.IsNullOrEmpty(_appSyncUrl))
        {
            _logger.LogInformation("LMA mock: returning sample transcript for call {CallId}", lmaCallId);
            return GetMockTranscript();
        }

        try
        {
            var query = new
            {
                query = @"
                    query GetTranscriptSegments($callId: ID!) {
                        getTranscriptSegments(callId: $callId) {
                            TranscriptSegments {
                                Speaker
                                StartTime
                                EndTime
                                Transcript
                            }
                        }
                    }",
                variables = new { callId = lmaCallId }
            };

            var response = await SendGraphQlAsync(query);
            if (response == null) return null;

            // Parse the AppSync response into our DTO
            // Actual parsing depends on LMA's exact schema
            return ParseTranscriptResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get transcript from LMA for call {CallId}", lmaCallId);
            return null;
        }
    }

    /// <summary>
    /// Get summary for an LMA call.
    /// </summary>
    public async Task<LmaSummaryDto?> GetSummaryAsync(string lmaCallId)
    {
        if (_useMock || string.IsNullOrEmpty(_appSyncUrl))
        {
            _logger.LogInformation("LMA mock: returning sample summary for call {CallId}", lmaCallId);
            return GetMockSummary();
        }

        try
        {
            var query = new
            {
                query = @"
                    query GetCall($callId: ID!) {
                        getCall(CallId: $callId) {
                            CallId
                            Status
                            CallSummaryText
                        }
                    }",
                variables = new { callId = lmaCallId }
            };

            var response = await SendGraphQlAsync(query);
            if (response == null) return null;

            return ParseSummaryResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get summary from LMA for call {CallId}", lmaCallId);
            return null;
        }
    }

    /// <summary>
    /// Get call status from LMA.
    /// </summary>
    public async Task<string?> GetCallStatusAsync(string lmaCallId)
    {
        if (_useMock || string.IsNullOrEmpty(_appSyncUrl))
        {
            return "ENDED"; // Mock: call completed
        }

        try
        {
            var query = new
            {
                query = @"
                    query GetCall($callId: ID!) {
                        getCall(CallId: $callId) {
                            CallId
                            Status
                        }
                    }",
                variables = new { callId = lmaCallId }
            };

            var response = await SendGraphQlAsync(query);
            // Parse status from response
            return response?.RootElement
                .GetProperty("data")
                .GetProperty("getCall")
                .GetProperty("Status")
                .GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get call status from LMA for call {CallId}", lmaCallId);
            return null;
        }
    }

    /// <summary>
    /// Send a GraphQL request to AppSync.
    /// </summary>
    private async Task<JsonDocument?> SendGraphQlAsync(object query)
    {
        var json = JsonSerializer.Serialize(query);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // TODO: Add Cognito/IAM auth headers
        var response = await _httpClient.PostAsync(_appSyncUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("AppSync request failed: {Status} {Error}", response.StatusCode, error);
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(responseBody);
    }

    // ---- Response Parsers ----

    private LmaTranscriptDto? ParseTranscriptResponse(JsonDocument response)
    {
        // Parse based on LMA's actual AppSync schema
        // This is a best-effort implementation; exact field names depend on LMA version
        try
        {
            var segments = new List<LmaTranscriptSegmentDto>();
            var fullTextParts = new List<string>();
            var speakers = new HashSet<string>();

            var data = response.RootElement.GetProperty("data").GetProperty("getTranscriptSegments");
            if (data.TryGetProperty("TranscriptSegments", out var segs))
            {
                foreach (var seg in segs.EnumerateArray())
                {
                    var speaker = seg.GetProperty("Speaker").GetString() ?? "Unknown";
                    var transcript = seg.GetProperty("Transcript").GetString() ?? "";
                    speakers.Add(speaker);
                    fullTextParts.Add($"{speaker}: {transcript}");
                    segments.Add(new LmaTranscriptSegmentDto
                    {
                        Speaker = speaker,
                        StartTime = seg.GetProperty("StartTime").GetDouble(),
                        EndTime = seg.GetProperty("EndTime").GetDouble(),
                        Content = transcript
                    });
                }
            }

            return new LmaTranscriptDto
            {
                FullText = string.Join("\n", fullTextParts),
                Segments = segments,
                Speakers = speakers.ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse LMA transcript response");
            return null;
        }
    }

    private LmaSummaryDto? ParseSummaryResponse(JsonDocument response)
    {
        try
        {
            var call = response.RootElement.GetProperty("data").GetProperty("getCall");
            var summaryText = call.GetProperty("CallSummaryText").GetString();

            return new LmaSummaryDto
            {
                Overview = summaryText ?? "No summary available",
                // LMA stores summaries as text; parsing into structured fields
                // depends on the prompt template used in LMA's Bedrock config
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse LMA summary response");
            return null;
        }
    }

    // ---- Mock Data (dev mode) ----

    private static LmaTranscriptDto GetMockTranscript()
    {
        return new LmaTranscriptDto
        {
            FullText = "Speaker_0: Good morning everyone, let's get started with the project update.\n" +
                       "Speaker_1: Sure. The frontend team completed the dashboard redesign this week.\n" +
                       "Speaker_0: Great progress. What about the backend API changes?\n" +
                       "Speaker_2: We're on track. The new endpoints will be ready by Friday.\n" +
                       "Speaker_0: Perfect. Let's plan a demo for next Monday.\n" +
                       "Speaker_1: I'll schedule that. Also, we need to decide on the testing strategy.\n" +
                       "Speaker_0: Good point. Fred, can you draft a testing plan by Wednesday?\n" +
                       "Speaker_2: Will do.",
            Segments = new List<LmaTranscriptSegmentDto>
            {
                new() { Speaker = "Speaker_0", StartTime = 0, EndTime = 5, Content = "Good morning everyone, let's get started with the project update." },
                new() { Speaker = "Speaker_1", StartTime = 5.5, EndTime = 10, Content = "Sure. The frontend team completed the dashboard redesign this week." },
                new() { Speaker = "Speaker_0", StartTime = 10.5, EndTime = 14, Content = "Great progress. What about the backend API changes?" },
                new() { Speaker = "Speaker_2", StartTime = 14.5, EndTime = 19, Content = "We're on track. The new endpoints will be ready by Friday." },
                new() { Speaker = "Speaker_0", StartTime = 19.5, EndTime = 23, Content = "Perfect. Let's plan a demo for next Monday." },
                new() { Speaker = "Speaker_1", StartTime = 23.5, EndTime = 28, Content = "I'll schedule that. Also, we need to decide on the testing strategy." },
                new() { Speaker = "Speaker_0", StartTime = 28.5, EndTime = 33, Content = "Good point. Fred, can you draft a testing plan by Wednesday?" },
                new() { Speaker = "Speaker_2", StartTime = 33.5, EndTime = 35, Content = "Will do." },
            },
            Speakers = new List<string> { "Speaker_0", "Speaker_1", "Speaker_2" }
        };
    }

    private static LmaSummaryDto GetMockSummary()
    {
        return new LmaSummaryDto
        {
            Overview = "The team discussed project progress including the completed dashboard redesign and upcoming backend API changes. " +
                       "A demo was planned for next Monday and a testing strategy discussion was initiated.",
            KeyDecisions = new List<string>
            {
                "Demo scheduled for next Monday",
                "Testing plan to be drafted by Wednesday"
            },
            ActionItems = new List<string>
            {
                "Fred: Draft testing plan by Wednesday",
                "Speaker_1: Schedule demo for Monday"
            },
            KeyTopics = new List<string>
            {
                "Dashboard redesign completion",
                "Backend API progress",
                "Testing strategy"
            },
            OpenQuestions = new List<string>
            {
                "What testing framework to use?"
            },
            ModelId = "mock (LMA not connected)"
        };
    }
}
