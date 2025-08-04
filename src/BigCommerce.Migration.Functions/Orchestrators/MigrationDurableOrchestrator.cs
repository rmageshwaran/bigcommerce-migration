using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Extensions;
using BigCommerce.Migration.Orchestration.Services;
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
        var instanceId = $"{Environment.MachineName}-{context.InstanceId}-{context.CurrentUtcDateTime:yyyyMMddHHmmss}";
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

            // Step 3: 🚀 SKIP CATEGORY TREE RESOLUTION: Using mock context for brand/product-only migration
            logger.LogInformation("Step 3: Using mock category tree context for brand/product migration (MigrationId: {MigrationId})", migrationId);
            
            // Create a mock CategoryTreeContext since we're not migrating categories
            // Products use hard-coded category ID, brands don't need categories
            var mockCategoryTreeContext = new CategoryTreeContext
            {
                SourceChannelId = "1",
                DestinationChannelId = "1", 
                SourceCategoryTreeId = "1",
                DestinationCategoryTreeId = "1"
            };

            // Update the input context with mock tree IDs
            input.CategoryTreeContext = mockCategoryTreeContext;

            // Step 4: Check for cancellation before starting entity processing using deterministic pattern
            var cancellationState = context.GetOrInitializeCancellationState(migrationId);
            cancellationState = await context.CheckExternalCancellationOnceAsync(cancellationState);

            if (cancellationState.IsCancelled)
            {
                logger.LogInformation("Migration {MigrationId} was cancelled before entity processing began. Reason: {Reason}", 
                    migrationId, cancellationState.CancellationReason);
                
                return (MigrationOrchestrationResult)CancelledResultFactory.CreateCancelledMigrationResult(
                    cancellationState, context.CurrentUtcDateTime);
            }

            // Step 4.5: Phase 3.1 - Orchestrator collision detection
            logger.LogInformation("Step 4.5: Checking for orchestrator instance collisions for MigrationId: {MigrationId}", migrationId);
            var collisionResult = await context.CallActivityAsync<OrchestratorCollisionResult>(
                "OrchestratorCollisionDetectionActivity",
                new OrchestratorCollisionDetectionRequest
                {
                    MigrationId = migrationId,
                    InstanceId = instanceId
                });

            if (!collisionResult.CanProceed)
            {
                logger.LogWarning("Orchestrator collision detected for migration: {MigrationId}. Another instance is already running: {CurrentHolder}", 
                    migrationId, collisionResult.CurrentLockHolder?.InstanceId);
                
                // Return deterministic cancellation result for collision
                var collisionCancellationResult = await context.CallActivityAsync<object>(
                    "CreateCollisionCancellationResultActivity",
                    new CollisionCancellationRequest
                    {
                        MigrationId = migrationId,
                        InstanceId = instanceId,
                        CancellationReason = collisionResult.Message,
                        CurrentUtcDateTime = context.CurrentUtcDateTime
                    });
                
                return (MigrationOrchestrationResult)collisionCancellationResult;
            }

            logger.LogInformation("Successfully acquired orchestrator lock for migration: {MigrationId}, instance: {InstanceId}", 
                migrationId, instanceId);

            // Create cancellation token source for this execution based on external cancellation state
            // This allows activities to use fast token checks instead of external storage calls
            var cancellationTokenSource = new CancellationTokenSource();
            if (cancellationState.IsCancelled)
            {
                cancellationTokenSource.Cancel();
            }

            try
            {
                // Step 5: Process entities in dependency order
            var entityOrder = GetEntityDependencyOrder(input.MigrationRequest?.Entities ?? new List<string>());
            logger.LogInformation("Step 5: Processing {EntityCount} entity types in dependency order for MigrationId: {MigrationId}", 
                entityOrder.Count, migrationId);

            foreach (var entityType in entityOrder)
            {
                logger.LogInformation("Processing entity type: {EntityType} for MigrationId: {MigrationId}", 
                    entityType, migrationId);

                // Fast cancellation check using token (microseconds vs milliseconds for external storage)
                if (cancellationTokenSource.Token.IsCancellationRequested)
                {
                    logger.LogInformation("Migration {MigrationId} was cancelled during {EntityType} processing via fast token check", 
                        migrationId, entityType);
                    
                    return (MigrationOrchestrationResult)CancelledResultFactory.CreateCancelledMigrationResult(
                        cancellationState, context.CurrentUtcDateTime);
                }

                // Periodically refresh cancellation state from external storage for long-running migrations
                // Only check external storage every few entities to balance performance with responsiveness
                if (entityOrder.IndexOf(entityType) % 3 == 0) // Check every 3rd entity
                {
                    var refreshedState = await context.CheckExternalCancellationOnceAsync(cancellationState);
                    if (refreshedState.IsCancelled && !cancellationState.IsCancelled)
                    {
                        cancellationState = refreshedState;
                        cancellationTokenSource.Cancel();
                        logger.LogInformation("Migration {MigrationId} cancellation detected via periodic refresh during {EntityType} processing", 
                            migrationId, entityType);
                        
                        return (MigrationOrchestrationResult)CancelledResultFactory.CreateCancelledMigrationResult(
                            cancellationState, context.CurrentUtcDateTime);
                    }
                }

                // Process the entity type using the entity-specific orchestrator
                var entityRequest = new EntityMigrationRequest
                {
                    MigrationId = migrationId,
                    EntityType = entityType,
                    SourceStore = input.MigrationRequest?.SourceStore ?? new StoreConfiguration(),
                    DestinationStore = input.MigrationRequest?.DestinationStore ?? new StoreConfiguration(),
                    CategoryTreeContext = input.CategoryTreeContext ?? new CategoryTreeContext(),
                    Settings = input.MigrationRequest?.Settings,
                    // Pass cancellation state for fast token-based checking in sub-orchestrator and activities
                    IsCancelled = cancellationState.IsCancelled,
                    CancellationReason = cancellationState.CancellationReason,
                    CancelledAt = cancellationState.CancelledAt
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

            // ✅ CANCELLATION DETECTION: Check if any entity migration was cancelled
            var cancelledEntities = result.EntityResults.Values
                .Where(r => !r.IsSuccess && (r.ErrorMessage?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true ||
                                           r.Errors.Any(e => e.Contains("cancelled", StringComparison.OrdinalIgnoreCase))))
                .ToList();

            if (cancelledEntities.Any())
            {
                result.Status = "Cancelled";
                var cancelledEntityTypes = string.Join(", ", cancelledEntities.Select(r => r.EntityType));
                result.ErrorMessage = $"Migration was cancelled. Cancelled entities: {cancelledEntityTypes}";
                
                logger.LogWarning("Migration was cancelled for MigrationId: {MigrationId}. " +
                                "Cancelled entities: {CancelledEntities}, Processed: {ProcessedCount}, Successful: {SuccessfulCount}", 
                    migrationId, cancelledEntityTypes, totalProcessed, totalSuccessful);
                
                // Complete migration as cancelled - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Cancelled, result.ErrorMessage);
                
                return result;
            }

            // Determine final status for non-cancelled migrations
            if (totalProcessed == 0)
            {
                result.Status = "Failed";
                result.ErrorMessage = "No entities were processed";
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Failed, result.ErrorMessage);
                
                // Migration failed event will be handled by queue-based system
            }
            else if (totalFailed == 0)
            {
                result.Status = "Completed";
                logger.LogInformation("Migration completed successfully for MigrationId: {MigrationId}. " +
                                    "Processed: {ProcessedCount}, Successful: {SuccessfulCount}", 
                    migrationId, totalProcessed, totalSuccessful);
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Completed);
                
                // Migration completed event will be handled by queue-based system
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
                
                // Migration completed with errors event will be handled by queue-based system
            }
            else if (totalSuccessful == 0 && totalFailed > 0)
            {
                result.Status = "Failed";
                result.ErrorMessage = $"All {totalFailed} entities failed to migrate";
                logger.LogError("Migration failed - all entities failed for MigrationId: {MigrationId}. " +
                              "Failed: {FailedCount}", migrationId, totalFailed);
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Failed, result.ErrorMessage);
                
                // Migration failed event will be handled by queue-based system
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
            finally
            {
                // Phase 3.1: Always release the orchestrator lock when migration completes, fails, or is cancelled
                try
                {
                    logger.LogInformation("Releasing orchestrator lock for migration: {MigrationId}, instance: {InstanceId}", 
                        migrationId, instanceId);
                    
                    var lockReleased = await context.CallActivityAsync<bool>(
                        "ReleaseOrchestratorLockActivity",
                        new OrchestratorLockReleaseRequest
                        {
                            MigrationId = migrationId,
                            InstanceId = instanceId
                        });
                    if (lockReleased)
                    {
                        logger.LogInformation("Successfully released orchestrator lock for migration: {MigrationId}", migrationId);
                    }
                    else
                    {
                        logger.LogWarning("Failed to release orchestrator lock for migration: {MigrationId} - may require cleanup", migrationId);
                    }
                }
                catch (Exception lockEx)
                {
                    logger.LogError(lockEx, "Error releasing orchestrator lock for migration: {MigrationId} - may require cleanup", migrationId);
                }
            }
        }
        catch (TaskCanceledException)
        {
            logger.LogInformation("Migration orchestration was cancelled for MigrationId: {MigrationId}", migrationId);
            result.Status = "Cancelled";
            result.ErrorMessage = "Migration orchestration was cancelled";
            result.EndTime = context.CurrentUtcDateTime;
            
            // Migration cancelled event will be handled by queue-based system
            
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
