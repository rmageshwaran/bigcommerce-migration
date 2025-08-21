using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Functions.Orchestrators;
using AzureQueueMessage = Azure.Storage.Queues.Models.QueueMessage;

namespace BigCommerce.Migration.Functions.Functions;

/// <summary>
/// Azure Functions for queue-based migration operations
/// Integrates with Durable Function orchestrators for actual migration processing
/// </summary>
public class MigrationQueueFunctions
{
    private readonly ILogger<MigrationQueueFunctions> _logger;
    private readonly IQueueService _queueService;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly IOpenSearchService _openSearchService;

    /// <summary>
    /// Initializes a new instance of the MigrationQueueFunctions class
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="queueService">Queue service for message operations</param>
    /// <param name="migrationStorageService">Migration storage service</param>
    /// <param name="openSearchService">OpenSearch service for logging</param>
    public MigrationQueueFunctions(
        ILogger<MigrationQueueFunctions> logger,
        IQueueService queueService,
        IMigrationStorageService migrationStorageService,
        IOpenSearchService openSearchService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
    }

    /// <summary>
    /// Processes migration start messages from the queue and starts the Durable Function orchestrator
    /// </summary>
    /// <param name="azureQueueMessage">Migration start message from Azure Storage Queue trigger</param>
    /// <param name="durableTaskClient">Durable task client for starting orchestrators</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("ProcessMigrationStartMessage")]
    public async Task ProcessMigrationStartMessage(
        [QueueTrigger("%MigrationStartQueueName%", Connection = "AzureWebJobsStorage")] AzureQueueMessage azureQueueMessage,
        [DurableClient] DurableTaskClient durableTaskClient,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Convert Azure Queue Message to our custom QueueMessage model
            var queueMessage = ConvertAzureQueueMessage(azureQueueMessage);
            
            await ProcessMigrationStartMessageInternal(queueMessage, new DurableTaskClientWrapper(durableTaskClient), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessMigrationStartMessage failed for MessageId: {MessageId}: {ErrorMessage}", 
                azureQueueMessage.MessageId, ex.Message);
            
            // 🚨 DISABLED: Don't re-throw to prevent Azure Functions retry behavior
            // throw; // Re-throw to ensure proper retry behavior
        }
    }

    /// <summary>
    /// Processes migration start messages from the queue and starts the Durable Function orchestrator
    /// Internal implementation that accepts the interface for testability
    /// </summary>
    /// <param name="queueMessage">Migration start message from queue trigger</param>
    /// <param name="durableTaskClient">Durable task client for starting orchestrators</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ProcessMigrationStartMessageInternal(
        Core.Models.QueueMessage queueMessage,
        IDurableTaskClient durableTaskClient,
        CancellationToken cancellationToken = default)
    {
        var migrationId = "unknown";
        
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Processing migration start message. MessageId: {MessageId}", queueMessage.MessageId);

            // Validate and parse the queue message
            var validationResult = await _queueService.ValidateQueueMessageAsync(queueMessage).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Invalid migration start message. MessageId: {MessageId}, Validation: {ValidationError}", 
                    queueMessage.MessageId, validationResult.ValidationError);
                
                // Create dead letter message (would be sent via separate process)
                var deadLetterMessage = _queueService.CreateDeadLetterMessage(queueMessage, 
                    $"Invalid message format: {validationResult.ValidationError}");
                
                _logger.LogWarning("Created dead letter message for invalid message: {DeadLetterMessageId}", 
                    deadLetterMessage.MessageId);
                return;
            }

            // Extract and parse the migration request from the queue message
            var migrationData = await ExtractMigrationDataFromMessage(queueMessage).ConfigureAwait(false);
            if (migrationData == null)
            {
                _logger.LogError("Failed to extract migration data from message. MessageId: {MessageId}", queueMessage.MessageId);
                
                var deadLetterMessage = _queueService.CreateDeadLetterMessage(queueMessage, 
                    "Failed to extract migration data from queue message");
                
                _logger.LogError("Created dead letter message for data extraction failure: {DeadLetterMessageId}", 
                    deadLetterMessage.MessageId);
                return;
            }

            migrationId = migrationData.MigrationId;
            
            _logger.LogInformation("Extracted migration data for MigrationId: {MigrationId}", migrationId);

            // Update migration status to InProgress
            try
            {
                var migrationEntry = await _migrationStorageService.GetMigrationAsync(migrationId).ConfigureAwait(false);
                if (migrationEntry != null)
                {
                    migrationEntry.Status = Core.Models.MigrationStatus.InProgress;
                    migrationEntry.UpdatedAt = DateTime.UtcNow;
                    await _migrationStorageService.UpdateMigrationAsync(migrationEntry);
                    
                    _logger.LogInformation("Updated migration status to InProgress for MigrationId: {MigrationId}", migrationId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update migration status for MigrationId: {MigrationId}", migrationId);
                // Continue with orchestrator start - don't fail the entire process
            }

            // Create orchestration request
            var orchestrationRequest = new MigrationOrchestrationRequest
            {
                MigrationId = migrationId,
                MigrationRequest = migrationData.MigrationRequest,
                CategoryTreeContext = migrationData.CategoryTreeContext
            };

            // Start the migration orchestrator
            var instanceId = await durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
                "MigrationDurableOrchestrator",
                orchestrationRequest,
                new StartOrchestrationOptions 
                { 
                    InstanceId = $"migration-{migrationId}",
                    StartAt = DateTime.UtcNow
                });

            _logger.LogInformation("Started migration orchestrator for MigrationId: {MigrationId}, InstanceId: {InstanceId}", 
                migrationId, instanceId);

            // Log successful orchestrator start
            await _openSearchService.LogMigrationEventAsync("MigrationOrchestratorStarted", migrationId, new
            {
                messageId = queueMessage.MessageId,
                instanceId = instanceId,
                entities = migrationData.MigrationRequest.Entities,
                sourceStore = migrationData.MigrationRequest.SourceStore?.StoreId ?? "unknown",
                destinationStore = migrationData.MigrationRequest.DestinationStore?.StoreId ?? "unknown"
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Successfully started migration processing for MigrationId: {MigrationId}", migrationId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Migration start processing was cancelled. MessageId: {MessageId}, MigrationId: {MigrationId}", 
                queueMessage.MessageId, migrationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing migration start message. MessageId: {MessageId}, MigrationId: {MigrationId}", 
                queueMessage.MessageId, migrationId);
            
            // Update migration status to Failed if we have the migration ID
            if (migrationId != "unknown")
            {
                try
                {
                    var migrationEntry = await _migrationStorageService.GetMigrationAsync(migrationId);
                    if (migrationEntry != null)
                    {
                        migrationEntry.Status = Core.Models.MigrationStatus.Failed;
                        migrationEntry.UpdatedAt = DateTime.UtcNow;
                        await _migrationStorageService.UpdateMigrationAsync(migrationEntry);
                    }
                }
                catch (Exception updateEx)
                {
                    _logger.LogWarning(updateEx, "Failed to update migration status to Failed for MigrationId: {MigrationId}", migrationId);
                }
            }
            
            var deadLetterMessage = _queueService.CreateDeadLetterMessage(queueMessage, 
                $"Unexpected error: {ex.Message}");
            
            _logger.LogError("Created dead letter message for unexpected error: {DeadLetterMessageId}", 
                deadLetterMessage.MessageId);
        }
    }

    #region Helper Methods

    /// <summary>
    /// Converts Azure Storage Queue Message to our custom QueueMessage model
    /// </summary>
    /// <param name="azureQueueMessage">Azure Storage Queue Message</param>
    /// <returns>Custom QueueMessage model</returns>
    private Core.Models.QueueMessage ConvertAzureQueueMessage(AzureQueueMessage azureQueueMessage)
    {
        var content = azureQueueMessage.Body?.ToString() ?? string.Empty;
        var messageType = ExtractMessageTypeFromContent(content);
        
        var queueMessage = new Core.Models.QueueMessage
        {
            MessageId = azureQueueMessage.MessageId,
            Content = content,
            MessageType = messageType,
            PopReceipt = azureQueueMessage.PopReceipt,
            DequeueCount = (int)azureQueueMessage.DequeueCount,
            InsertionTime = azureQueueMessage.InsertedOn?.DateTime ?? DateTime.UtcNow,
            ExpirationTime = azureQueueMessage.ExpiresOn?.DateTime ?? DateTime.UtcNow.AddDays(7),
            NextVisibleTime = azureQueueMessage.NextVisibleOn?.DateTime
        };
        
        return queueMessage;
    }

    /// <summary>
    /// Extracts MessageType from JSON content
    /// </summary>
    /// <param name="content">JSON content from queue message</param>
    /// <returns>Message type or empty string if not found</returns>
    private string ExtractMessageTypeFromContent(string content)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            using var document = JsonDocument.Parse(content);
            
            if (document.RootElement.TryGetProperty("messageType", out var messageTypeElement))
            {
                return messageTypeElement.GetString() ?? string.Empty;
            }
            
            // Also try PascalCase variant
            if (document.RootElement.TryGetProperty("MessageType", out var messageTypeElementPascal))
            {
                return messageTypeElementPascal.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract MessageType from content");
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts migration data from queue message
    /// </summary>
    /// <param name="queueMessage">Queue message</param>
    /// <returns>Migration start data or null</returns>
    private async Task<MigrationStartData?> ExtractMigrationDataFromMessage(Core.Models.QueueMessage queueMessage)
    {
        try
        {
            var messageContent = await _queueService.ParseQueueMessageAsync<dynamic>(queueMessage);
            
            // Parse the message content to extract migration data
            var jsonString = JsonSerializer.Serialize(messageContent);
            var migrationStartData = JsonSerializer.Deserialize<MigrationStartData>(jsonString, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            return migrationStartData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract migration data from message. MessageId: {MessageId}", queueMessage.MessageId);
            return null;
        }
    }

    #endregion
}

/// <summary>
/// Data model for migration start message from queue
/// </summary>
public class MigrationStartData
{
    public string MigrationId { get; set; } = string.Empty;
    public MigrationRequest MigrationRequest { get; set; } = new();
    public CategoryTreeContext? CategoryTreeContext { get; set; }
} 
