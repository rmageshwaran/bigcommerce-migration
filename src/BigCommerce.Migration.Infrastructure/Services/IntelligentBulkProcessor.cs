using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services
{
    /// <summary>
    /// Phase 5: Intelligent Bulk Processing - Task 5.2.1
    /// Groups operations for maximum efficiency with smart batching algorithms
    /// 
    /// Goals:
    /// - Achieve 90% storage efficiency improvement through intelligent operation grouping
    /// - Optimize mixed entity type processing with smart batching strategies
    /// - Implement adaptive processing based on entity characteristics and system performance
    /// - Maintain memory safety while maximizing throughput and storage efficiency
    /// </summary>
    public class IntelligentBulkProcessor
    {
        private readonly ILogger<IntelligentBulkProcessor> _logger;
        private readonly DynamicBatchSizeCalculator _batchSizeCalculator;
        private readonly AdaptiveConcurrencyController _concurrencyController;
        
        // Performance thresholds for intelligent processing
        private const int MinBatchEfficiencyThreshold = 75; // 75% minimum efficiency
        private const int OptimalMemoryUsageThreshold = 80; // 80MB optimal memory usage
        private const double MaxProcessingTimeThreshold = 5.0; // 5 seconds max processing time

        /// <summary>
        /// Initializes a new instance of the IntelligentBulkProcessor
        /// </summary>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="batchSizeCalculator">Calculator for optimal batch sizes</param>
        /// <param name="concurrencyController">Controller for adaptive concurrency</param>
        public IntelligentBulkProcessor(
            ILogger<IntelligentBulkProcessor> logger,
            DynamicBatchSizeCalculator batchSizeCalculator,
            AdaptiveConcurrencyController concurrencyController)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _batchSizeCalculator = batchSizeCalculator ?? throw new ArgumentNullException(nameof(batchSizeCalculator));
            _concurrencyController = concurrencyController ?? throw new ArgumentNullException(nameof(concurrencyController));
        }

        /// <summary>
        /// Processes mixed entity types with intelligent grouping and optimization
        /// </summary>
        /// <typeparam name="T">Base type of entities to process</typeparam>
        /// <param name="entityGroups">Groups of entities organized by type and characteristics</param>
        /// <param name="processingStrategies">Processing strategies for different entity types</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Intelligent bulk processing results with efficiency metrics</returns>
        public async Task<IntelligentBulkResults> ProcessMixedEntitiesAsync<T>(
            IEnumerable<EntityGroup<T>> entityGroups,
            IDictionary<string, Func<IEnumerable<T>, Task<ProcessingResult>>> processingStrategies,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                _logger.LogInformation("Starting intelligent bulk processing for {GroupCount} entity groups", 
                    entityGroups.Count());

                var results = new IntelligentBulkResults
                {
                    StartTime = startTime,
                    ProcessingStrategies = new List<string>(processingStrategies.Keys)
                };

                // Step 1: Analyze and optimize entity groups
                var optimizedGroups = await AnalyzeAndOptimizeGroupsAsync(entityGroups, cancellationToken)
                    .ConfigureAwait(false);

                // Step 2: Determine optimal processing order based on dependencies and efficiency
                var processingPlan = await CreateIntelligentProcessingPlanAsync(optimizedGroups, cancellationToken)
                    .ConfigureAwait(false);

                // Step 3: Execute processing plan with adaptive optimization
                await ExecuteProcessingPlanAsync(processingPlan, processingStrategies, results, cancellationToken)
                    .ConfigureAwait(false);

                // Step 4: Calculate final efficiency metrics
                results.EndTime = DateTime.UtcNow;
                results.TotalDuration = results.EndTime - results.StartTime;
                CalculateIntelligentEfficiencyMetrics(results);

                _logger.LogInformation("Intelligent bulk processing completed. " +
                    "Overall Efficiency: {OverallEfficiency:F1}%, Groups Processed: {GroupsProcessed}, " +
                    "Total Duration: {Duration:F1}s",
                    results.OverallEfficiencyGain, results.GroupResults.Count, 
                    results.TotalDuration.TotalSeconds);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during intelligent bulk processing");
                throw;
            }
        }

        /// <summary>
        /// Processes entities with smart batching optimization for homogeneous data
        /// </summary>
        /// <typeparam name="T">Type of entities to process</typeparam>
        /// <param name="entities">Collection of entities to process</param>
        /// <param name="entityType">Type identifier for optimization strategies</param>
        /// <param name="processingFunction">Function to process each optimized batch</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Smart batching processing results</returns>
        public async Task<SmartBatchingResults> ProcessWithSmartBatchingAsync<T>(
            IEnumerable<T> entities,
            string entityType,
            Func<IEnumerable<T>, Task<ProcessingResult>> processingFunction,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var entityList = entities.ToList();
                var startTime = DateTime.UtcNow;

                _logger.LogInformation("Starting smart batching for {EntityCount} {EntityType} entities", 
                    entityList.Count, entityType);

                var results = new SmartBatchingResults
                {
                    EntityType = entityType,
                    TotalEntities = entityList.Count,
                    StartTime = startTime
                };

                // Step 1: Analyze entity characteristics for optimal batching
                var entityCharacteristics = AnalyzeEntityCharacteristics(entityList, entityType);
                
                // Step 2: Calculate optimal batch configuration
                var batchConfiguration = await CalculateOptimalBatchConfigurationAsync(
                    entityCharacteristics, cancellationToken).ConfigureAwait(false);

                // Step 3: Create smart batches based on characteristics and configuration
                var smartBatches = CreateSmartBatches(entityList, batchConfiguration);

                // Step 4: Process batches with adaptive optimization
                await ProcessSmartBatchesAsync(smartBatches, processingFunction, results, cancellationToken)
                    .ConfigureAwait(false);

                // Step 5: Calculate smart batching efficiency metrics
                results.EndTime = DateTime.UtcNow;
                results.Duration = results.EndTime - results.StartTime;
                CalculateSmartBatchingMetrics(results, entityList.Count);

                _logger.LogInformation("Smart batching completed for {EntityType}. " +
                    "Efficiency: {Efficiency:F1}%, Batches: {BatchCount}, Duration: {Duration:F1}s",
                    entityType, results.EfficiencyGain, results.ProcessedBatches, 
                    results.Duration.TotalSeconds);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during smart batching for {EntityType}", entityType);
                throw;
            }
        }

        /// <summary>
        /// Analyzes and optimizes entity groups for maximum processing efficiency
        /// </summary>
        private async Task<IEnumerable<OptimizedEntityGroup<T>>> AnalyzeAndOptimizeGroupsAsync<T>(
            IEnumerable<EntityGroup<T>> entityGroups,
            CancellationToken cancellationToken)
        {
            var optimizedGroups = new List<OptimizedEntityGroup<T>>();

            foreach (var group in entityGroups)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _logger.LogDebug("Analyzing entity group: {EntityType} with {EntityCount} entities", 
                    group.EntityType, group.Entities.Count());

                var complexity = DetermineEntityComplexity(group.EntityType);
                var optimalBatchSize = await _batchSizeCalculator.CalculateOptimalBatchSizeAsync(
                    group.EntityType, complexity, group.EstimatedEntitySize).ConfigureAwait(false);

                var optimizedGroup = new OptimizedEntityGroup<T>
                {
                    EntityType = group.EntityType,
                    Entities = group.Entities,
                    EstimatedEntitySize = group.EstimatedEntitySize,
                    Complexity = complexity,
                    OptimalBatchSize = optimalBatchSize,
                    Priority = group.Priority,
                    Dependencies = group.Dependencies,
                    ProcessingWeight = CalculateProcessingWeight(group, complexity)
                };

                optimizedGroups.Add(optimizedGroup);
            }

            return optimizedGroups.OrderBy(g => g.Priority).ThenByDescending(g => g.ProcessingWeight);
        }

        /// <summary>
        /// Creates an intelligent processing plan based on dependencies and efficiency
        /// </summary>
        private async Task<ProcessingPlan<T>> CreateIntelligentProcessingPlanAsync<T>(
            IEnumerable<OptimizedEntityGroup<T>> optimizedGroups,
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false); // Placeholder for async operations

            var plan = new ProcessingPlan<T>
            {
                ExecutionStages = new List<ProcessingStage<T>>()
            };

            var processedGroups = new HashSet<string>();
            var remainingGroups = optimizedGroups.ToList();

            var stageNumber = 1;
            while (remainingGroups.Any() && !cancellationToken.IsCancellationRequested)
            {
                // Find groups that can be processed in parallel (no unmet dependencies)
                var availableGroups = remainingGroups
                    .Where(g => g.Dependencies == null || 
                               g.Dependencies.All(dep => processedGroups.Contains(dep)))
                    .ToList();

                if (!availableGroups.Any())
                {
                    // Handle circular dependencies or missing dependencies
                    _logger.LogWarning("Breaking dependency deadlock in processing plan");
                    availableGroups = remainingGroups.Take(1).ToList();
                }

                // Group available items by compatibility for parallel processing
                var parallelGroups = GroupByParallelCompatibility(availableGroups);

                var stage = new ProcessingStage<T>
                {
                    StageNumber = stageNumber++,
                    ParallelGroups = parallelGroups.ToList(),
                    EstimatedDuration = CalculateStageEstimatedDuration(parallelGroups),
                    MemoryRequirement = CalculateStageMemoryRequirement(parallelGroups)
                };

                plan.ExecutionStages.Add(stage);

                // Mark groups as processed and remove from remaining
                foreach (var group in availableGroups)
                {
                    processedGroups.Add(group.EntityType);
                    remainingGroups.Remove(group);
                }
            }

            plan.TotalEstimatedDuration = plan.ExecutionStages.Sum(s => s.EstimatedDuration);
            plan.PeakMemoryRequirement = plan.ExecutionStages.Max(s => s.MemoryRequirement);

            _logger.LogInformation("Created processing plan with {StageCount} stages, " +
                "estimated duration: {Duration:F1}s, peak memory: {Memory:F1}MB",
                plan.ExecutionStages.Count, plan.TotalEstimatedDuration, 
                plan.PeakMemoryRequirement / 1024.0 / 1024.0);

            return plan;
        }

        /// <summary>
        /// Executes the intelligent processing plan with adaptive optimization
        /// </summary>
        private async Task ExecuteProcessingPlanAsync<T>(
            ProcessingPlan<T> plan,
            IDictionary<string, Func<IEnumerable<T>, Task<ProcessingResult>>> processingStrategies,
            IntelligentBulkResults results,
            CancellationToken cancellationToken)
        {
            foreach (var stage in plan.ExecutionStages)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _logger.LogInformation("Executing processing stage {StageNumber} with {GroupCount} parallel groups",
                    stage.StageNumber, stage.ParallelGroups.Count);

                var stageStartTime = DateTime.UtcNow;
                var stageTasks = new List<Task<GroupProcessingResult>>();

                // Execute parallel groups concurrently
                foreach (var parallelGroup in stage.ParallelGroups)
                {
                    foreach (var group in parallelGroup)
                    {
                        if (!processingStrategies.TryGetValue(group.EntityType, out var strategy))
                        {
                            _logger.LogWarning("No processing strategy found for entity type: {EntityType}", 
                                group.EntityType);
                            continue;
                        }

                        var groupTask = ProcessGroupWithAdaptiveOptimizationAsync(
                            group, strategy, cancellationToken);
                        stageTasks.Add(groupTask);
                    }
                }

                // Wait for all parallel groups to complete
                var stageResults = await Task.WhenAll(stageTasks).ConfigureAwait(false);
                var stageEndTime = DateTime.UtcNow;

                // Record stage results
                foreach (var groupResult in stageResults)
                {
                    groupResult.StageNumber = stage.StageNumber;
                    groupResult.StageStartTime = stageStartTime;
                    groupResult.StageEndTime = stageEndTime;
                    results.GroupResults.Add(groupResult);
                }

                _logger.LogDebug("Completed stage {StageNumber} in {Duration:F1}s", 
                    stage.StageNumber, (stageEndTime - stageStartTime).TotalSeconds);
            }
        }

        /// <summary>
        /// Processes a group with adaptive optimization based on performance feedback
        /// </summary>
        private async Task<GroupProcessingResult> ProcessGroupWithAdaptiveOptimizationAsync<T>(
            OptimizedEntityGroup<T> group,
            Func<IEnumerable<T>, Task<ProcessingResult>> processingStrategy,
            CancellationToken cancellationToken)
        {
            var groupStartTime = DateTime.UtcNow;
            var entityList = group.Entities.ToList();

            _logger.LogDebug("Processing group {EntityType} with {EntityCount} entities using adaptive optimization",
                group.EntityType, entityList.Count);

            var result = new GroupProcessingResult
            {
                EntityType = group.EntityType,
                TotalEntities = entityList.Count,
                StartTime = groupStartTime
            };

            try
            {
                // Get optimal concurrency for this group
                var currentPerformance = new SystemPerformanceMetrics
                {
                    AverageLatencyMs = 100, // Default values - would come from monitoring
                    MemoryPressure = 0.5,
                    CpuUsage = 0.6,
                    ErrorRate = 0.01,
                    CurrentConcurrency = 4
                };
                var optimalConcurrency = await _concurrencyController.GetOptimalConcurrencyAsync(
                    group.EntityType, currentPerformance).ConfigureAwait(false);

                // Create batches based on optimal batch size and concurrency
                var batches = CreateOptimizedBatches(entityList, group.OptimalBatchSize);
                var semaphore = new SemaphoreSlim(optimalConcurrency, optimalConcurrency);
                var batchTasks = new List<Task<ProcessingResult>>();

                foreach (var batch in batches)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var batchTask = ProcessBatchWithConcurrencyControlAsync(
                        batch, processingStrategy, semaphore, cancellationToken);
                    batchTasks.Add(batchTask);
                }

                var batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);

                // Aggregate batch results
                result.ProcessedEntities = batchResults.Sum(r => r.ProcessedCount);
                result.FailedEntities = batchResults.Sum(r => r.FailedCount);
                result.ProcessedBatches = batchResults.Length;
                result.SuccessfulBatches = batchResults.Count(r => r.IsSuccess);

                // Record performance feedback for adaptive optimization
                var avgProcessingTime = batchResults.Average(r => r.ProcessingTime.TotalMilliseconds);
                var avgThroughput = batchResults.Average(r => r.Throughput);
                var avgErrorRate = batchResults.Average(r => r.ErrorRate);

                await _concurrencyController.RecordPerformanceAsync(
                    group.EntityType, 
                    optimalConcurrency, 
                    TimeSpan.FromMilliseconds(avgProcessingTime),
                    batchResults.Sum(r => r.MemoryUsedMB),
                    avgErrorRate < 5.0).ConfigureAwait(false);

                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                result.EfficiencyGain = CalculateGroupEfficiencyGain(result, entityList.Count);

                _logger.LogDebug("Completed group {EntityType}: {ProcessedEntities}/{TotalEntities} entities, " +
                    "efficiency: {Efficiency:F1}%", group.EntityType, result.ProcessedEntities, 
                    result.TotalEntities, result.EfficiencyGain);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing group {EntityType}", group.EntityType);
                
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                result.HasError = true;
                result.ErrorMessage = ex.Message;
                
                return result;
            }
        }

        /// <summary>
        /// Processes a batch with concurrency control
        /// </summary>
        private async Task<ProcessingResult> ProcessBatchWithConcurrencyControlAsync<T>(
            IEnumerable<T> batch,
            Func<IEnumerable<T>, Task<ProcessingResult>> processingStrategy,
            SemaphoreSlim semaphore,
            CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await processingStrategy(batch).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Analyzes entity characteristics for optimal batching strategies
        /// </summary>
        private EntityCharacteristics AnalyzeEntityCharacteristics<T>(IList<T> entities, string entityType)
        {
            return new EntityCharacteristics
            {
                EntityType = entityType,
                TotalCount = entities.Count,
                Complexity = DetermineEntityComplexity(entityType),
                EstimatedSizePerEntity = EstimateEntitySize(entityType),
                HasDependencies = CheckForDependencies(entityType),
                IsTimeSeriesData = CheckIfTimeSeriesData(entityType),
                RequiresOrdering = CheckIfOrderingRequired(entityType)
            };
        }

        /// <summary>
        /// Calculates optimal batch configuration based on entity characteristics
        /// </summary>
        private async Task<BatchConfiguration> CalculateOptimalBatchConfigurationAsync(
            EntityCharacteristics characteristics,
            CancellationToken cancellationToken)
        {
            var optimalBatchSize = await _batchSizeCalculator.CalculateOptimalBatchSizeAsync(
                characteristics.EntityType, 
                characteristics.Complexity, 
                characteristics.EstimatedSizePerEntity).ConfigureAwait(false);

            var currentPerformance = new SystemPerformanceMetrics
            {
                AverageLatencyMs = 100,
                MemoryPressure = 0.5,
                CpuUsage = 0.6,
                ErrorRate = 0.01,
                CurrentConcurrency = 4
            };
            var optimalConcurrency = await _concurrencyController.GetOptimalConcurrencyAsync(
                characteristics.EntityType, currentPerformance).ConfigureAwait(false);

            return new BatchConfiguration
            {
                OptimalBatchSize = optimalBatchSize,
                OptimalConcurrency = optimalConcurrency,
                UseAdaptiveSizing = characteristics.TotalCount > 1000,
                PreserveOrdering = characteristics.RequiresOrdering,
                EnableDependencyTracking = characteristics.HasDependencies,
                MemoryOptimizedMode = characteristics.EstimatedSizePerEntity > 10 * 1024 // >10KB per entity
            };
        }

        /// <summary>
        /// Creates smart batches based on characteristics and configuration
        /// </summary>
        private IEnumerable<SmartBatch<T>> CreateSmartBatches<T>(IList<T> entities, BatchConfiguration config)
        {
            var batches = new List<SmartBatch<T>>();
            var batchId = 1;

            for (int i = 0; i < entities.Count; i += config.OptimalBatchSize)
            {
                var batchEntities = entities.Skip(i).Take(config.OptimalBatchSize).ToList();
                
                batches.Add(new SmartBatch<T>
                {
                    BatchId = batchId++,
                    Entities = batchEntities,
                    BatchSize = batchEntities.Count,
                    EstimatedProcessingTime = TimeSpan.FromMilliseconds(batchEntities.Count * 100), // 100ms per entity estimate
                    Priority = CalculateBatchPriority(batchEntities, i, entities.Count),
                    Configuration = config
                });
            }

            return batches;
        }

        /// <summary>
        /// Processes smart batches with adaptive optimization
        /// </summary>
        private async Task ProcessSmartBatchesAsync<T>(
            IEnumerable<SmartBatch<T>> smartBatches,
            Func<IEnumerable<T>, Task<ProcessingResult>> processingFunction,
            SmartBatchingResults results,
            CancellationToken cancellationToken)
        {
            var batches = smartBatches.OrderBy(b => b.Priority).ToList();
            var semaphore = new SemaphoreSlim(batches.First().Configuration.OptimalConcurrency, 
                batches.First().Configuration.OptimalConcurrency);

            var batchTasks = batches.Select(batch => 
                ProcessSmartBatchAsync(batch, processingFunction, semaphore, cancellationToken));

            var batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);

            // Aggregate results
            results.ProcessedBatches = batchResults.Length;
            results.SuccessfulBatches = batchResults.Count(r => r.IsSuccess);
            results.ProcessedEntities = batchResults.Sum(r => r.ProcessedCount);
            results.FailedEntities = batchResults.Sum(r => r.FailedCount);
            results.TotalProcessingTime = TimeSpan.FromMilliseconds(
                batchResults.Sum(r => r.ProcessingTime.TotalMilliseconds));
        }

        /// <summary>
        /// Processes a single smart batch with monitoring
        /// </summary>
        private async Task<ProcessingResult> ProcessSmartBatchAsync<T>(
            SmartBatch<T> batch,
            Func<IEnumerable<T>, Task<ProcessingResult>> processingFunction,
            SemaphoreSlim semaphore,
            CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var startTime = DateTime.UtcNow;
                var result = await processingFunction(batch.Entities).ConfigureAwait(false);
                var endTime = DateTime.UtcNow;

                result.ProcessingTime = endTime - startTime;
                result.BatchId = batch.BatchId;
                result.Throughput = batch.BatchSize / result.ProcessingTime.TotalSeconds;

                return result;
            }
            finally
            {
                semaphore.Release();
            }
        }

        // Helper methods for entity analysis and optimization
        private EntityComplexity DetermineEntityComplexity(string entityType) =>
            entityType.ToLowerInvariant() switch
            {
                "categories" or "brands" => EntityComplexity.Low,
                "variants" or "modifiers" => EntityComplexity.Medium,
                "products" or "images" => EntityComplexity.High,
                _ => EntityComplexity.Medium
            };

        private int EstimateEntitySize(string entityType) =>
            entityType.ToLowerInvariant() switch
            {
                "categories" => 5 * 1024,   // 5KB
                "brands" => 3 * 1024,       // 3KB
                "variants" => 15 * 1024,    // 15KB
                "products" => 50 * 1024,    // 50KB
                "images" => 30 * 1024,      // 30KB
                _ => 10 * 1024               // 10KB default
            };

        private double CalculateProcessingWeight<T>(EntityGroup<T> group, EntityComplexity complexity)
        {
            var baseWeight = group.Entities.Count() * (double)group.EstimatedEntitySize;
            var complexityMultiplier = complexity switch
            {
                EntityComplexity.Low => 1.0,
                EntityComplexity.Medium => 1.5,
                EntityComplexity.High => 2.0,
                _ => 1.0
            };
            return baseWeight * complexityMultiplier;
        }

        private bool CheckForDependencies(string entityType) =>
            entityType.ToLowerInvariant() is "variants" or "images" or "modifiers";

        private bool CheckIfTimeSeriesData(string entityType) => false; // Most BigCommerce entities are not time-series

        private bool CheckIfOrderingRequired(string entityType) =>
            entityType.ToLowerInvariant() is "categories"; // Categories may have hierarchical ordering

        private IEnumerable<IEnumerable<OptimizedEntityGroup<T>>> GroupByParallelCompatibility<T>(
            IList<OptimizedEntityGroup<T>> groups)
        {
            // Group entities that can be processed in parallel (no conflicts or dependencies)
            return new[] { groups }; // Simplified - in reality would check for conflicts
        }

        private double CalculateStageEstimatedDuration<T>(IEnumerable<IEnumerable<OptimizedEntityGroup<T>>> parallelGroups)
        {
            return parallelGroups.Max(groups => groups.Sum(g => g.Entities.Count() * 0.1)); // 100ms per entity estimate
        }

        private long CalculateStageMemoryRequirement<T>(IEnumerable<IEnumerable<OptimizedEntityGroup<T>>> parallelGroups)
        {
            return parallelGroups.Max(groups => groups.Sum(g => g.Entities.Count() * g.EstimatedEntitySize));
        }

        private IEnumerable<IEnumerable<T>> CreateOptimizedBatches<T>(IList<T> entities, int batchSize)
        {
            for (int i = 0; i < entities.Count; i += batchSize)
            {
                yield return entities.Skip(i).Take(batchSize);
            }
        }

        private int CalculateBatchPriority<T>(IList<T> batchEntities, int startIndex, int totalCount)
        {
            // Higher priority for earlier batches and smaller batches
            var positionWeight = (double)startIndex / totalCount;
            var sizeWeight = (double)batchEntities.Count / 100; // Normalize to reasonable range
            return (int)((1.0 - positionWeight) * 100 + (1.0 - sizeWeight) * 10);
        }

        private double CalculateGroupEfficiencyGain(GroupProcessingResult result, int originalEntityCount)
        {
            if (result.ProcessedBatches == 0) return 0;
            
            // Calculate efficiency gain: individual operations vs batch operations
            var individualOperations = originalEntityCount;
            var batchOperations = result.ProcessedBatches;
            
            return ((double)(individualOperations - batchOperations) / individualOperations) * 100;
        }

        private void CalculateIntelligentEfficiencyMetrics(IntelligentBulkResults results)
        {
            if (!results.GroupResults.Any()) return;

            results.TotalEntitiesProcessed = results.GroupResults.Sum(r => r.ProcessedEntities);
            results.TotalBatchesProcessed = results.GroupResults.Sum(r => r.ProcessedBatches);
            results.OverallSuccessRate = results.GroupResults.Average(r => 
                (double)r.ProcessedEntities / r.TotalEntities * 100);
            
            // Calculate overall efficiency gain
            var totalOriginalOperations = results.TotalEntitiesProcessed;
            var totalBatchOperations = results.TotalBatchesProcessed;
            results.OverallEfficiencyGain = totalBatchOperations > 0 
                ? ((double)(totalOriginalOperations - totalBatchOperations) / totalOriginalOperations) * 100 
                : 0;

            results.AverageProcessingTimePerEntity = results.GroupResults
                .Where(r => r.ProcessedEntities > 0)
                .Average(r => r.Duration.TotalMilliseconds / r.ProcessedEntities);
        }

        private void CalculateSmartBatchingMetrics(SmartBatchingResults results, int originalEntityCount)
        {
            if (results.ProcessedBatches == 0) return;

            results.EfficiencyGain = ((double)(originalEntityCount - results.ProcessedBatches) / originalEntityCount) * 100;
            results.SuccessRate = (double)results.SuccessfulBatches / results.ProcessedBatches * 100;
            results.ThroughputPerSecond = results.ProcessedEntities / results.Duration.TotalSeconds;
            results.AverageProcessingTimePerBatch = results.TotalProcessingTime.TotalMilliseconds / results.ProcessedBatches;
        }
    }
} 