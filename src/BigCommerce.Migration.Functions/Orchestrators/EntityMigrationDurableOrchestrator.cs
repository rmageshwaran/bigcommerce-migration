using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Activities;

namespace BigCommerce.Migration.Functions.Orchestrators;

/// <summary>
/// Entity-specific migration orchestrator that handles discovery, batching, and processing of individual entity types
/// Supports cancellation, error handling, and progress tracking
/// </summary>
public static class EntityMigrationDurableOrchestrator
{
    /// <summary>
    /// Entity migration orchestrator function that processes a specific entity type with batching
    /// </summary>
    [Function("EntityMigrationDurableOrchestrator")]
    public static async Task<EntityMigrationResult> RunEntityMigrationOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger("EntityMigrationDurableOrchestrator");
        var input = context.GetInput<EntityMigrationRequest>();
        
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input), "Entity migration request is required");
        }

        var migrationId = input.MigrationId;
        var entityType = input.EntityType;
        
        var result = new EntityMigrationResult
        {
            EntityType = entityType,
            StartTime = context.CurrentUtcDateTime,
            ProcessedEntities = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            BatchResults = new List<BatchProcessingResult>()
        };

        try
        {
            logger.LogInformation("Starting {EntityType} migration for MigrationId: {MigrationId}", 
                entityType, migrationId);

            // Step 1: Check for cancellation before starting
            var cancellationCheck = await context.CallActivityAsync<CheckCancellationResult>(
                "CheckMigrationCancellation",
                migrationId);

            if (cancellationCheck.IsCancelled)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"Migration was cancelled before {entityType} processing began";
                result.EndTime = context.CurrentUtcDateTime;
                return result;
            }

            // Step 2: Check rate limiting before starting entity processing
            await context.CallActivityAsync(
                "CheckRateLimitActivity",
                new CheckRateLimitRequest
                {
                    StoreId = input.SourceStore?.StoreId ?? string.Empty,
                    EntityType = entityType
                });

            // Step 3: Discover entities to migrate
            logger.LogInformation("Discovering {EntityType} entities for MigrationId: {MigrationId}", 
                entityType, migrationId);

            var discoverRequest = new EntityDiscoveryRequest
            {
                MigrationId = migrationId,
                EntityType = entityType,
                SourceStore = input.SourceStore ?? new StoreConfiguration(),
                EntityConfig = new EntityConfiguration()
            };

            var discoverResult = await context.CallActivityAsync<EntityDiscoveryResult>(
                "DiscoverEntitiesActivity",
                discoverRequest);

            if (discoverResult.Errors.Any())
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"Failed to discover {entityType} entities: {string.Join(", ", discoverResult.Errors)}";
                result.EndTime = context.CurrentUtcDateTime;
                return result;
            }

            if (discoverResult.TotalCount == 0)
            {
                logger.LogInformation("No {EntityType} entities found to migrate for MigrationId: {MigrationId}", 
                    entityType, migrationId);
                result.IsSuccess = true;
                result.EndTime = context.CurrentUtcDateTime;
                return result;
            }

            logger.LogInformation("Discovered {EntityCount} {EntityType} entities for MigrationId: {MigrationId}", 
                discoverResult.TotalCount, entityType, migrationId);

            // Step 4: Start entity progress tracking
            await context.CallActivityAsync(
                "UpdateEntityProgressActivity",
                new UpdateEntityProgressRequest
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    Phase = "Processing",
                    TotalEntities = discoverResult.TotalCount,
                    ProcessedEntities = 0,
                    SuccessfulEntities = 0,
                    FailedEntities = 0
                });

            // Step 5: Process entities in batches
            var totalBatches = CalculateBatchCount(discoverResult.TotalCount, 10); // Default batch size of 10
            logger.LogInformation("Processing {EntityType} entities in {BatchCount} batches for MigrationId: {MigrationId}", 
                entityType, totalBatches, migrationId);

            for (int batchNumber = 1; batchNumber <= totalBatches; batchNumber++)
            {
                // Check for cancellation before each batch
                var batchCancellationCheck = await context.CallActivityAsync<CheckCancellationResult>(
                    "CheckMigrationCancellation",
                    migrationId);

                if (batchCancellationCheck.IsCancelled)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = $"Migration was cancelled during {entityType} batch {batchNumber} processing";
                    result.EndTime = context.CurrentUtcDateTime;
                    return result;
                }

                // Check rate limiting before each batch
                await context.CallActivityAsync(
                    "CheckRateLimitActivity",
                    new CheckRateLimitRequest
                    {
                        StoreId = input.SourceStore?.StoreId ?? string.Empty,
                        EntityType = entityType
                    });

                logger.LogInformation("Processing {EntityType} batch {BatchNumber}/{TotalBatches} for MigrationId: {MigrationId}", 
                    entityType, batchNumber, totalBatches, migrationId);

                try
                {
                    // Process the batch
                    var batchRequest = new BatchProcessingRequest
                    {
                        MigrationId = migrationId,
                        EntityType = entityType,
                        BatchNumber = batchNumber,
                        SourceStore = input.SourceStore ?? new StoreConfiguration(),
                        DestinationStore = input.DestinationStore ?? new StoreConfiguration(),
                        CategoryTreeContext = input.CategoryTreeContext ?? new CategoryTreeContext()
                    };

                    var batchResult = await context.CallActivityAsync<BatchProcessingResult>(
                        "ProcessEntityBatchActivity",
                        batchRequest);

                    result.BatchResults.Add(batchResult);
                    result.ProcessedEntities += batchResult.TotalProcessed;
                    result.SuccessfulEntities += batchResult.SuccessfulEntities;
                    result.FailedEntities += batchResult.FailedEntities;

                    logger.LogInformation("Completed {EntityType} batch {BatchNumber}/{TotalBatches} for MigrationId: {MigrationId}. " +
                                        "Processed: {ProcessedCount}, Successful: {SuccessfulCount}, Failed: {FailedCount}", 
                        entityType, batchNumber, totalBatches, migrationId, 
                        batchResult.TotalProcessed, batchResult.SuccessfulEntities, batchResult.FailedEntities);

                    // Update progress after each batch
                    await context.CallActivityAsync(
                        "UpdateEntityProgressActivity",
                        new UpdateEntityProgressRequest
                        {
                            MigrationId = migrationId,
                            EntityType = entityType,
                            Phase = "Processing",
                            TotalEntities = discoverResult.TotalCount,
                            ProcessedEntities = result.ProcessedEntities,
                            SuccessfulEntities = result.SuccessfulEntities,
                            FailedEntities = result.FailedEntities,
                            CurrentBatch = batchNumber,
                            TotalBatches = totalBatches
                        });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to process {EntityType} batch {BatchNumber} for MigrationId: {MigrationId}", 
                        entityType, batchNumber, migrationId);

                    // Create failed batch result
                    var failedBatchResult = new BatchProcessingResult
                    {
                        BatchNumber = batchNumber,
                        TotalProcessed = 0,
                        SuccessfulEntities = 0,
                        FailedEntities = 0, // We don't know how many entities were in this batch
                        ProcessingTime = TimeSpan.Zero
                    };

                    result.BatchResults.Add(failedBatchResult);

                    // Continue with next batch (continue-on-failure pattern)
                    logger.LogInformation("Continuing with next batch after {EntityType} batch {BatchNumber} failure for MigrationId: {MigrationId}", 
                        entityType, batchNumber, migrationId);
                }
            }

            // Step 6: Complete entity processing
            await context.CallActivityAsync(
                "UpdateEntityProgressActivity",
                new UpdateEntityProgressRequest
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    Phase = "Completed",
                    TotalEntities = discoverResult.TotalCount,
                    ProcessedEntities = result.ProcessedEntities,
                    SuccessfulEntities = result.SuccessfulEntities,
                    FailedEntities = result.FailedEntities,
                    CurrentBatch = totalBatches,
                    TotalBatches = totalBatches
                });

            // Step 7: Determine final result
            result.EndTime = context.CurrentUtcDateTime;
            result.Duration = result.EndTime.Value - result.StartTime;

            if (result.ProcessedEntities == 0)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"No {entityType} entities were processed";
            }
            else if (result.FailedEntities == 0)
            {
                result.IsSuccess = true;
                logger.LogInformation("Completed {EntityType} migration successfully for MigrationId: {MigrationId}. " +
                                    "Processed: {ProcessedCount} entities", 
                    entityType, migrationId, result.ProcessedEntities);
            }
            else if (result.SuccessfulEntities > 0)
            {
                result.IsSuccess = true; // Partial success
                result.ErrorMessage = $"Completed with {result.FailedEntities} failed entities out of {result.ProcessedEntities} total";
                logger.LogWarning("Completed {EntityType} migration with errors for MigrationId: {MigrationId}. " +
                                "Processed: {ProcessedCount}, Successful: {SuccessfulCount}, Failed: {FailedCount}", 
                    entityType, migrationId, result.ProcessedEntities, result.SuccessfulEntities, result.FailedEntities);
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"All {result.FailedEntities} {entityType} entities failed to migrate";
                logger.LogError("All {EntityType} entities failed for MigrationId: {MigrationId}. Failed: {FailedCount}", 
                    entityType, migrationId, result.FailedEntities);
            }

            return result;
        }
        catch (TaskCanceledException)
        {
            logger.LogInformation("{EntityType} migration was cancelled for MigrationId: {MigrationId}", 
                entityType, migrationId);
            result.IsSuccess = false;
            result.ErrorMessage = $"{entityType} migration was cancelled";
            result.EndTime = context.CurrentUtcDateTime;
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in {EntityType} migration for MigrationId: {MigrationId}", 
                entityType, migrationId);
            result.IsSuccess = false;
            result.ErrorMessage = $"Unexpected error: {ex.Message}";
            result.EndTime = context.CurrentUtcDateTime;
            return result;
        }
    }

    /// <summary>
    /// Calculates the number of batches needed for a given entity count and batch size
    /// </summary>
    /// <param name="totalCount">Total number of entities</param>
    /// <param name="batchSize">Number of entities per batch</param>
    /// <returns>Number of batches needed</returns>
    private static int CalculateBatchCount(int totalCount, int batchSize)
    {
        if (totalCount == 0) return 0;
        return (int)Math.Ceiling((double)totalCount / batchSize);
    }
}

/// <summary>
/// Request model for entity migration
/// </summary>
public class EntityMigrationRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public StoreConfiguration? SourceStore { get; set; }
    public StoreConfiguration? DestinationStore { get; set; }
    public CategoryTreeContext? CategoryTreeContext { get; set; }
    public MigrationSettings? Settings { get; set; }
}

/// <summary>
/// Result model for entity migration
/// </summary>
public class EntityMigrationResult
{
    public string EntityType { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public int ProcessedEntities { get; set; }
    public int SuccessfulEntities { get; set; }
    public int FailedEntities { get; set; }
    public List<BatchProcessingResult> BatchResults { get; set; } = new();
}

/// <summary>
/// Request model for checking rate limits
/// </summary>
public class CheckRateLimitRequest
{
    public string StoreId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
}

/// <summary>
/// Request model for updating entity progress
/// </summary>
public class UpdateEntityProgressRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public int TotalEntities { get; set; }
    public int ProcessedEntities { get; set; }
    public int SuccessfulEntities { get; set; }
    public int FailedEntities { get; set; }
    public int CurrentBatch { get; set; }
    public int TotalBatches { get; set; }
} 