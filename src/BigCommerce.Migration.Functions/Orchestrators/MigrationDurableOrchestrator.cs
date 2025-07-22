using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Activities;
using System.Linq;

namespace BigCommerce.Migration.Functions.Orchestrators;

/// <summary>
/// Main migration workflow orchestrator that coordinates the complete BigCommerce migration process
/// Manages entity dependencies, progress tracking, and cancellation support
/// </summary>
public static class MigrationDurableOrchestrator
{
    /// <summary>
    /// Main migration orchestrator function that processes migrations in the correct entity dependency order
    /// </summary>
    [Function("MigrationDurableOrchestrator")]
    public static async Task<MigrationOrchestrationResult> RunMigrationOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger("MigrationDurableOrchestrator");
        var input = context.GetInput<MigrationOrchestrationRequest>();
        
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input), "Migration orchestration request is required");
        }

        var migrationId = input.MigrationId;
        var result = new MigrationOrchestrationResult
        {
            MigrationId = migrationId,
            StartTime = context.CurrentUtcDateTime,
            Status = "InProgress",
            EntityResults = new Dictionary<string, EntityMigrationResult>()
        };

        try
        {
            logger.LogInformation("Starting migration orchestration for MigrationId: {MigrationId}", migrationId);

            // Broadcast migration started event
            await context.CallActivityAsync<bool>(
                "BroadcastMigrationStarted",
                new
                {
                    MigrationId = migrationId,
                    Data = new
                    {
                        Status = "Started",
                        SourceStore = input.MigrationRequest?.SourceStore?.StoreId ?? string.Empty,
                        DestinationStore = input.MigrationRequest?.DestinationStore?.StoreId ?? string.Empty,
                        RequestedEntities = input.MigrationRequest?.Entities ?? new List<string>(),
                        StartTime = context.CurrentUtcDateTime,
                        TotalEntityTypes = input.MigrationRequest?.Entities?.Count ?? 0
                    }
                });

            // Step 1: Initialize Migration
            logger.LogInformation("Step 1: Initializing migration for MigrationId: {MigrationId}", migrationId);
            var initializeResult = await context.CallActivityAsync<InitializeMigrationResult>(
                "InitializeMigration", 
                new InitializeMigrationRequest 
                { 
                    MigrationRequest = input.MigrationRequest ?? new MigrationRequest(),
                    CategoryTreeContext = input.CategoryTreeContext
                });

            if (!initializeResult.IsSuccess)
            {
                result.Status = "Failed";
                result.ErrorMessage = $"Migration initialization failed: {initializeResult.ErrorMessage}";
                result.EndTime = context.CurrentUtcDateTime;
                return result;
            }

            // Step 2: Validate Migration Stores
            logger.LogInformation("Step 2: Validating migration stores for MigrationId: {MigrationId}", migrationId);
            var validateResult = await context.CallActivityAsync<ValidateStoresResult>(
                "ValidateMigrationStores",
                new ValidateStoresRequest
                {
                    SourceStore = input.MigrationRequest?.SourceStore ?? new StoreConfiguration(),
                    DestinationStore = input.MigrationRequest?.DestinationStore ?? new StoreConfiguration()
                });

            if (!validateResult.IsValid)
            {
                result.Status = "Failed";
                result.ErrorMessage = $"Store validation failed: {validateResult.ErrorMessage}";
                result.EndTime = context.CurrentUtcDateTime;
                return result;
            }

            // Step 3: Resolve Category Tree IDs
            logger.LogInformation("Step 3: Resolving category tree IDs for MigrationId: {MigrationId}", migrationId);
            var resolvedCategoryTreeContext = await context.CallActivityAsync<CategoryTreeContext>(
                "ResolveCategoryTreeIds",
                new ResolveCategoryTreeIdsRequest
                {
                    MigrationId = migrationId,
                    SourceStore = input.MigrationRequest?.SourceStore ?? new StoreConfiguration(),
                    DestinationStore = input.MigrationRequest?.DestinationStore ?? new StoreConfiguration(),
                    CategoryTreeContext = input.CategoryTreeContext
                });

            // Update the input context with resolved tree IDs
            input.CategoryTreeContext = resolvedCategoryTreeContext;

            // Step 4: Check for cancellation before starting entity processing
            var isCancelled = await context.CallActivityAsync<bool>(
                "CheckMigrationCancellation",
                migrationId);

            if (isCancelled)
            {
                result.Status = "Cancelled";
                result.ErrorMessage = "Migration was cancelled before entity processing began";
                result.EndTime = context.CurrentUtcDateTime;
                return result;
            }

            // Step 5: Process entities in dependency order
            var entityOrder = GetEntityDependencyOrder(input.MigrationRequest?.Entities ?? new List<string>());
            logger.LogInformation("Step 5: Processing {EntityCount} entity types in dependency order for MigrationId: {MigrationId}", 
                entityOrder.Count, migrationId);

            foreach (var entityType in entityOrder)
            {
                logger.LogInformation("Processing entity type: {EntityType} for MigrationId: {MigrationId}", 
                    entityType, migrationId);

                // Broadcast entity phase started event
                await context.CallActivityAsync<bool>(
                    "BroadcastEntityPhaseStarted",
                    new
                    {
                        MigrationId = migrationId,
                        EntityType = entityType,
                        Data = new
                        {
                            Status = "EntityStarted",
                            EntityType = entityType,
                            StartTime = context.CurrentUtcDateTime,
                            OverallProgress = new
                            {
                                CompletedEntityTypes = result.EntityResults.Count,
                                TotalEntityTypes = entityOrder.Count,
                                PercentComplete = (double)result.EntityResults.Count / entityOrder.Count * 100
                            }
                        }
                    });

                // Check for cancellation before each entity
                var isEntityCancelled = await context.CallActivityAsync<bool>(
                    "CheckMigrationCancellation",
                    migrationId);

                if (isEntityCancelled)
                {
                    result.Status = "Cancelled";
                    result.ErrorMessage = $"Migration was cancelled during {entityType} processing";
                    result.EndTime = context.CurrentUtcDateTime;
                    return result;
                }

                // Process the entity type using the entity-specific orchestrator
                var entityRequest = new EntityMigrationRequest
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    SourceStore = input.MigrationRequest?.SourceStore ?? new StoreConfiguration(),
                    DestinationStore = input.MigrationRequest?.DestinationStore ?? new StoreConfiguration(),
                    CategoryTreeContext = input.CategoryTreeContext ?? new CategoryTreeContext(),
                    Settings = input.MigrationRequest?.Settings
                };

                try
                {
                    var entityResult = await context.CallSubOrchestratorAsync<EntityMigrationResult>(
                        "EntityMigrationDurableOrchestrator",
                        entityRequest);

                    result.EntityResults[entityType] = entityResult;

                    if (!entityResult.IsSuccess)
                    {
                        logger.LogWarning("Entity {EntityType} migration completed with errors for MigrationId: {MigrationId}. " +
                                        "Success: {SuccessCount}, Failed: {FailureCount}", 
                            entityType, migrationId, entityResult.SuccessfulEntities, entityResult.FailedEntities);
                    }
                    else
                    {
                        logger.LogInformation("Entity {EntityType} migration completed successfully for MigrationId: {MigrationId}. " +
                                            "Processed: {ProcessedCount} entities", 
                            entityType, migrationId, entityResult.ProcessedEntities);
                    }

                    // Broadcast entity phase completed event
                    await context.CallActivityAsync<bool>(
                        "BroadcastEntityPhaseCompleted",
                        new
                        {
                            MigrationId = migrationId,
                            EntityType = entityType,
                            Data = new
                            {
                                Status = "EntityCompleted",
                                EntityType = entityType,
                                EndTime = context.CurrentUtcDateTime,
                                Results = new
                                {
                                    ProcessedEntities = entityResult.ProcessedEntities,
                                    SuccessfulEntities = entityResult.SuccessfulEntities,
                                    FailedEntities = entityResult.FailedEntities,
                                    IsSuccess = entityResult.IsSuccess,
                                    ProcessingTime = entityResult.EndTime - entityResult.StartTime
                                },
                                OverallProgress = new
                                {
                                    CompletedEntityTypes = result.EntityResults.Count,
                                    TotalEntityTypes = entityOrder.Count,
                                    PercentComplete = (double)result.EntityResults.Count / entityOrder.Count * 100
                                }
                            }
                        });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to process entity {EntityType} for MigrationId: {MigrationId}", 
                        entityType, migrationId);

                    result.EntityResults[entityType] = new EntityMigrationResult
                    {
                        EntityType = entityType,
                        IsSuccess = false,
                        ErrorMessage = ex.Message,
                        ProcessedEntities = 0,
                        SuccessfulEntities = 0,
                        FailedEntities = 0,
                        StartTime = context.CurrentUtcDateTime,
                        EndTime = context.CurrentUtcDateTime
                    };

                    // Continue processing other entities (continue-on-failure pattern)
                    logger.LogInformation("Continuing with next entity type after {EntityType} failure for MigrationId: {MigrationId}", 
                        entityType, migrationId);
                }
            }

            // Step 6: Calculate final results
            var totalProcessed = result.EntityResults.Values.Sum(r => r.ProcessedEntities);
            var totalSuccessful = result.EntityResults.Values.Sum(r => r.SuccessfulEntities);
            var totalFailed = result.EntityResults.Values.Sum(r => r.FailedEntities);

            result.TotalEntitiesProcessed = totalProcessed;
            result.TotalEntitiesSuccessful = totalSuccessful;
            result.TotalEntitiesFailed = totalFailed;
            result.EndTime = context.CurrentUtcDateTime;
            result.Duration = result.EndTime.Value - result.StartTime;

            // Determine final status
            if (totalProcessed == 0)
            {
                result.Status = "Failed";
                result.ErrorMessage = "No entities were processed";
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Failed, result.ErrorMessage);
                
                // Broadcast migration failed event
                await BroadcastMigrationEventAsync(context, migrationId, result, "BroadcastMigrationFailed", "Failed", totalProcessed, totalSuccessful, totalFailed, result.ErrorMessage);
            }
            else if (totalFailed == 0)
            {
                result.Status = "Completed";
                logger.LogInformation("Migration completed successfully for MigrationId: {MigrationId}. " +
                                    "Processed: {ProcessedCount}, Successful: {SuccessfulCount}", 
                    migrationId, totalProcessed, totalSuccessful);
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Completed);
                
                // Broadcast migration completed event
                await BroadcastMigrationEventAsync(context, migrationId, result, "BroadcastMigrationCompleted", "Completed", totalProcessed, totalSuccessful, totalFailed);
            }
            else if (totalSuccessful > 0)
            {
                result.Status = "CompletedWithErrors";
                result.ErrorMessage = $"Migration completed with {totalFailed} failed entities out of {totalProcessed} total";
                logger.LogWarning("Migration completed with errors for MigrationId: {MigrationId}. " +
                                "Processed: {ProcessedCount}, Successful: {SuccessfulCount}, Failed: {FailedCount}", 
                    migrationId, totalProcessed, totalSuccessful, totalFailed);
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Completed, result.ErrorMessage);
                
                // Broadcast migration completed with errors event
                await BroadcastMigrationEventAsync(context, migrationId, result, "BroadcastMigrationCompleted", "CompletedWithErrors", totalProcessed, totalSuccessful, totalFailed, result.ErrorMessage);
            }
            else if (totalSuccessful == 0 && totalFailed > 0)
            {
                result.Status = "Failed";
                result.ErrorMessage = $"All {totalFailed} entities failed to migrate";
                logger.LogError("Migration failed - all entities failed for MigrationId: {MigrationId}. " +
                              "Failed: {FailedCount}", migrationId, totalFailed);
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Failed, result.ErrorMessage);
                
                // Broadcast migration failed event
                await BroadcastMigrationEventAsync(context, migrationId, result, "BroadcastMigrationFailed", "Failed", totalProcessed, totalSuccessful, totalFailed, result.ErrorMessage);
            }
            else
            {
                // Fallback case - should not normally reach here, but handle gracefully
                result.Status = "CompletedWithErrors";
                result.ErrorMessage = $"Migration completed with unexpected status: {totalProcessed} processed, {totalSuccessful} successful, {totalFailed} failed";
                logger.LogWarning("Migration completed with unexpected status for MigrationId: {MigrationId}. " +
                                "Processed: {ProcessedCount}, Successful: {SuccessfulCount}, Failed: {FailedCount}", 
                    migrationId, totalProcessed, totalSuccessful, totalFailed);
            }

            return result;
        }
        catch (TaskCanceledException)
        {
            logger.LogInformation("Migration orchestration was cancelled for MigrationId: {MigrationId}", migrationId);
            result.Status = "Cancelled";
            result.ErrorMessage = "Migration orchestration was cancelled";
            result.EndTime = context.CurrentUtcDateTime;
            
            // Broadcast migration cancelled event
            await BroadcastMigrationEventAsync(context, migrationId, result, "BroadcastMigrationCancelled", "Cancelled", result.TotalEntitiesProcessed, result.TotalEntitiesSuccessful, result.TotalEntitiesFailed, result.ErrorMessage);
            
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in migration orchestration for MigrationId: {MigrationId}", migrationId);
            result.Status = "Failed";
            result.ErrorMessage = $"Unexpected error: {ex.Message}";
            result.EndTime = context.CurrentUtcDateTime;
            return result;
        }
    }

    /// <summary>
    /// Gets the entity processing order based on dependencies
    /// Categories must be processed before products, variants depend on products, etc.
    /// </summary>
    /// <summary>
    /// Helper method to create and call CompleteMigration with consistent MigrationResult structure
    /// </summary>
    private static async Task CompleteMigrationAsync(
        TaskOrchestrationContext context,
        string migrationId,
        MigrationOrchestrationResult result,
        MigrationStatus finalStatus,
        string? errorMessage = null)
    {
        var errors = new List<string>();
        if (!string.IsNullOrEmpty(errorMessage))
        {
            errors.Add(errorMessage);
        }

        await context.CallActivityAsync("CompleteMigration", new
        {
            MigrationId = migrationId,
            Result = new MigrationResult
            {
                MigrationId = migrationId,
                Status = finalStatus,
                StartTime = result.StartTime,
                EndTime = result.EndTime ?? context.CurrentUtcDateTime,
                Duration = result.Duration,
                EntityResults = result.EntityResults,
                Errors = errors,
                Statistics = new MigrationStatistics
                {
                    TotalDuration = result.Duration,
                    TotalApiCalls = 0,
                    SuccessfulApiCalls = 0,
                    FailedApiCalls = 0,
                    AverageResponseTimeMs = 0,
                    PeakApiCallsPerMinute = 0,
                    TotalBytesTransferred = 0
                }
            }
        });
    }

    /// <summary>
    /// Helper method to broadcast migration status events with consistent data structure
    /// </summary>
    private static async Task BroadcastMigrationEventAsync(
        TaskOrchestrationContext context,
        string migrationId,
        MigrationOrchestrationResult result,
        string activityName,
        string status,
        int totalProcessed,
        int totalSuccessful,
        int totalFailed,
        string? errorMessage = null)
    {
        var data = new
        {
            Status = status,
            EndTime = result.EndTime,
            Duration = result.Duration,
            TotalEntitiesProcessed = totalProcessed,
            TotalEntitiesSuccessful = totalSuccessful,
            TotalEntitiesFailed = totalFailed,
            EntityResults = result.EntityResults
        };

        // Add error message if provided
        var broadcastData = errorMessage != null 
            ? new { data.Status, data.EndTime, data.Duration, data.TotalEntitiesProcessed, data.TotalEntitiesSuccessful, data.TotalEntitiesFailed, data.EntityResults, ErrorMessage = errorMessage }
            : (object)data;

        await context.CallActivityAsync<bool>(activityName, new
        {
            MigrationId = migrationId,
            Data = broadcastData
        });
    }

    private static List<string> GetEntityDependencyOrder(IEnumerable<string> requestedEntities)
    {
        // Define the complete dependency order
        var fullDependencyOrder = new[]
        {
            "categories",  // Must be first - referenced by products
            "brands",      // Must be before products - referenced by products  
            "products",    // Must be before variants and images
            "variants",    // Depends on products
            "images",      // Can reference products and variants
            "modifiers"    // Product modifiers/options
        };

        // Filter to only include requested entities while maintaining order
        var requestedSet = new HashSet<string>(requestedEntities, StringComparer.OrdinalIgnoreCase);
        
        return fullDependencyOrder
            .Where(entity => requestedSet.Contains(entity))
            .ToList();
    }
}

/// <summary>
/// Request model for migration orchestration
/// </summary>
public class MigrationOrchestrationRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public MigrationRequest MigrationRequest { get; set; } = new();
    public CategoryTreeContext? CategoryTreeContext { get; set; }
}

/// <summary>
/// Result model for migration orchestration
/// </summary>
public class MigrationOrchestrationResult
{
    public string MigrationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public int TotalEntitiesProcessed { get; set; }
    public int TotalEntitiesSuccessful { get; set; }
    public int TotalEntitiesFailed { get; set; }
    public Dictionary<string, EntityMigrationResult> EntityResults { get; set; } = new();
} 
