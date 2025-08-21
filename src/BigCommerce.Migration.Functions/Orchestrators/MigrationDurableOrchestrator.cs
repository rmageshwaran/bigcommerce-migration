using BigCommerce.Migration.Activities.Activities;
using BigCommerce.Migration.Activities.Models;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

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

            // Step 5.3: Broadcast Migration Started Event (Phase 4)
            logger.LogInformation("Step 5.3: Broadcasting migration started event for MigrationId: {MigrationId}", migrationId);
            await context.CallActivityAsync("BroadcastMigrationStartedActivity", new BroadcastMigrationStartedRequest
            {
                MigrationId = migrationId,
                SourceStore = input.MigrationRequest?.SourceStore?.StoreId ?? "Unknown",
                DestinationStore = input.MigrationRequest?.DestinationStore?.StoreId ?? "Unknown",
                StartDateTime = result.StartTime,
                Entities = entityOrder.Select(entityType => new EntityInfo
                {
                    EntityType = entityType,
                    TotalCount = 0, // Will be determined during discovery
                    EstimatedDuration = null
                }).ToList()
            });

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

            // 🆕 TASK 3.3: Simplified final results using database as source of truth
            // Get latest aggregated progress from database (includes real-time incremental updates)
            try
            {
                var finalProgress = await context.CallActivityAsync<MigrationProgress>(
                    "GetLatestAggregatedProgressActivity", 
                    new GetProgressRequest { MigrationId = migrationId, EntityType = null }); // null = all entities

                // Simple assignment - database is the single source of truth
                result.TotalEntitiesProcessed = finalProgress.ProcessedEntities;
                result.TotalEntitiesSuccessful = finalProgress.SuccessfulEntities;
                result.TotalEntitiesFailed = finalProgress.FailedEntities;
                result.TotalEntitiesSkipped = finalProgress.SkippedEntities;
                result.TotalEntitiesCancelled = finalProgress.CancelledEntities;
                
                logger.LogInformation("✅ [TASK-3.3] Final results from database: Processed={Processed}, Successful={Successful}, Failed={Failed}, Skipped={Skipped}, Cancelled={Cancelled}", 
                    result.TotalEntitiesProcessed, result.TotalEntitiesSuccessful, result.TotalEntitiesFailed, finalProgress.SkippedEntities, finalProgress.CancelledEntities);
            }
            catch (Exception progressEx)
            {
                logger.LogWarning(progressEx, "⚠️ [TASK-3.3] Could not retrieve final progress from database, falling back to entity result aggregation");
                
                // Fallback to old method if database query fails
                var totalProcessed = result.EntityResults.Values.Sum(r => r.ProcessedEntities);
                var totalSuccessful = result.EntityResults.Values.Sum(r => r.SuccessfulEntities);
                var totalFailed = result.EntityResults.Values.Sum(r => r.FailedEntities);
                var totalSkipped = result.EntityResults.Values.Sum(r => r.SkippedEntities);
                var totalCancelled = result.EntityResults.Values.Sum(r => r.CancelledEntities);

                result.TotalEntitiesProcessed = totalProcessed;
                result.TotalEntitiesSuccessful = totalSuccessful;
                result.TotalEntitiesFailed = totalFailed;
                result.TotalEntitiesSkipped = totalSkipped;
                result.TotalEntitiesCancelled = totalCancelled;
            }
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
                        migrationId, cancellationState.CancellationSource, cancellationState.CancellationReason, result.TotalEntitiesProcessed, result.TotalEntitiesSuccessful);
                }
                else
                {
                    var cancelledEntityTypes = string.Join(", ", cancelledEntities.Select(r => r.EntityType));
                    result.ErrorMessage = $"Migration was cancelled. Cancelled entities: {cancelledEntityTypes}";
                    
                    logger.LogWarning("Migration was cancelled for MigrationId: {MigrationId}. " +
                                    "Cancelled entities: {CancelledEntities}, Processed: {ProcessedCount}, Successful: {SuccessfulCount}", 
                        migrationId, cancelledEntityTypes, result.TotalEntitiesProcessed, result.TotalEntitiesSuccessful);
                }
                
                // 🎯 FIX: Get real final counts from chunkincrementevents before completing cancelled migration
                logger.LogInformation("🚫 Getting final real progress for cancelled migration {MigrationId}", migrationId);
                var cancelledMigrationProgress = await context.CallActivityAsync<MigrationProgress>(
                    "GetLatestAggregatedProgressActivity", 
                    new GetProgressRequest { MigrationId = migrationId, EntityType = null });
                
                if (cancelledMigrationProgress != null)
                {
                    // Update result with real counts from chunkincrementevents aggregation
                    result.TotalEntitiesProcessed = cancelledMigrationProgress.ProcessedEntities;
                    result.TotalEntitiesSuccessful = cancelledMigrationProgress.SuccessfulEntities;
                    result.TotalEntitiesFailed = cancelledMigrationProgress.FailedEntities;
                    result.TotalEntitiesSkipped = cancelledMigrationProgress.SkippedEntities;
                    result.TotalEntitiesCancelled = cancelledMigrationProgress.CancelledEntities;
                    
                    logger.LogInformation("🎯 Updated cancelled migration {MigrationId} with real counts: " +
                        "Processed={ProcessedCount}, Successful={SuccessfulCount}, Failed={FailedCount}, Skipped={SkippedCount}, Cancelled={CancelledCount}",
                        migrationId, cancelledMigrationProgress.ProcessedEntities, cancelledMigrationProgress.SuccessfulEntities, 
                        cancelledMigrationProgress.FailedEntities, cancelledMigrationProgress.SkippedEntities, cancelledMigrationProgress.CancelledEntities);
                }
                
                // Phase 4: Broadcast migration cancellation event
                await context.CallActivityAsync("BroadcastMigrationCompletedActivity", new BroadcastMigrationCompletedRequest
                {
                    MigrationId = migrationId,
                    Status = "Cancelled",
                    Message = result.ErrorMessage ?? "Migration was cancelled",
                    TotalProcessedEntities = result.TotalEntitiesProcessed,
                    TotalFailedEntities = result.TotalEntitiesFailed,
                    DurationMs = (long)result.Duration.TotalMilliseconds,
                    EndDateTime = result.EndTime ?? context.CurrentUtcDateTime
                });
                
                // Complete migration as cancelled - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Cancelled, result.ErrorMessage);
                
                return result;
            }

            // 🎯 FIX: Get real final counts from chunkincrementevents for ALL migrations before determining status
            logger.LogInformation("🏁 Getting final real progress for completed migration {MigrationId}", migrationId);
            var completedMigrationProgress = await context.CallActivityAsync<MigrationProgress>(
                "GetLatestAggregatedProgressActivity", 
                new GetProgressRequest { MigrationId = migrationId, EntityType = null });
            
            if (completedMigrationProgress != null)
            {
                // Update result with real counts from chunkincrementevents aggregation
                result.TotalEntitiesProcessed = completedMigrationProgress.ProcessedEntities;
                result.TotalEntitiesSuccessful = completedMigrationProgress.SuccessfulEntities;
                result.TotalEntitiesFailed = completedMigrationProgress.FailedEntities;
                result.TotalEntitiesSkipped = completedMigrationProgress.SkippedEntities;
                result.TotalEntitiesCancelled = completedMigrationProgress.CancelledEntities;
                
                logger.LogInformation("🎯 Updated completed migration {MigrationId} with real counts: " +
                    "Processed={ProcessedCount}, Successful={SuccessfulCount}, Failed={FailedCount}, Skipped={SkippedCount}, Cancelled={CancelledCount}",
                    migrationId, completedMigrationProgress.ProcessedEntities, completedMigrationProgress.SuccessfulEntities, 
                    completedMigrationProgress.FailedEntities, completedMigrationProgress.SkippedEntities, completedMigrationProgress.CancelledEntities);
            }

            // Determine final status for non-cancelled migrations
            if (result.TotalEntitiesProcessed == 0)
            {
                result.Status = "Failed";
                result.ErrorMessage = "No entities were processed";
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Failed, result.ErrorMessage);
                
                // Phase 4: Broadcast migration completion event
                await context.CallActivityAsync("BroadcastMigrationCompletedActivity", new BroadcastMigrationCompletedRequest
                {
                    MigrationId = migrationId,
                    Status = "Failed",
                    Message = result.ErrorMessage,
                    TotalProcessedEntities = result.TotalEntitiesProcessed,
                    TotalFailedEntities = result.TotalEntitiesFailed,
                    DurationMs = (long)result.Duration.TotalMilliseconds,
                    EndDateTime = result.EndTime ?? context.CurrentUtcDateTime
                });
            }
            else if (result.TotalEntitiesFailed == 0)
            {
                result.Status = "Completed";
                logger.LogInformation("Migration completed successfully for MigrationId: {MigrationId}. " +
                                    "Processed: {ProcessedCount}, Successful: {SuccessfulCount}", 
                    migrationId, result.TotalEntitiesProcessed, result.TotalEntitiesSuccessful);
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Completed);
                
                // Phase 4: Broadcast migration completion event
                await context.CallActivityAsync("BroadcastMigrationCompletedActivity", new BroadcastMigrationCompletedRequest
                {
                    MigrationId = migrationId,
                    Status = "Completed",
                    Message = "Migration completed successfully",
                    TotalProcessedEntities = result.TotalEntitiesProcessed,
                    TotalFailedEntities = result.TotalEntitiesFailed,
                    DurationMs = (long)result.Duration.TotalMilliseconds,
                    EndDateTime = result.EndTime ?? context.CurrentUtcDateTime
                });
            }
            else if (result.TotalEntitiesSuccessful > 0)
            {
                result.Status = "CompletedWithErrors";
                result.ErrorMessage = $"Migration completed with {result.TotalEntitiesFailed} failed entities out of {result.TotalEntitiesProcessed} total";
                logger.LogWarning("Migration completed with errors for MigrationId: {MigrationId}. " +
                                "Processed: {ProcessedCount}, Successful: {SuccessfulCount}, Failed: {FailedCount}", 
                    migrationId, result.TotalEntitiesProcessed, result.TotalEntitiesSuccessful, result.TotalEntitiesFailed);
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Completed, result.ErrorMessage);
                
                // Phase 4: Broadcast migration completion event
                await context.CallActivityAsync("BroadcastMigrationCompletedActivity", new BroadcastMigrationCompletedRequest
                {
                    MigrationId = migrationId,
                    Status = "CompletedWithErrors",
                    Message = result.ErrorMessage,
                    TotalProcessedEntities = result.TotalEntitiesProcessed,
                    TotalFailedEntities = result.TotalEntitiesFailed,
                    DurationMs = (long)result.Duration.TotalMilliseconds,
                    EndDateTime = result.EndTime ?? context.CurrentUtcDateTime
                });
            }
            else if (result.TotalEntitiesSuccessful == 0 && result.TotalEntitiesFailed > 0)
            {
                result.Status = "Failed";
                result.ErrorMessage = $"All {result.TotalEntitiesFailed} entities failed to migrate";
                logger.LogError("Migration failed - all entities failed for MigrationId: {MigrationId}. " +
                              "Failed: {FailedCount}", migrationId, result.TotalEntitiesFailed);
                
                // Complete migration - update storage service for HTTP API
                await CompleteMigrationAsync(context, migrationId, result, MigrationStatus.Failed, result.ErrorMessage);
                
                // Phase 4: Broadcast migration completion event
                await context.CallActivityAsync("BroadcastMigrationCompletedActivity", new BroadcastMigrationCompletedRequest
                {
                    MigrationId = migrationId,
                    Status = "Failed",
                    Message = result.ErrorMessage,
                    TotalProcessedEntities = result.TotalEntitiesProcessed,
                    TotalFailedEntities = result.TotalEntitiesFailed,
                    DurationMs = (long)result.Duration.TotalMilliseconds,
                    EndDateTime = result.EndTime ?? context.CurrentUtcDateTime
                });
            }
            else
            {
                // Fallback case - should not normally reach here, but handle gracefully
                result.Status = "CompletedWithErrors";
                result.ErrorMessage = $"Migration completed with unexpected status: {result.TotalEntitiesProcessed} processed, {result.TotalEntitiesSuccessful} successful, {result.TotalEntitiesFailed} failed";
                logger.LogWarning("Migration completed with unexpected status for MigrationId: {MigrationId}. " +
                                "Processed: {ProcessedCount}, Successful: {SuccessfulCount}, Failed: {FailedCount}", 
                    migrationId, result.TotalEntitiesProcessed, result.TotalEntitiesSuccessful, result.TotalEntitiesFailed);
            }

            return result;
        }
        catch (TaskCanceledException)
        {
            logger.LogInformation("Migration orchestration was cancelled for MigrationId: {MigrationId}", migrationId);
            result.Status = "Cancelled";
            result.ErrorMessage = "Migration orchestration was cancelled";
            result.EndTime = context.CurrentUtcDateTime;
            
            // Phase 4: Broadcast migration cancellation event
            await context.CallActivityAsync("BroadcastMigrationCompletedActivity", new BroadcastMigrationCompletedRequest
            {
                MigrationId = migrationId,
                Status = "Cancelled",
                Message = result.ErrorMessage,
                TotalProcessedEntities = result.TotalEntitiesProcessed,
                TotalFailedEntities = result.TotalEntitiesFailed,
                DurationMs = (long)result.Duration.TotalMilliseconds,
                EndDateTime = result.EndTime ?? context.CurrentUtcDateTime
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
    public int TotalEntitiesSkipped { get; set; }
    public int TotalEntitiesCancelled { get; set; }
    public Dictionary<string, EntityMigrationResult> EntityResults { get; set; } = new();
}
