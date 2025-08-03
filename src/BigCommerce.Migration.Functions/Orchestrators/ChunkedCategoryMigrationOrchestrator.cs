using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Functions.Orchestrators;

/// <summary>
/// Chunked Category Migration Orchestrator for level-by-level category processing
/// Task 4.1: Azure Durable Functions orchestrator with deterministic execution compliance
/// Coordinates FetchCategoriesForLevelActivity and ProcessCategoryLevelActivity for optimal performance
/// </summary>
public static class ChunkedCategoryMigrationOrchestrator
{
    /// <summary>
    /// Main chunked category migration orchestrator function
    /// Processes categories level-by-level for optimal memory usage and performance
    /// CRITICAL: Maintains deterministic execution for Azure Durable Functions compliance
    /// </summary>
    [Function("ChunkedCategoryMigrationOrchestrator")]
    public static async Task<ChunkedCategoryMigrationResult> RunChunkedCategoryMigration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger("ChunkedCategoryMigrationOrchestrator");
        var input = context.GetInput<ChunkedCategoryMigrationRequest>();
        
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input), "Chunked category migration orchestration request is required");
        }

        var migrationId = input.MigrationId;
        var result = new ChunkedCategoryMigrationResult
        {
            MigrationId = migrationId,
            StartTime = context.CurrentUtcDateTime,
            Status = "InProgress"
        };

        try
        {
            logger.LogInformation("🚀 Starting chunked category migration orchestration for MigrationId: {MigrationId}", migrationId);

            // Step 1: Check for live cancellation before starting (Entity-level for categories)
            var entityCancellationCheck = await context.CallActivityAsync<CancellationCheckResult>(
                "CheckLiveCancellationActivity",
                new CancellationCheckRequest
                {
                    MigrationId = migrationId,
                    Scope = CancellationScope.EntityType,
                    EntityType = "categories"
                });

            if (entityCancellationCheck.IsCancelled)
            {
                logger.LogInformation("🚫 Migration {MigrationId} was cancelled before starting chunked category processing. Reason: {Reason}", 
                    migrationId, entityCancellationCheck.Reason);
                result = CreateCancelledResult(result, context.CurrentUtcDateTime, 
                    entityCancellationCheck.Reason ?? "Migration cancelled before processing started");
                
                // Process cancellation through live cancellation system
                await context.CallActivityAsync("ProcessCancellationActivity", 
                    new CancellationProcessRequest 
                    { 
                        MigrationId = migrationId, 
                        Scope = CancellationScope.EntityType,
                        EntityType = "categories",
                        Reason = entityCancellationCheck.Reason ?? "Chunked category migration cancelled"
                    });
                
                // Send cancellation progress notification
                if (input.EnableProgressUpdates)
                {
                    await context.CallActivityAsync<object>(
                        "CompleteChunkedMigrationProgressActivity",
                        new CompleteChunkedMigrationProgressRequest
                        {
                            MigrationId = migrationId,
                            Success = false,
                            TotalCategoriesProcessed = 0,
                            TotalCategoriesCreated = 0,
                            PerformanceImprovement = 1.0,
                            ProcessingTimeMinutes = 0,
                            LevelsProcessed = 0,
                            LevelsFailed = 0
                        });
                }
                
                return result;
                }

            // Step 2: Analyze hierarchy to determine levels (deterministic activity call)
            logger.LogInformation("📊 Step 1: Analyzing category hierarchy for MigrationId: {MigrationId}", migrationId);
            var hierarchyAnalysis = await context.CallActivityAsync<HierarchyMetadata>(
                "AnalyzeHierarchyActivity",
                new 
                {
                    MigrationId = migrationId,
                    SourceStore = input.SourceStore,
                    Config = input.ChunkedHierarchyConfig,
                    CategoryTreeContext = input.CategoryTreeContext
                });

            if (hierarchyAnalysis.TotalCategories == 0)
            {
                logger.LogInformation("✅ No categories found for migration {MigrationId}. Completing with success.", migrationId);
                return CreateEmptySuccessResult(result, context.CurrentUtcDateTime);
            }

            logger.LogInformation("📈 Hierarchy analysis complete: {TotalCategories} categories across {MaxDepth} levels for MigrationId: {MigrationId}", 
                hierarchyAnalysis.TotalCategories, hierarchyAnalysis.MaxDepth, migrationId);

            // Step 3: Send migration start progress update (deterministic activity call)
            if (input.EnableProgressUpdates)
            {
                await context.CallActivityAsync<object>(
                    "StartChunkedMigrationProgressActivity",
                    new StartChunkedMigrationProgressRequest
                    {
                        MigrationId = migrationId,
                        TotalCategories = hierarchyAnalysis.TotalCategories,
                        TotalLevels = hierarchyAnalysis.MaxDepth,
                        EstimatedTimeMinutes = hierarchyAnalysis.EstimatedProcessingTimeMinutes
                    });
            }

            // Step 4: Process levels in hierarchical order (deterministic level-by-level processing)
            var maxLevels = Math.Min(hierarchyAnalysis.MaxDepth, input.MaxLevelsToProcess);
            logger.LogInformation("🔄 Step 2: Processing {MaxLevels} hierarchy levels for MigrationId: {MigrationId}", maxLevels, migrationId);

            for (int level = 0; level < maxLevels; level++)
            {
                logger.LogInformation("📊 Processing Level {Level} for MigrationId: {MigrationId}", level, migrationId);

                // Check for live cancellation before each level (Batch-level for each level)
                var levelCancellationCheck = await context.CallActivityAsync<CancellationCheckResult>(
                    "CheckLiveCancellationActivity",
                    new CancellationCheckRequest
                    {
                        MigrationId = migrationId,
                        Scope = CancellationScope.Batch,
                        EntityType = "categories",
                        BatchId = $"level-{level}"
                    });

                if (levelCancellationCheck.IsCancelled)
                {
                    logger.LogInformation("🚫 Migration {MigrationId} was cancelled during Level {Level} processing. Reason: {Reason}. Partial progress: {ProcessedLevels}/{TotalLevels} levels, {TotalCategoriesProcessed} categories processed", 
                        migrationId, level, levelCancellationCheck.Reason, result.ProcessedLevels, maxLevels, result.TotalCategoriesProcessed);
                    
                    result = CreateCancelledResult(result, context.CurrentUtcDateTime, 
                        levelCancellationCheck.Reason ?? $"Migration cancelled during Level {level} processing");
                    
                    // Process cancellation through live cancellation system
                    await context.CallActivityAsync("ProcessCancellationActivity", 
                        new CancellationProcessRequest 
                        { 
                            MigrationId = migrationId, 
                            Scope = CancellationScope.Batch,
                            EntityType = "categories",
                            BatchId = $"level-{level}",
                            Reason = levelCancellationCheck.Reason ?? $"Category level {level} cancelled"
                        });
                    
                    // Send level-specific cancellation progress notification
                    if (input.EnableProgressUpdates)
                    {
                        await context.CallActivityAsync<object>(
                            "CompleteChunkedMigrationProgressActivity",
                            new CompleteChunkedMigrationProgressRequest
                            {
                                MigrationId = migrationId,
                                Success = false,
                                TotalCategoriesProcessed = result.TotalCategoriesProcessed,
                                TotalCategoriesCreated = result.TotalCategoriesCreated,
                                PerformanceImprovement = result.PerformanceImprovement,
                                ProcessingTimeMinutes = result.TotalProcessingTimeMinutes,
                                LevelsProcessed = result.ProcessedLevels,
                                LevelsFailed = result.FailedLevels
                            });
                    }
                    
                    return result;
                }

                try
                {
                    var levelResult = await ProcessSingleLevel(context, input, level, logger);
                    result.LevelResults[level] = ConvertToChunkedLevelResult(levelResult);
                    result.TotalLevelsProcessed++;

                    if (levelResult.Success)
                    {
                        result.ProcessedLevels++;
                        result.TotalCategoriesProcessed += levelResult.ProcessedCategories;
                        result.TotalCategoriesCreated += levelResult.ProcessedCategories; // Assuming processed = created for successful levels
                    }
                    else
                    {
                        result.FailedLevels++;
                        result.TotalCategoriesFailed += levelResult.TotalCategories - levelResult.ProcessedCategories;
                        var errorMessages = levelResult.BatchErrors.Select(e => e.ErrorMessage ?? "Unknown batch error");
                        result.ErrorMessages.Add($"Level {level}: {string.Join("; ", errorMessages)}");
                        
                        // Continue-on-error: Log failure but continue with next level
                        logger.LogWarning("⚠️ Level {Level} failed for MigrationId: {MigrationId}. Continuing with next level (continue-on-error)", level, migrationId);
                    }

                    // Update running totals
                    result.TotalProcessingTimeMinutes += levelResult.ProcessingTimeMinutes;
                    result.PeakMemoryUsageMB = Math.Max(result.PeakMemoryUsageMB, levelResult.MemoryUsageMB);
                    result.PerformanceImprovement = Math.Max(result.PerformanceImprovement, levelResult.PerformanceImprovement);

                    // Send level completion progress update (deterministic activity call)
                    if (input.EnableProgressUpdates)
                    {
                        await context.CallActivityAsync<object>(
                            "LevelCompletionProgressActivity",
                            new LevelCompletionProgressRequest
                            {
                                MigrationId = migrationId,
                                Level = level,
                                Success = levelResult.Success,
                                CategoriesProcessed = levelResult.ProcessedCategories,
                                ProcessingTimeMinutes = levelResult.ProcessingTimeMinutes,
                                CompletedLevels = result.ProcessedLevels,
                                TotalLevels = maxLevels,
                                CumulativeCategoriesProcessed = result.TotalCategoriesProcessed,
                                CumulativeCategoriesFailed = result.TotalCategoriesFailed,
                                TotalCategories = hierarchyAnalysis.TotalCategories
                            });
                    }

                    logger.LogInformation("✅ Completed Level {Level} for MigrationId: {MigrationId}. " +
                        "Success: {Success}, Categories: {ProcessedCategories}/{TotalCategories}, Time: {ProcessingTimeMinutes:F2}min", 
                        level, migrationId, levelResult.Success, levelResult.ProcessedCategories, levelResult.TotalCategories, levelResult.ProcessingTimeMinutes);
                }
                catch (TimeoutException ex)
                {
                    // Handle activity timeout with continue-on-error
                    logger.LogWarning(ex, "⏰ Level {Level} processing timed out for MigrationId: {MigrationId}. Continuing with next level.", level, migrationId);
                    result.FailedLevels++;
                    result.TotalLevelsProcessed++;
                    result.ErrorMessages.Add($"Level {level}: Processing timed out - {ex.Message}");
                }
                catch (Exception ex)
                {
                    // Handle unexpected errors with continue-on-error
                    logger.LogError(ex, "❌ Unexpected error processing Level {Level} for MigrationId: {MigrationId}. Continuing with next level.", level, migrationId);
                    result.FailedLevels++;
                    result.TotalLevelsProcessed++;
                    result.ErrorMessages.Add($"Level {level}: Unexpected error - {ex.Message}");
                }
            }

            // Step 5: Complete migration and send final progress update
            result.EndTime = context.CurrentUtcDateTime;
            result.Success = result.ProcessedLevels > 0; // Success if at least one level processed (continue-on-error)
            result.Status = result.Success ? "Completed" : "Failed";

            if (input.EnableProgressUpdates)
            {
                await context.CallActivityAsync<object>(
                    "CompleteChunkedMigrationProgressActivity",
                    new CompleteChunkedMigrationProgressRequest
                    {
                        MigrationId = migrationId,
                        Success = result.Success,
                        TotalCategoriesProcessed = result.TotalCategoriesProcessed,
                        TotalCategoriesCreated = result.TotalCategoriesCreated,
                        PerformanceImprovement = result.PerformanceImprovement,
                        ProcessingTimeMinutes = result.TotalProcessingTimeMinutes,
                        LevelsProcessed = result.ProcessedLevels,
                        LevelsFailed = result.FailedLevels
                    });
            }

            logger.LogInformation("🎉 Completed chunked category migration for MigrationId: {MigrationId}. " +
                "Success: {Success}, Levels: {ProcessedLevels}/{TotalLevels}, Categories: {TotalCategoriesCreated}, " +
                "Performance: {PerformanceImprovement:F1}x, Time: {TotalProcessingTimeMinutes:F2}min", 
                migrationId, result.Success, result.ProcessedLevels, result.TotalLevelsProcessed, 
                result.TotalCategoriesCreated, result.PerformanceImprovement, result.TotalProcessingTimeMinutes);

            return result;
        }
        catch (TaskCanceledException)
        {
            logger.LogInformation("🚫 Chunked category migration orchestration was cancelled for MigrationId: {MigrationId}", migrationId);
            
            result.Success = false;
            result.Status = "Cancelled";
            result.EndTime = context.CurrentUtcDateTime;
            result.CancellationReason = "Migration orchestration was cancelled";
            result.ErrorMessages.Add("Migration orchestration was cancelled");
            
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 Fatal error in chunked category migration orchestrator for MigrationId: {MigrationId}: {ErrorMessage}", 
                migrationId, ex.Message);
            
            result.Success = false;
            result.Status = "Failed";
            result.EndTime = context.CurrentUtcDateTime;
            result.ErrorMessages.Add($"Orchestrator fatal error: {ex.Message}");
            
            return result;
        }
        finally
        {
            // Task 4.1.3: Cleanup operations - Ensure graceful shutdown regardless of outcome
            try
            {
                logger.LogInformation("🧹 Performing cleanup operations for chunked category migration MigrationId: {MigrationId}", migrationId);
                
                // Send final status progress notification if not already sent
                if (input.EnableProgressUpdates && !result.Success && result.Status != "Cancelled")
                {
                    await context.CallActivityAsync<object>(
                        "CompleteChunkedMigrationProgressActivity",
                        new CompleteChunkedMigrationProgressRequest
                        {
                            MigrationId = migrationId,
                            Success = false,
                            TotalCategoriesProcessed = result.TotalCategoriesProcessed,
                            TotalCategoriesCreated = result.TotalCategoriesCreated,
                            PerformanceImprovement = result.PerformanceImprovement,
                            ProcessingTimeMinutes = result.TotalProcessingTimeMinutes,
                            LevelsProcessed = result.ProcessedLevels,
                            LevelsFailed = result.FailedLevels
                        });
                }
                
                // Log final cleanup summary
                logger.LogInformation("✅ Cleanup completed for chunked category migration MigrationId: {MigrationId}. " +
                    "Final status: {Status}, Levels processed: {ProcessedLevels}, Categories: {TotalCategoriesProcessed}", 
                    migrationId, result.Status, result.ProcessedLevels, result.TotalCategoriesProcessed);
            }
            catch (Exception cleanupEx)
            {
                logger.LogError(cleanupEx, "⚠️ Error during cleanup operations for chunked category migration MigrationId: {MigrationId} - migration result preserved", migrationId);
                // Don't throw - preserve the original migration result
            }
        }
    }

    /// <summary>
    /// Processes a single hierarchy level using the Phase 3 activities
    /// CRITICAL: Uses only deterministic activity calls for Azure Durable Functions compliance
    /// </summary>
    private static async Task<LevelProcessingActivityResult> ProcessSingleLevel(
        TaskOrchestrationContext context, 
        ChunkedCategoryMigrationRequest input, 
        int level, 
        ILogger logger)
    {
        var migrationId = input.MigrationId;
        
        logger.LogDebug("🔍 Fetching categories for Level {Level}, MigrationId: {MigrationId}", level, migrationId);
        
        // Step 1: Fetch categories for this level (deterministic activity call)
        var fetchResult = await context.CallActivityAsync<LevelFetchActivityResult>(
            "FetchCategoriesForLevelActivity",
            new LevelFetchActivityInput
            {
                LevelRequest = new LevelFetchRequest
                {
                    Level = level,
                    ParentCategoryId = level == 0 ? null : (int?)null, // Will be resolved by the activity
                    BatchSize = input.ChunkedHierarchyConfig.BatchSizePerLevel,
                    CategoryTreeContext = input.CategoryTreeContext // ✅ CRITICAL FIX: Pass CategoryTreeContext for correct tree_id
                },
                DiscoveryRequest = new EntityDiscoveryRequest
                {
                    MigrationId = migrationId,
                    EntityType = "categories",
                    SourceStore = input.SourceStore
                },
                TimeoutMinutes = 4.0
            });

        if (!fetchResult.Success || fetchResult.SuccessCount == 0)
        {
            logger.LogInformation("📭 No categories found for Level {Level}, MigrationId: {MigrationId}. Returning empty success.", level, migrationId);
            return new LevelProcessingActivityResult
            {
                Success = true,
                Level = level,
                TotalCategories = 0,
                ProcessedCategories = 0,
                FailedCategories = 0,
                ProcessingTimeMinutes = fetchResult.ProcessingTimeMinutes,
                MemoryUsageMB = fetchResult.MemoryUsageMB,
                PerformanceImprovement = 1.0,
                BatchErrors = fetchResult.Errors.Select(e => new BatchProcessingError 
                { 
                    ErrorMessage = e.ErrorMessage ?? "Unknown error", 
                    BatchNumber = 0, 
                    FailedCategoryIds = new List<string>() 
                }).ToList()
            };
        }

        logger.LogDebug("📦 Processing {CategoryCount} categories for Level {Level}, MigrationId: {MigrationId}", 
            fetchResult.SuccessCount, level, migrationId);
        
        // Step 2: Process/transform/create categories for this level (deterministic activity call)
        var processResult = await context.CallActivityAsync<LevelProcessingActivityResult>(
            "ProcessCategoryLevelActivity",
            new LevelProcessingActivityInput
            {
                ProcessingRequest = new LevelProcessingRequest
                {
                    Level = level,
                    CategoryIds = fetchResult.FetchedCategoryIds ?? new List<string>(),
                    BatchSize = input.ChunkedHierarchyConfig.BatchSizePerLevel
                },
                DiscoveryRequest = new EntityDiscoveryRequest
                {
                    MigrationId = migrationId,
                    EntityType = "categories",
                    SourceStore = input.SourceStore,
                    CategoryTreeContext = input.CategoryTreeContext
                },
                TimeoutMinutes = 4.0
            });

        return processResult ?? new LevelProcessingActivityResult
        {
            Success = false,
            Level = level,
            TotalCategories = 0,
            ProcessedCategories = 0,
            FailedCategories = 0,
            ProcessingTimeMinutes = 0.0,
            MemoryUsageMB = 0.0,
            PerformanceImprovement = 1.0,
            BatchErrors = new List<BatchProcessingError> 
            { 
                new BatchProcessingError 
                { 
                    ErrorMessage = "Activity returned null result", 
                    BatchNumber = 0, 
                    FailedCategoryIds = new List<string>() 
                } 
            }
        };
    }

    /// <summary>
    /// Converts LevelProcessingActivityResult to ChunkedLevelResult
    /// CRITICAL: Pure function for deterministic behavior
    /// </summary>
    private static ChunkedLevelResult ConvertToChunkedLevelResult(LevelProcessingActivityResult activityResult)
    {
        return new ChunkedLevelResult
        {
            Level = activityResult.Level,
            Success = activityResult.Success,
            CategoriesFetched = activityResult.TotalCategories,
            CategoriesProcessed = activityResult.ProcessedCategories,
            CategoriesCreated = activityResult.ProcessedCategories, // Assuming processed = created for successful processing
            CategoriesFailed = activityResult.FailedCategories,
            ProcessingTimeMinutes = activityResult.ProcessingTimeMinutes,
            MemoryUsageMB = activityResult.MemoryUsageMB,
            PerformanceImprovement = activityResult.PerformanceImprovement,
            ErrorMessages = activityResult.BatchErrors.Select(e => e.ErrorMessage ?? "Unknown batch error").ToList()
        };
    }

    /// <summary>
    /// Creates a cancelled result for deterministic cancellation handling
    /// Task 4.1.3: Enhanced cancellation result with comprehensive tracking
    /// CRITICAL: Pure function for deterministic behavior
    /// </summary>
    private static ChunkedCategoryMigrationResult CreateCancelledResult(
        ChunkedCategoryMigrationResult result, 
        DateTime endTime, 
        string cancellationReason)
    {
        result.Success = false;
        result.Status = "Cancelled";
        result.EndTime = endTime;
        result.CancellationReason = cancellationReason;
        
        // Task 4.1.3: Enhanced cancellation tracking
        // Calculate processing time up to cancellation point
        result.TotalProcessingTimeMinutes = (endTime - result.StartTime).TotalMinutes;
        
        // Ensure performance metrics are set to reasonable defaults for cancelled migrations
        if (result.PerformanceImprovement <= 0)
        {
            result.PerformanceImprovement = 1.0;
        }
        
        // Log comprehensive cancellation summary in error messages
        result.ErrorMessages.Add($"Migration cancelled: {cancellationReason}");
        result.ErrorMessages.Add($"Partial progress: {result.ProcessedLevels} levels completed, {result.TotalCategoriesProcessed} categories processed");
        result.ErrorMessages.Add($"Processing time until cancellation: {result.TotalProcessingTimeMinutes:F2} minutes");
        
        return result;
    }

    /// <summary>
    /// Creates a successful result for empty category hierarchies
    /// CRITICAL: Pure function for deterministic behavior
    /// </summary>
    private static ChunkedCategoryMigrationResult CreateEmptySuccessResult(
        ChunkedCategoryMigrationResult result, 
        DateTime endTime)
    {
        result.Success = true;
        result.Status = "Completed";
        result.EndTime = endTime;
        result.TotalCategoriesProcessed = 0;
        result.TotalCategoriesCreated = 0;
        result.TotalLevelsProcessed = 0;
        result.ProcessedLevels = 0;
        result.PerformanceImprovement = 1.0;
        return result;
    }
}