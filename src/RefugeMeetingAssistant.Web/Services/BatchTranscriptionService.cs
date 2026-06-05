using Amazon.Batch;
using Amazon.Batch.Model;
using Microsoft.EntityFrameworkCore;
using RefugeMeetingAssistant.Api.Data;

namespace RefugeMeetingAssistant.Web.Services;

public class BatchTranscriptionService
{
    private readonly IDbContextFactory<MeetingAssistantDbContext> _dbFactory;
    private readonly IAmazonBatch _batch;
    private readonly IConfiguration _config;
    private readonly ILogger<BatchTranscriptionService> _logger;

    private string JobQueue => _config["Firm:BatchJobQueue"] ?? "rn-transcription-queue";
    private string JobDefinition => _config["Firm:BatchJobDefinition"] ?? "rn-transcription-job";

    public BatchTranscriptionService(
        IDbContextFactory<MeetingAssistantDbContext> dbFactory,
        IAmazonBatch batch,
        IConfiguration config,
        ILogger<BatchTranscriptionService> logger)
    {
        _dbFactory = dbFactory;
        _batch = batch;
        _config = config;
        _logger = logger;
    }

    public async Task<string> SubmitTranscriptionJobAsync(Guid meetingId, string audioS3Key, DateTime? meetingDate = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var wikiEntries = await db.OrgWikiEntries.ToListAsync();

        string? wikiJson = null;
        if (wikiEntries.Count > 0)
        {
            var flatEntries = wikiEntries.Select(e => new { e.Term, e.Description, e.Source }).ToList();
            wikiJson = System.Text.Json.JsonSerializer.Serialize(flatEntries);
            _logger.LogInformation("RN: ORG_WIKI_JSON will contain {Count} entries for meeting {MeetingId}", wikiEntries.Count, meetingId);
        }

        var envVars = new List<Amazon.Batch.Model.KeyValuePair>
        {
            new() { Name = "MEETING_ID", Value = meetingId.ToString() },
            new() { Name = "AUDIO_S3_KEY", Value = audioS3Key },
            new() { Name = "S3_BUCKET", Value = _config["Firm:S3Bucket"] ?? "" },
            new() { Name = "FIRM_CALLBACK_URL", Value = _config["Firm:CallbackUrl"] ?? "" },
            new() { Name = "BOT_CALLBACK_SECRET", Value = _config["Firm:BotCallbackSecret"] ?? "" },
            new() { Name = "BEDROCK_MODEL_ID", Value = _config["Bedrock:SummaryModelId"] ?? "" },
            new() { Name = "AWS_REGION", Value = "us-east-1" },
        };

        if (meetingDate.HasValue)
            envVars.Add(new() { Name = "MEETING_DATE", Value = meetingDate.Value.ToString("yyyy-MM-dd") });

        if (wikiJson != null)
            envVars.Add(new() { Name = "ORG_WIKI_JSON", Value = wikiJson });

        var request = new SubmitJobRequest
        {
            JobName = $"rn-transcribe-{meetingId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            JobQueue = JobQueue,
            JobDefinition = JobDefinition,
            ContainerOverrides = new ContainerOverrides { Environment = envVars }
        };

        var response = await _batch.SubmitJobAsync(request);
        _logger.LogInformation("RN: Batch transcription job {JobId} submitted for meeting {MeetingId}", response.JobId, meetingId);
        return response.JobId;
    }
}
