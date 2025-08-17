using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Activities.Models;
using BigCommerce.Migration.Activities.Activities;
using BigCommerce.Migration.Activities.Extensions;
using BigCommerce.Migration.Activities.Services;
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
            var cancellationResult = await context.CallActivityAsync<(bool IsCancelled, string Reason)>("CheckCancellationFlag", migrationId);
            var isCancelled = cancellationResult.IsCancelled;
            if (isCancelled)
            {
                logger.LogInformation("Migration {MigrationId} was cancelled before entity processing began. Reason: {Reason}", 
                    migrationId, cancellationResult.Reason);
                
                return new MigrationOrchestrationResult
                {
                    MigrationId = migrationId,
                    Status = "Cancelled",
                    EndTime = context.CurrentUtcDateTime,
                    ErrorMessage = $"Migration cancelled before entity processing began: {cancellationResult.Reason}"
                };
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
                
                // Phase 4.1: Enhanced collision cancellation with native cancellation integration
                logger.LogInformation("Publishing enhanced collision cancellation via native cancellation system for migration: {MigrationId}", migrationId);
                await context.CallActivityAsync(
                    "PublishCollisionCancellation",
                    new CollisionCancellationNotificationRequest
                    {
                        MigrationId = migrationId,
                        InstanceId = instanceId,
                        Reason = collisionResult.Message ?? "Another orchestrator instance is already running",
                        CancelledAt = context.CurrentUtcDateTime
                    });
                
                // Phase 4.1: Return standardized result (consistent with enhanced cancellation)
                logger.LogInformation("Creating collision cancellation result for migration: {MigrationId}", migrationId);
                return new MigrationOrchestrationResult
                {
                    MigrationId = migrationId,
                    Status = "Cancelled",
                    ErrorMessage = $"Orchestrator collision detected: {collisionResult.Message}",
                    StartTime = context.CurrentUtcDateTime,
                    EndTime = context.CurrentUtcDateTime,
                    Duration = TimeSpan.Zero,
                    TotalEntitiesProcessed = 0,
                    TotalEntitiesSuccessful = 0,
                    TotalEntitiesFailed = 0,
                    EntityResults = new Dictionary<string, EntityMigrationResult>()
                };
            }

            logger.LogInformation("Successfully acquired orchestrator lock for migration: {MigrationId}, instance: {InstanceId}", 
                migrationId, instanceId);

            // Simplified cancellation handling - we'll check via activities

            try
            {
                // Step 5: Resolve entity dependencies using EntityDependencyResolver
            var entityOrder = await context.CallActivityAsync<List<string>>(
                "ResolveEntityDependencies", 
                input.MigrationRequest?.Entities ?? new List<string>());
            logger.LogInformation("Step 5: Processing {EntityCount} entity types in dependency order for MigrationId: {MigrationId}", 
                entityOrder.Count, migrationId);

            // Step 5.1: Setup external event listening for cancellation
            var cancellationEvent = context.WaitForExternalEvent<string>("CancellationRequested");
            logger.LogInformation("Step 5.1: External cancellation event listener activated for MigrationId: {MigrationId}", migrationId);

            // Step 5.2: Initialize deterministic cancellation state
            var cancellationState = new { 
                IsCancelled = false, 
                CancellationReason = string.Empty, 
                CancellationSource = string.Empty,
                CancelledAt = (DateTime?)null 
            };
            logger.LogInformation("Step 5.2: Deterministic cancellation state initialized for MigrationId: {MigrationId}", migrationId);

            foreach (var entityType in entityOrder)
            {
                logger.LogInformation("Processing entity type: {EntityType} for MigrationId: {MigrationId}", 
                    entityType, migrationId);

                // Check for cancellation before processing each entity (flag + external event)
                if (!cancellationState.IsCancelled)
                {
                    var entityCancellationResult = await context.CallActivityAsync<(bool IsCancelled, string? Reason)>("CheckCancellationFlag", migrationId);
                    bool externalCancellationReceived = cancellationEvent.IsCompleted && cancellationEvent.IsCompletedSuccessfully;
                    
                    if (entityCancellationResult.IsCancelled || externalCancellationReceived)
                    {
                        // Update deterministic cancellation state
                        cancellationState = new {
                            IsCancelled = true,
                            CancellationReason = externalCancellationReceived ? 
                                (cancellationEvent.Result ?? "External cancellation requested") : 
                                (entityCancellationResult.Reason ?? "No reason provided"),
                            CancellationSource = externalCancellationReceived ? "ExternalEvent" : "CancellationFlag",
                            CancelledAt = (DateTime?)context.CurrentUtcDateTime
                        };
                    }
                }

                if (cancellationState.IsCancelled)
                {
                    logger.LogInformation("Migration {MigrationId} was cancelled during {EntityType} processing. Source: {Source}, Reason: {Reason}", 
                        migrationId, entityType, cancellationState.CancellationSource, cancellationState.CancellationReason);
                    
                    return new MigrationOrchestrationResult
                    {
                        MigrationId = migrationId,
                        Status = "Cancelled",
                        EndTime = cancellationState.CancelledAt ?? context.CurrentUtcDateTime,
                        ErrorMessage = $"Migration cancelled during {entityType} processing ({cancellationState.CancellationSource}): {cancellationState.CancellationReason}"
                    };
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
                    // 🚫 CANCELLATION FIX: Propagate cancellation state to child orchestrators
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
                        // 🚫 CANCELLATION FIX: Check if entity failure was due to cancellation
                        bool wasEntityCancelled = entityResult.ErrorMessage?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true ||
                                                 entityResult.Errors?.Any(e => e.Contains("cancelled", StringComparison.OrdinalIgnoreCase)) == true;
                        
                        if (wasEntityCancelled)
                        {
                            logger.LogInformation("🚫 Entity {EntityType} migration was cancelled for MigrationId: {MigrationId}. " +
                                                "Updating cancellation state to stop dependent phases.", 
                                entityType, migrationId);
                            
                            // Update cancellation state to stop subsequent entity phases
                            cancellationState = new {
                                IsCancelled = true,
                                CancellationReason = entityResult.ErrorMessage ?? "Entity migration was cancelled",
                                CancellationSource = "EntityCancellation",
                                CancelledAt = (DateTime?)context.CurrentUtcDateTime
                            };
                        }
                        else
                        {
                            logger.LogWarning("Entity {EntityType} migration completed with errors for MigrationId: {MigrationId}. " +
                                            "Success: {SuccessCount}, Failed: {FailureCount}", 
                                entityType, migrationId, entityResult.SuccessfulEntities, entityResult.FailedEntities);
                        }
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
                    // 🚫 ENTITY ORCHESTRATOR CANCELLATION FIX: Distinguish between cancellation and actual failures
                    bool isCancellationError = ex.Message.Contains("Migration cancelled", StringComparison.OrdinalIgnoreCase) ||
                                             ex.Message.Contains("User requested cancellation", StringComparison.OrdinalIgnoreCase) ||
                                             ex is OperationCanceledException;
                    
                    if (isCancellationError)
                    {
                        logger.LogInformation("🚫 Entity {EntityType} processing was cancelled for MigrationId: {MigrationId}: {ErrorMessage}", 
                            entityType, migrationId, ex.Message);

                        result.EntityResults[entityType] = new EntityMigrationResult
                        {
                            EntityType = entityType,
                            IsSuccess = false,
                            ErrorMessage = $"{entityType} migration was cancelled: {ex.Message}",
                            ProcessedEntities = 0,
                            SuccessfulEntities = 0,
                            FailedEntities = 0,  // 🚫 FIX: Don't mark cancelled entities as failed
                            StartTime = context.CurrentUtcDateTime,
                            EndTime = context.CurrentUtcDateTime
                        };

                        // Update cancellation state to stop subsequent entity phases
                        cancellationState = new {
                            IsCancelled = true,
                            CancellationReason = ex.Message,
                            CancellationSource = "EntityOrchestrationCancellation",
                            CancelledAt = (DateTime?)context.CurrentUtcDateTime
                        };
                        
                        logger.LogInformation("🚫 Cancellation detected during {EntityType}, stopping further entity processing for MigrationId: {MigrationId}", 
                            entityType, migrationId);
                        break; // Stop processing further entities
                    }
                    else
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
                            FailedEntities = 0,  // Let EntityMigrationOrchestrator handle the actual counts
                            StartTime = context.CurrentUtcDateTime,
                            EndTime = context.CurrentUtcDateTime
                        };

                        // Continue processing other entities (continue-on-failure pattern)
                        logger.LogInformation("Continuing with next entity type after {EntityType} failure for MigrationId: {MigrationId}", 
                            entityType, migrationId);
                    }
                }
            }

            // Step 6: Calculate final results with enhanced cancellation detection
            var totalProcessed = result.EntityResults.Values.Sum(r => r.ProcessedEntities);
            var totalSuccessful = result.EntityResults.Values.Sum(r => r.SuccessfulEntities);
            var totalFailed = result.EntityResults.Values.Sum(r => r.FailedEntities);

            result.TotalEntitiesProcessed = totalProcessed;
            result.TotalEntitiesSuccessful = totalSuccessful;
            result.TotalEntitiesFailed = totalFailed;
            result.EndTime = context.CurrentUtcDateTime;
            result.Duration = result.EndTime.Value - result.StartTime;

            // ✅ ENHANCED CANCELLATION DETECTION: Check both state and entity results
            bool wasCancelledByState = cancellationState.IsCancelled;
            var cancelledEntities = result.EntityResults.Values
                .Where(r => !r.IsSuccess && (r.ErrorMessage?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true ||
                                           r.Errors.Any(e => e.Contains("cancelled", StringComparison.OrdinalIgnoreCase))))
                .ToList();

            if (wasCancelledByState || cancelledEntities.Any())
            {
                result.Status = "Cancelled";
                
                // Prioritize deterministic state information if available
                if (wasCancelledByState)
                {
                    result.ErrorMessage = $"Migration was cancelled ({cancellationState.CancellationSource}): {cancellationState.CancellationReason}";
                    result.EndTime = cancellationState.CancelledAt ?? result.EndTime;
                    
                    logger.LogWarning("Migration was cancelled for MigrationId: {MigrationId} via {Source}. " +
                                    "Reason: {Reason}, Processed: {ProcessedCount}, Successful: {SuccessfulCount}", 
                        migrationId, cancellationState.CancellationSource, cancellationState.CancellationReason, totalProcessed, totalSuccessful);
                }
                else
                {
                    var cancelledEntityTypes = string.Join(", ", cancelledEntities.Select(r => r.EntityType));
                    result.ErrorMessage = $"Migration was cancelled. Cancelled entities: {cancelledEntityTypes}";
                    
                    logger.LogWarning("Migration was cancelled for MigrationId: {MigrationId}. " +
                                    "Cancelled entities: {CancelledEntities}, Processed: {ProcessedCount}, Successful: {SuccessfulCount}", 
                        migrationId, cancelledEntityTypes, totalProcessed, totalSuccessful);
                }
                
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
                Statistics = new BigCommerce.Migration.Activities.Models.MigrationStatistics
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
