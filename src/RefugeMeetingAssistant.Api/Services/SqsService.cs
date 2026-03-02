using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using RefugeMeetingAssistant.Api.Models;

namespace RefugeMeetingAssistant.Api.Services;

/// <summary>
/// SQS integration for VP bot orchestration.
/// Sends bot commands to the queue; the Node.js VP bot worker consumes them.
/// </summary>
public class SqsService
{
    private readonly IAmazonSQS _sqsClient;
    private readonly ILogger<SqsService> _logger;
    private readonly string _botCommandsQueueUrl;
    private readonly string _processingQueueUrl;

    public SqsService(IAmazonSQS sqsClient, IConfiguration config, ILogger<SqsService> logger)
    {
        _sqsClient = sqsClient;
        _logger = logger;
        _botCommandsQueueUrl = config["AWS:SQS:BotCommandsQueueUrl"] ?? "";
        _processingQueueUrl = config["AWS:SQS:ProcessingQueueUrl"] ?? "";
    }

    /// <summary>
    /// Send a bot join command to the VP bot queue.
    /// </summary>
    public async Task<bool> SendBotCommandAsync(BotCommand command)
    {
        if (string.IsNullOrEmpty(_botCommandsQueueUrl))
        {
            _logger.LogWarning("SQS BotCommandsQueueUrl not configured. Skipping SQS send (dev mode).");
            return false;
        }

        try
        {
            var messageBody = JsonSerializer.Serialize(command, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var response = await _sqsClient.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = _botCommandsQueueUrl,
                MessageBody = messageBody,
                // MessageGroupId removed — only needed for FIFO queues (.fifo suffix)
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["Action"] = new() { DataType = "String", StringValue = command.Action },
                    ["Platform"] = new() { DataType = "String", StringValue = command.Platform }
                }
            });

            _logger.LogInformation("Sent bot command to SQS: {Action} for meeting {MeetingId}, MessageId: {MessageId}",
                command.Action, command.MeetingId, response.MessageId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send bot command to SQS for meeting {MeetingId}", command.MeetingId);
            return false;
        }
    }

    /// <summary>
    /// Send a processing command (e.g., transcribe, summarize) to the processing queue.
    /// </summary>
    public async Task<bool> SendProcessingCommandAsync(string action, Guid meetingId, string? audioS3Key = null)
    {
        if (string.IsNullOrEmpty(_processingQueueUrl))
        {
            _logger.LogWarning("SQS ProcessingQueueUrl not configured. Skipping SQS send (dev mode).");
            return false;
        }

        try
        {
            var command = new { action, meetingId, audioS3Key };
            var messageBody = JsonSerializer.Serialize(command, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await _sqsClient.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = _processingQueueUrl,
                MessageBody = messageBody
            });

            _logger.LogInformation("Sent processing command: {Action} for meeting {MeetingId}", action, meetingId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send processing command for meeting {MeetingId}", meetingId);
            return false;
        }
    }

    /// <summary>
    /// Ensure SQS queues exist (for local dev with LocalStack).
    /// </summary>
    public async Task EnsureQueuesExistAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_botCommandsQueueUrl))
            {
                // Check if queue exists by trying to get attributes
                await _sqsClient.GetQueueAttributesAsync(new GetQueueAttributesRequest
                {
                    QueueUrl = _botCommandsQueueUrl,
                    AttributeNames = new List<string> { "QueueArn" }
                });
                _logger.LogInformation("SQS bot commands queue verified: {Url}", _botCommandsQueueUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SQS queue check failed (non-fatal in dev): {Error}", ex.Message);
        }
    }
}
