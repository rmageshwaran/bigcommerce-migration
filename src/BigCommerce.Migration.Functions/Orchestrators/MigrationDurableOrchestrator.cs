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
            var cancellationCheck = await context.CallActivityAsync<CheckCancellationResult>(
                "CheckMigrationCancellation",
                migrationId);

            if (cancellationCheck.IsCancelled)
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
                var entityCancellationCheck = await context.CallActivityAsync<CheckCancellationResult>(
                    "CheckMigrationCancellation",
                    migrationId);

                if (entityCancellationCheck.IsCancelled)
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
                
                // Broadcast migration failed event
                await context.CallActivityAsync<bool>(
                    "BroadcastMigrationFailed",
                    new
                    {
                        MigrationId = migrationId,
                        Data = new
                        {
                            Status = "Failed",
                            ErrorMessage = result.ErrorMessage,
                            EndTime = result.EndTime,
                            Duration = result.Duration,
                            TotalEntitiesProcessed = totalProcessed,
                            TotalEntitiesSuccessful = totalSuccessful,
                            TotalEntitiesFailed = totalFailed
                        }
                    });
            }
            else if (totalFailed == 0)
            {
                result.Status = "Completed";
                logger.LogInformation("Migration completed successfully for MigrationId: {MigrationId}. " +
                                    "Processed: {ProcessedCount}, Successful: {SuccessfulCount}", 
                    migrationId, totalProcessed, totalSuccessful);
                
                // Complete migration - update storage service for HTTP API
                await context.CallActivityAsync("CompleteMigration", new
                {
                    MigrationId = migrationId,
                    Result = new MigrationResult
                    {
                        MigrationId = migrationId,
                        Status = MigrationStatus.Completed,
                        StartTime = result.StartTime,
                        EndTime = result.EndTime ?? context.CurrentUtcDateTime,
                        Duration = result.Duration,
                        EntityResults = result.EntityResults,
                        Errors = new List<string>(),
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
                
                // Broadcast migration completed event
                await context.CallActivityAsync<bool>(
                    "BroadcastMigrationCompleted",
                    new
                    {
                        MigrationId = migrationId,
                        Data = new
                        {
                            Status = "Completed",
                            EndTime = result.EndTime,
                            Duration = result.Duration,
                            TotalEntitiesProcessed = totalProcessed,
                            TotalEntitiesSuccessful = totalSuccessful,
                            TotalEntitiesFailed = totalFailed,
                            EntityResults = result.EntityResults
                        }
                    });
            }
            else if (totalSuccessful > 0)
            {
                result.Status = "CompletedWithErrors";
                result.ErrorMessage = $"Migration completed with {totalFailed} failed entities out of {totalProcessed} total";
                logger.LogWarning("Migration completed with errors for MigrationId: {MigrationId}. " +
                                "Processed: {ProcessedCount}, Successful: {SuccessfulCount}, Failed: {FailedCount}", 
                    migrationId, totalProcessed, totalSuccessful, totalFailed);
                
                // Complete migration - update storage service for HTTP API
                await context.CallActivityAsync("CompleteMigration", new
                {
                    MigrationId = migrationId,
                    Result = new MigrationResult
                    {
                        MigrationId = migrationId,
                        Status = MigrationStatus.Completed,
                        StartTime = result.StartTime,
                        EndTime = result.EndTime ?? context.CurrentUtcDateTime,
                        Duration = result.Duration,
                        EntityResults = result.EntityResults,
                        Errors = new List<string> { result.ErrorMessage },
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
                
                // Broadcast migration completed with errors event
                await context.CallActivityAsync<bool>(
                    "BroadcastMigrationCompleted",
                    new
                    {
                        MigrationId = migrationId,
                        Data = new
                        {
                            Status = "CompletedWithErrors",
                            ErrorMessage = result.ErrorMessage,
                            EndTime = result.EndTime,
                            Duration = result.Duration,
                            TotalEntitiesProcessed = totalProcessed,
                            TotalEntitiesSuccessful = totalSuccessful,
                            TotalEntitiesFailed = totalFailed,
                            EntityResults = result.EntityResults
                        }
                    });
            }
            else
            {
                result.Status = "Failed";
                result.ErrorMessage = $"All {totalFailed} entities failed to migrate";
                logger.LogError("Migration failed - all entities failed for MigrationId: {MigrationId}. " +
                              "Failed: {FailedCount}", migrationId, totalFailed);
                
                // Broadcast migration failed event
                await context.CallActivityAsync<bool>(
                    "BroadcastMigrationFailed",
                    new
                    {
                        MigrationId = migrationId,
                        Data = new
                        {
                            Status = "Failed",
                            ErrorMessage = result.ErrorMessage,
                            EndTime = result.EndTime,
                            Duration = result.Duration,
                            TotalEntitiesProcessed = totalProcessed,
                            TotalEntitiesSuccessful = totalSuccessful,
                            TotalEntitiesFailed = totalFailed,
                            EntityResults = result.EntityResults
                        }
                    });
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
            await context.CallActivityAsync<bool>(
                "BroadcastMigrationCancelled",
                new
                {
                    MigrationId = migrationId,
                    Data = new
                    {
                        Status = "Cancelled",
                        ErrorMessage = result.ErrorMessage,
                        EndTime = result.EndTime,
                        Duration = result.EndTime.Value - result.StartTime,
                        TotalEntitiesProcessed = result.TotalEntitiesProcessed,
                        TotalEntitiesSuccessful = result.TotalEntitiesSuccessful,
                        TotalEntitiesFailed = result.TotalEntitiesFailed
                    }
                });
            
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
