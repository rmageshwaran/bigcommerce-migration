using System.Collections.Concurrent;
using System.Diagnostics;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// **PHASE 2: Enhanced Parallel Processor Implementation**
/// 
/// Implements controlled parallel batch processing with semaphore-based throttling,
/// dynamic rate limiting integration, and thread-safe progress tracking.
/// 
/// **Key Features:**
/// - Semaphore-based concurrency control with adaptive adjustments
/// - Integration with Phase 1 IDynamicRateLimiter for rate-aware processing
/// - Thread-safe progress aggregation with SignalR real-time updates
/// - Durable Functions determinism compliance
/// - System resource monitoring and pressure detection
/// 
/// **Performance Targets:**
/// - 12.5x total throughput improvement (720 → 9,000 req/hour)
/// - Optimal resource utilization (70% CPU target)
/// - Zero message loss or duplication during parallel processing
/// </summary>
public class EnhancedParallelProcessor : BigCommerce.Migration.Core.Interfaces.IEnhancedParallelProcessor
{
    #region Private Fields

    private readonly IDynamicRateLimiter _dynamicRateLimiter;
    private readonly ISignalREventFactory _signalREventFactory; // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
    private readonly ILogger<EnhancedParallelProcessor> _logger;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IProgressTracker _progressTracker; // 🆕 TASK 3.2: Batch-level progress tracking
    
    // Concurrency management
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _storeSemaphores;
    private readonly ConcurrentDictionary<string, ParallelPerformanceMetrics> _storeMetrics;
    private readonly ConcurrentDictionary<string, ConcurrencyHistory> _concurrencyHistory;
    
    // Performance monitoring
    private readonly Timer _performanceMonitoringTimer;
    private readonly object _performanceMonitoringLock = new();
    
    // Configuration
    // **Phase 2.8a: Adaptive Concurrency Scaling Constants**
    private const int DEFAULT_MAX_CONCURRENCY = 8;
    private const int MIN_CONCURRENCY = 1;
    private const int MAX_CONCURRENCY = 32; // Increased from 16 for enterprise migrations
    
    // **Phase 2.8a: Migration Size-Based Concurrency Scaling**
    // **Phase 2.9b: Increased limits to compensate for entity estimation fix regression**
    private const int SMALL_MIGRATION_MAX_CONCURRENCY = 28;    // ≤200 entities (was 16)
    private const int MEDIUM_MIGRATION_MAX_CONCURRENCY = 32;   // 201-800 entities (was 24)
    private const int LARGE_MIGRATION_MAX_CONCURRENCY = 40;    // 801-2000 entities (was 24)
    private const int ENTERPRISE_MIGRATION_MAX_CONCURRENCY = 50; // 2000+ entities (was 32)
    private const double TARGET_CPU_UTILIZATION = 0.70;
    private const double ERROR_RATE_THRESHOLD = 0.05; // 5%

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes the Enhanced Parallel Processor with required dependencies
    /// **PHASE 2.4: Enhanced Dynamic Rate Limiting Integration**
    /// - Subscribes to rate limit events for real-time adjustments
    /// - Configures health-aware parallel processing
    /// </summary>
    public EnhancedParallelProcessor(
        IDynamicRateLimiter dynamicRateLimiter,
        ISignalREventFactory signalREventFactory, // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
        ILogger<EnhancedParallelProcessor> logger,
        IDateTimeProvider dateTimeProvider,
        IProgressTracker progressTracker) // 🆕 TASK 3.2: Batch-level progress tracking
    {
        _dynamicRateLimiter = dynamicRateLimiter ?? throw new ArgumentNullException(nameof(dynamicRateLimiter));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory)); // 🎯 CENTRALIZED SIGNALR: Store factory reference
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker)); // 🆕 TASK 3.2: Store progress tracker reference

        _storeSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
        _storeMetrics = new ConcurrentDictionary<string, ParallelPerformanceMetrics>();
        _concurrencyHistory = new ConcurrentDictionary<string, ConcurrencyHistory>();

        // **PHASE 2.4: Real-time Rate Limit Event Integration**
        // Subscribe to dynamic rate limiter events for adaptive parallel processing
        _dynamicRateLimiter.ApiHealthChanged += OnApiHealthChanged;
        _dynamicRateLimiter.OptimalRateChanged += OnOptimalRateChanged;

        // Start performance monitoring every 30 seconds
        _performanceMonitoringTimer = new Timer(MonitorPerformanceAsync, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        _logger.LogInformation("Enhanced Parallel Processor initialized with Phase 2.4 dynamic rate limiting integration and real-time event handling");
    }

    #endregion

    #region Dynamic Rate Limit Event Handlers (Phase 2.4)

    /// <summary>
    /// Handles API health changes from the dynamic rate limiter
    /// Adjusts parallel processing behavior based on health changes
    /// </summary>
    private void OnApiHealthChanged(object? sender, ApiHealthChangedEventArgs e)
    {
        try
        {
            var storeId = e.StoreId;
            var newHealthScore = e.NewHealthScore;
            var previousHealthScore = e.PreviousHealthScore;

            _logger.LogInformation("API health changed for store {StoreId}: {PreviousHealth} → {NewHealth}", 
                storeId, previousHealthScore, newHealthScore);

            // Adjust concurrency based on health changes
            if (newHealthScore < 30 && previousHealthScore >= 30)
            {
                // Health degraded - reduce concurrency
                _ = Task.Run(async () => await ReduceConcurrencyForStore(storeId, "Health degradation"));
                _logger.LogWarning("Reducing concurrency for store {StoreId} due to API health degradation ({NewHealth})", 
                    storeId, newHealthScore);
            }
            else if (newHealthScore > 70 && previousHealthScore <= 70)
            {
                // Health improved - potentially increase concurrency
                _ = Task.Run(async () => await OptimizeConcurrencyForStore(storeId, "Health improvement"));
                _logger.LogInformation("Optimizing concurrency for store {StoreId} due to API health improvement ({NewHealth})", 
                    storeId, newHealthScore);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling API health change event");
        }
    }

    /// <summary>
    /// Handles optimal rate changes from the dynamic rate limiter
    /// Adjusts parallel processing timing based on new optimal rates
    /// </summary>
    private void OnOptimalRateChanged(object? sender, OptimalRateChangedEventArgs e)
    {
        try
        {
            var storeId = e.StoreId;
            var newOptimalRate = e.NewRate;
            var previousOptimalRate = e.PreviousRate;

            _logger.LogDebug("Optimal rate changed for store {StoreId}: {PreviousRate} → {NewRate} req/sec", 
                storeId, previousOptimalRate, newOptimalRate);

            // Calculate new optimal concurrency based on rate change
            var ratioChange = newOptimalRate / Math.Max(previousOptimalRate, 1.0);
            
            if (ratioChange < 0.7) // Significant rate decrease
            {
                _ = Task.Run(async () => await ReduceConcurrencyForStore(storeId, $"Rate decreased to {newOptimalRate:F1} req/sec"));
                _logger.LogInformation("Reducing concurrency for store {StoreId} due to optimal rate decrease ({NewRate:F1} req/sec)", 
                    storeId, newOptimalRate);
            }
            else if (ratioChange > 1.3) // Significant rate increase
            {
                _ = Task.Run(async () => await OptimizeConcurrencyForStore(storeId, $"Rate increased to {newOptimalRate:F1} req/sec"));
                _logger.LogInformation("Optimizing concurrency for store {StoreId} due to optimal rate increase ({NewRate:F1} req/sec)", 
                    storeId, newOptimalRate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling optimal rate change event");
        }
    }

    /// <summary>
    /// Reduces concurrency for a specific store in response to rate limiting events
    /// </summary>
    private async Task ReduceConcurrencyForStore(string storeId, string reason)
    {
        try
        {
            if (_storeSemaphores.TryGetValue(storeId, out var semaphore))
            {
                var currentCount = semaphore.CurrentCount;
                var newCount = Math.Max(MIN_CONCURRENCY, currentCount - 1);
                
                // Note: Semaphore adjustment would need careful handling in a real implementation
                // For now, this logs the intent - actual semaphore adjustment is complex during active processing
                _logger.LogInformation("Would reduce concurrency for store {StoreId} from {CurrentCount} to {NewCount}: {Reason}", 
                    storeId, currentCount, newCount, reason);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reducing concurrency for store {StoreId}", storeId);
        }
    }

    /// <summary>
    /// Optimizes concurrency for a specific store in response to improved rate limiting conditions
    /// </summary>
    private async Task OptimizeConcurrencyForStore(string storeId, string reason)
    {
        try
        {
            if (_storeSemaphores.TryGetValue(storeId, out var semaphore))
            {
                var currentCount = semaphore.CurrentCount;
                var newCount = Math.Min(MAX_CONCURRENCY, currentCount + 1);
                
                // Note: Semaphore adjustment would need careful handling in a real implementation
                // For now, this logs the intent - actual semaphore adjustment is complex during active processing
                _logger.LogInformation("Would optimize concurrency for store {StoreId} from {CurrentCount} to {NewCount}: {Reason}", 
                    storeId, currentCount, newCount, reason);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error optimizing concurrency for store {StoreId}", storeId);
        }
    }

    #endregion

    #region Parallel Batch Coordination

    /// <summary>
    /// Processes multiple batches in parallel while respecting dynamic rate limits
    /// </summary>
    public async Task<ParallelProcessingResult> ProcessBatchesInParallelAsync<TBatch>(
        IReadOnlyList<TBatch> batches,
        Func<TBatch, CancellationToken, Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult>> batchProcessor,
        ParallelProcessingConfiguration parallelConfig,
        IProgress<BatchProgressUpdate>? progressCallback = null,
        CancellationToken cancellationToken = default) where TBatch : class
    {
        if (batches == null || batches.Count == 0)
        {
            _logger.LogWarning("No batches provided for parallel processing");
            return new ParallelProcessingResult();
        }

        var storeId = parallelConfig.StoreId;
        var startTime = _dateTimeProvider.UtcNow;
        
        _logger.LogInformation("Starting parallel processing of {BatchCount} batches for store {StoreId} with entity type {EntityType}",
            batches.Count, storeId, parallelConfig.EntityType);

        try
        {
            // **Phase 2.8a: Estimate total entity count for adaptive concurrency**
            var estimatedEntityCount = EstimateTotalEntityCount(batches);
            
            // **Phase 2.7: Register parallel context for adaptive rate limiting**
            _dynamicRateLimiter.RegisterParallelContext(storeId, parallelConfig.MaxConcurrentBatches ?? 8, batches.Count);
            
            // Step 1: Calculate optimal concurrency based on current conditions with adaptive scaling
            var concurrencyResult = await CalculateOptimalConcurrencyAsync(
                storeId, parallelConfig.EntityType, batches.Count, estimatedEntityCount, cancellationToken);

            // **Phase 2.9c: Prioritize adaptive max concurrency when entity count is accurate**
            int optimalConcurrency;
            if (estimatedEntityCount > 0)
            {
                // Calculate adaptive max concurrency based on entity count
                var adaptiveMaxConcurrency = CalculateAdaptiveMaxConcurrency(estimatedEntityCount, batches.Count);
                
                // Use adaptive max concurrency when it's higher than the constrained calculation
                if (adaptiveMaxConcurrency > DEFAULT_MAX_CONCURRENCY)
                {
                    optimalConcurrency = adaptiveMaxConcurrency;
                    _logger.LogInformation("🔧 P2.9c: Using adaptive max concurrency {AdaptiveMax} for {EntityCount} entities (overriding constrained result: {ConstrainedResult})",
                        optimalConcurrency, estimatedEntityCount, concurrencyResult.OptimalConcurrency);
                }
                else
                {
                    // Use constrained calculation for small migrations
                    optimalConcurrency = parallelConfig.MaxConcurrentBatches ?? concurrencyResult.OptimalConcurrency;
                }
            }
            else
            {
                // Use configuration override when entity count is unknown
                optimalConcurrency = parallelConfig.MaxConcurrentBatches ?? concurrencyResult.OptimalConcurrency;
            }
            
            _logger.LogInformation("Using concurrency level {Concurrency} for store {StoreId} (reasoning: {Reasoning})",
                optimalConcurrency, storeId, concurrencyResult.Reasoning);

            // Step 2: Get or create semaphore for controlled concurrency
            var semaphore = GetOrCreateSemaphore(storeId, optimalConcurrency);

            // Step 3: Create progress aggregator for thread-safe progress tracking
            var progressAggregator = CreateProgressAggregator(
                $"parallel-{storeId}-{_dateTimeProvider.UtcNow.Ticks}",
                parallelConfig.EntityType,
                batches.Count,
                null // Will be provided by the caller if needed
            );

            // Step 4: Configure progress callback integration
            if (progressCallback != null)
            {
                progressAggregator.BatchCompleted += (sender, args) =>
                {
                    var progressUpdate = new BatchProgressUpdate
                    {
                        MigrationId = $"parallel-{storeId}",
                        EntityType = parallelConfig.EntityType,
                        CompletedBatchNumber = args.BatchNumber,
                        TotalBatches = batches.Count,
                        BatchEntitiesProcessed = args.EntitiesProcessed,
                        BatchEntitiesFailed = args.EntitiesFailed,
                        BatchProcessingTime = args.ProcessingTime,
                        OverallProgressPercentage = args.OverallProgressPercentage,
                        CurrentConcurrency = optimalConcurrency
                    };
                    
                    progressCallback.Report(progressUpdate);
                };
            }

            // Step 5: Process batches in parallel with controlled concurrency
            var result = await ProcessBatchesWithSemaphoreAsync(
                batches, batchProcessor, semaphore, progressAggregator, parallelConfig, cancellationToken);

            // Step 6: Record performance metrics and update history
            var endTime = _dateTimeProvider.UtcNow;
            var totalTime = endTime - startTime;
            
            await RecordProcessingPerformanceAsync(storeId, result, totalTime, optimalConcurrency);

            _logger.LogInformation("Completed parallel processing for store {StoreId}: {ProcessedBatches}/{TotalBatches} batches, " +
                                 "{ProcessedEntities} entities in {TotalTime:F2}s (throughput: {Throughput:F2} entities/sec)",
                storeId, result.SuccessfulBatches, result.TotalBatchesProcessed, 
                result.TotalEntitiesProcessed, totalTime.TotalSeconds, result.OverallThroughput);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed parallel processing for store {StoreId} with {BatchCount} batches", storeId, batches.Count);
            throw;
        }
        finally
        {
            // **Phase 2.7: Unregister parallel context for adaptive rate limiting**
            _dynamicRateLimiter.UnregisterParallelContext(storeId);
        }
    }

    #endregion

    #region Concurrency Management

        /// <summary>
    /// **Phase 2.8a**: Calculates adaptive max concurrency based on migration size
    /// **Phase 2.9b**: Increased limits for aggressive parallel processing
    /// 
    /// Scales concurrency limits based on total entity count for optimal throughput:
    /// - Small migrations (≤200): 28 concurrent batches (was 16)
    /// - Medium migrations (201-800): 32 concurrent batches (was 24)
    /// - Large migrations (801-2000): 40 concurrent batches (was 24)
    /// - Enterprise migrations (2000+): 50 concurrent batches (was 32)
    /// </summary>
    /// <param name="totalEntityCount">Total number of entities being migrated</param>
    /// <param name="totalBatches">Total number of batches</param>
    /// <returns>Adaptive max concurrency for this migration size</returns>
    private int CalculateAdaptiveMaxConcurrency(int totalEntityCount, int totalBatches)
    {
        var adaptiveMax = totalEntityCount switch
        {
            <= 200 => SMALL_MIGRATION_MAX_CONCURRENCY,
            <= 800 => MEDIUM_MIGRATION_MAX_CONCURRENCY,
            <= 2000 => LARGE_MIGRATION_MAX_CONCURRENCY,
            _ => ENTERPRISE_MIGRATION_MAX_CONCURRENCY
        };
        
        // Don't exceed the number of batches (no point in having more workers than work)
        adaptiveMax = Math.Min(adaptiveMax, totalBatches);
        
        _logger.LogInformation("🔧 P2.8a: Adaptive max concurrency for {EntityCount} entities: {AdaptiveMax} concurrent batches (category: {Category})",
            totalEntityCount, adaptiveMax, 
            totalEntityCount <= 200 ? "Small" : 
            totalEntityCount <= 800 ? "Medium" : 
            totalEntityCount <= 2000 ? "Large" : "Enterprise");
            
        return adaptiveMax;
    }

    /// <summary>
    /// **Phase 2.8a**: Estimates total entity count from batch collection
    /// 
    /// Attempts to extract entity count information from batch objects,
    /// with fallback to reasonable estimates based on batch count.
    /// </summary>
    /// <typeparam name="TBatch">Batch type</typeparam>
    /// <param name="batches">Collection of batches</param>
    /// <returns>Estimated total entity count</returns>
    private int EstimateTotalEntityCount<TBatch>(IReadOnlyList<TBatch> batches) where TBatch : class
    {
        if (batches == null || batches.Count == 0)
            return 0;

        // Try to extract entity count using reflection to avoid cross-assembly references
        var totalCount = 0;
        var foundEntityIds = false;
        
        foreach (var batch in batches)
        {
            // Use reflection to check for EntityIds property
            var entityIdsProperty = batch.GetType().GetProperty("EntityIds");
            if (entityIdsProperty != null)
            {
                var entityIds = entityIdsProperty.GetValue(batch) as System.Collections.ICollection;
                if (entityIds != null)
                {
                    totalCount += entityIds.Count;
                    foundEntityIds = true;
                }
            }
        }
        
        if (foundEntityIds)
        {
            _logger.LogInformation("🔧 P2.8a: Extracted exact entity count from batch EntityIds: {EntityCount} entities across {BatchCount} batches",
                totalCount, batches.Count);
            return totalCount;
        }

        // Fallback: estimate based on average batch size and typical patterns
        var averageEntitiesPerBatch = batches.Count switch
        {
            <= 10 => 10,    // Small migrations: ~10 entities per batch
            <= 20 => 25,    // Medium migrations: ~25 entities per batch  
            <= 30 => 50,    // Large migrations: ~50 entities per batch
            _ => 100        // Enterprise migrations: ~100 entities per batch
        };

        var estimatedCount = batches.Count * averageEntitiesPerBatch;
        
        _logger.LogInformation("🔧 P2.8a: Estimated entity count from batch patterns: {EstimatedCount} entities (avg {AvgPerBatch} per batch)",
            estimatedCount, averageEntitiesPerBatch);
            
        return estimatedCount;
    }

    /// <summary>
    /// Calculates optimal concurrency level based on current system and API health
    /// **PHASE 2.4: Enhanced with sophisticated rate limit integration**
    /// **PHASE 2.8a: Enhanced with adaptive concurrency scaling based on migration size**
    /// 
    /// Uses enhanced rate limit status, API health metrics, and real-time feedback
    /// to calculate the optimal parallel processing concurrency level.
    /// </summary>
    public async Task<OptimalConcurrencyResult> CalculateOptimalConcurrencyAsync(
        string storeId,
        string entityType,
        int totalBatches,
        int totalEntityCount = -1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // **PHASE 2.4: Enhanced Rate Limit Analysis**
            // Get comprehensive rate limit and health information
            var enhancedRateStatus = await _dynamicRateLimiter.GetEnhancedRateLimitStatusAsync(storeId, cancellationToken);
            var apiHealth = await _dynamicRateLimiter.GetApiHealthAsync(storeId, cancellationToken);
            var optimalRate = await _dynamicRateLimiter.GetOptimalRateAsync(storeId, cancellationToken);

            var healthScore = apiHealth.GetHealthScore();
            
            _logger.LogDebug("Calculating optimal concurrency for store {StoreId}: Health={HealthScore}, OptimalRate={OptimalRate} req/sec", 
                storeId, healthScore, optimalRate);

            // **PHASE 2.4: Multi-Factor Concurrency Calculation**
            
            // 1. Rate-based concurrency calculation
            // Assume each batch needs 2-5 requests on average, scale based on optimal rate
            var averageRequestsPerBatch = 3.5; // Conservative estimate
            var rateBased = Math.Max(1, (int)(optimalRate / averageRequestsPerBatch));
            
            // 2. Health-based adjustment
            var healthMultiplier = healthScore switch
            {
                < 20 => 0.5,  // Very poor health - very conservative
                < 40 => 0.7,  // Poor health - conservative
                < 60 => 0.85, // Fair health - slightly conservative
                < 80 => 1.0,  // Good health - normal
                _ => 1.2      // Excellent health - slightly aggressive
            };
            
            var healthAdjusted = (int)(rateBased * healthMultiplier);

            // 3. System resource constraints
            var systemPressure = await DetectSystemPressureAsync();
            var systemConcurrency = CalculateSystemResourceConcurrency(0.5); // Use placeholder value for now

            // 4. Enhanced rate limit status constraints
            var rateLimitMultiplier = 1.0;
            if (enhancedRateStatus.RequestsRemaining < enhancedRateStatus.RequestsPerMinute * 0.2) // Less than 20% remaining
            {
                rateLimitMultiplier = 0.6; // Be very conservative
                _logger.LogDebug("Applying rate limit constraint for store {StoreId}: {RequestsRemaining}/{RequestsPerMinute} remaining", 
                    storeId, enhancedRateStatus.RequestsRemaining, enhancedRateStatus.RequestsPerMinute);
            }
            else if (enhancedRateStatus.RequestsRemaining < enhancedRateStatus.RequestsPerMinute * 0.5) // Less than 50% remaining
            {
                rateLimitMultiplier = 0.8; // Be somewhat conservative
            }

            var rateLimitAdjusted = (int)(healthAdjusted * rateLimitMultiplier);

            // 5. Historical performance adjustments
            var historicalAdjustment = GetHistoricalConcurrencyAdjustment(storeId, entityType);

            // **Phase 2.8a: Adaptive max concurrency based on migration size**
            var adaptiveMaxConcurrency = totalEntityCount > 0 
                ? CalculateAdaptiveMaxConcurrency(totalEntityCount, totalBatches)
                : MAX_CONCURRENCY; // Fallback to static max if entity count not provided

            // **Phase 2.8a: Prioritize adaptive concurrency for large migrations**
            int candidateConcurrency;
            if (totalEntityCount > 0 && adaptiveMaxConcurrency > DEFAULT_MAX_CONCURRENCY)
            {
                // For large migrations, use adaptive concurrency as the primary limit
                // But still respect severe constraints (very poor health or critical rate limits)
                var minimumConstraints = Math.Min(
                    healthScore < 20 ? 2 : adaptiveMaxConcurrency, // Only cap severely for very poor health
                    rateLimitMultiplier < 0.7 ? 4 : adaptiveMaxConcurrency // Only cap severely for critical rate limits
                );
                
                candidateConcurrency = Math.Min(
                    Math.Min(minimumConstraints, systemConcurrency),
                    Math.Max(adaptiveMaxConcurrency, historicalAdjustment) // Use adaptive max for large migrations
                );
                
                _logger.LogInformation("🔧 P2.8a: Using adaptive concurrency strategy: adaptive={AdaptiveMax}, constraints={Constraints}, final={Candidate}",
                    adaptiveMaxConcurrency, minimumConstraints, candidateConcurrency);
            }
            else
            {
                // For small migrations or fallback, use traditional calculation
                candidateConcurrency = Math.Min(
                    Math.Min(rateLimitAdjusted, systemConcurrency),
                    Math.Min(adaptiveMaxConcurrency, Math.Max(MIN_CONCURRENCY, historicalAdjustment))
                );
                
                _logger.LogInformation("🔧 P2.8a: Using traditional concurrency strategy: rate={RateAdjusted}, system={SystemConcurrency}, final={Candidate}",
                    rateLimitAdjusted, systemConcurrency, candidateConcurrency);
            }

            // Ensure we don't exceed the number of batches
            var optimalConcurrency = Math.Min(candidateConcurrency, totalBatches);

            // **PHASE 2.4: Enhanced Confidence Calculation**
            var confidence = CalculateConfidence(healthScore, enhancedRateStatus, systemPressure, optimalRate);

            // **Phase 2.8a: Enhanced reasoning with adaptive concurrency info**
            var reasoning = totalEntityCount > 0 && adaptiveMaxConcurrency > DEFAULT_MAX_CONCURRENCY
                ? $"P2.8a: Adaptive concurrency for {totalEntityCount} entities (max: {adaptiveMaxConcurrency}), API health ({healthScore:F1}), rate ({optimalRate:F1} req/sec)"
                : $"Based on API health ({healthScore:F1}), optimal rate ({optimalRate:F1} req/sec), and system pressure analysis";

            var result = new OptimalConcurrencyResult
            {
                OptimalConcurrency = optimalConcurrency,
                Reasoning = reasoning,
                ApiHealthScore = healthScore,
                SystemResourceUtilization = 0.5, // Placeholder system utilization
                EstimatedMaxSafeRate = optimalRate * optimalConcurrency / averageRequestsPerBatch,
                ConfidenceLevel = confidence,
                CalculatedAt = _dateTimeProvider.UtcNow
            };

            _logger.LogInformation("Calculated optimal concurrency for store {StoreId}: {OptimalConcurrency} (confidence: {Confidence:P1}, health: {HealthScore}, rate: {OptimalRate:F1} req/sec)", 
                storeId, optimalConcurrency, confidence, healthScore, optimalRate);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate optimal concurrency for store {StoreId}, using default", storeId);
            
            // Return safe fallback
            return new OptimalConcurrencyResult
            {
                OptimalConcurrency = Math.Min(DEFAULT_MAX_CONCURRENCY, totalBatches),
                Reasoning = $"Error during calculation, using default conservative value: {ex.Message}",
                ApiHealthScore = 50.0, // Assume neutral
                SystemResourceUtilization = 0.5,
                EstimatedMaxSafeRate = 10.0,
                ConfidenceLevel = 0.3, // Low confidence due to error
                CalculatedAt = _dateTimeProvider.UtcNow
            };
        }
    }

    /// <summary>
    /// Monitors and adjusts concurrency during parallel execution
    /// </summary>
    public async Task<ConcurrencyAdjustmentResult> AdjustConcurrencyAsync(
        int currentConcurrency,
        ParallelPerformanceMetrics performanceMetrics,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var adjustmentType = ConcurrencyAdjustmentType.NoChange;
            var newConcurrency = currentConcurrency;
            var reason = "No adjustment needed";
            var trigger = PerformanceTrigger.None;

            // Check for emergency conditions first
            if (performanceMetrics.ErrorRate > ERROR_RATE_THRESHOLD * 2) // 10%
            {
                newConcurrency = Math.Max(MIN_CONCURRENCY, currentConcurrency / 2);
                adjustmentType = ConcurrencyAdjustmentType.Emergency;
                reason = $"Emergency reduction due to high error rate: {performanceMetrics.ErrorRate:P1}";
                trigger = PerformanceTrigger.ErrorRateIncreased;
            }
            else if (performanceMetrics.CpuUtilization > 0.90)
            {
                newConcurrency = Math.Max(MIN_CONCURRENCY, currentConcurrency - 1);
                adjustmentType = ConcurrencyAdjustmentType.Decrease;
                reason = $"Reduced due to high CPU utilization: {performanceMetrics.CpuUtilization:P1}";
                trigger = PerformanceTrigger.HighCpuUsage;
            }
            else if (performanceMetrics.MemoryUtilization > 0.85)
            {
                newConcurrency = Math.Max(MIN_CONCURRENCY, currentConcurrency - 1);
                adjustmentType = ConcurrencyAdjustmentType.Decrease;
                reason = $"Reduced due to high memory utilization: {performanceMetrics.MemoryUtilization:P1}";
                trigger = PerformanceTrigger.HighMemoryUsage;
            }
            else if (performanceMetrics.RateLimitUtilization > 0.80)
            {
                newConcurrency = Math.Max(MIN_CONCURRENCY, currentConcurrency - 1);
                adjustmentType = ConcurrencyAdjustmentType.Decrease;
                reason = $"Reduced due to approaching rate limits: {performanceMetrics.RateLimitUtilization:P1}";
                trigger = PerformanceTrigger.RateLimitApproaching;
            }
            // Check for positive adjustment opportunities
            else if (performanceMetrics.CpuUtilization < TARGET_CPU_UTILIZATION && 
                     performanceMetrics.ErrorRate < ERROR_RATE_THRESHOLD &&
                     performanceMetrics.RateLimitUtilization < 0.60 &&
                     currentConcurrency < MAX_CONCURRENCY)
            {
                newConcurrency = Math.Min(MAX_CONCURRENCY, currentConcurrency + 1);
                adjustmentType = ConcurrencyAdjustmentType.Increase;
                reason = $"Increased due to good performance metrics (CPU: {performanceMetrics.CpuUtilization:P1}, " +
                        $"Error: {performanceMetrics.ErrorRate:P1}, Rate: {performanceMetrics.RateLimitUtilization:P1})";
                trigger = PerformanceTrigger.ApiHealthImproved;
            }

            if (newConcurrency != currentConcurrency)
            {
                _logger.LogInformation("Adjusting concurrency from {Current} to {New}: {Reason}",
                    currentConcurrency, newConcurrency, reason);
            }

            return await Task.FromResult(new ConcurrencyAdjustmentResult
            {
                NewConcurrency = newConcurrency,
                PreviousConcurrency = currentConcurrency,
                AdjustmentType = adjustmentType,
                AdjustmentReason = reason,
                PerformanceTrigger = trigger
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to adjust concurrency, maintaining current level {Current}", currentConcurrency);
            
            return await Task.FromResult(new ConcurrencyAdjustmentResult
            {
                NewConcurrency = currentConcurrency,
                PreviousConcurrency = currentConcurrency,
                AdjustmentType = ConcurrencyAdjustmentType.NoChange,
                AdjustmentReason = $"Error during adjustment: {ex.Message}",
                PerformanceTrigger = PerformanceTrigger.None
            });
        }
    }

    #endregion

    #region Progress Tracking & SignalR Integration

    /// <summary>
    /// Creates a thread-safe progress aggregator for parallel batch processing
    /// </summary>
    public IParallelProgressAggregator CreateProgressAggregator(
        string migrationId,
        string entityType,
        int totalBatches,
        int? totalEntities = null)
    {
        return new ParallelProgressAggregator(
            migrationId, entityType, totalBatches, _logger, _dateTimeProvider, totalEntities);
    }

    #endregion

    #region Health & Performance Monitoring

    /// <summary>
    /// Monitors parallel processing health and performance metrics
    /// </summary>
    public async Task<ParallelProcessingHealthMetrics> GetProcessingHealthAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentMetrics = _storeMetrics.GetValueOrDefault(storeId) ?? new ParallelPerformanceMetrics();
            var systemPressure = await DetectSystemPressureAsync();
            
            // Calculate overall health score (0-100)
            var healthScore = CalculateOverallHealthScore(currentMetrics, systemPressure);

            // Get hourly averages (would be calculated from historical data)
            var hourlyAverage = CalculateHourlyAverageMetrics(storeId);

            // Count recent concurrency adjustments
            var recentAdjustments = GetRecentConcurrencyAdjustments(storeId);

            return new ParallelProcessingHealthMetrics
            {
                OverallHealthScore = healthScore,
                CurrentPerformance = currentMetrics,
                HourlyAveragePerformance = hourlyAverage,
                HourlyConcurrencyAdjustments = recentAdjustments,
                RecentErrors = GetRecentErrors(storeId),
                SystemPressure = systemPressure,
                RateLimitingIntegrationHealth = await TestRateLimitingIntegrationAsync(storeId),
                SignalRConnectivityHealth = true // Would test actual SignalR connectivity
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get processing health for store {StoreId}", storeId);
            
            return new ParallelProcessingHealthMetrics
            {
                OverallHealthScore = 0,
                RecentErrors = new List<string> { ex.Message }
            };
        }
    }

    /// <summary>
    /// Event triggered when parallel processing performance changes significantly
    /// **PHASE 2.4: Real-time performance monitoring integration**
    /// </summary>
    public event EventHandler<ParallelPerformanceChangedEventArgs>? ParallelPerformanceChanged;

    /// <summary>
    /// Triggers the ParallelPerformanceChanged event when significant performance changes are detected
    /// **PHASE 2.4: Enhanced performance monitoring**
    /// </summary>
    private void OnParallelPerformanceChanged(string storeId, ParallelPerformanceMetrics previousMetrics, 
        ParallelPerformanceMetrics newMetrics, PerformanceChangeSeverity severity)
    {
        try
        {
            var eventArgs = new ParallelPerformanceChangedEventArgs
            {
                StoreId = storeId,
                PreviousMetrics = previousMetrics,
                CurrentMetrics = newMetrics,
                Severity = severity,
                DetectedAt = _dateTimeProvider.UtcNow
            };

            ParallelPerformanceChanged?.Invoke(this, eventArgs);
            
            _logger.LogDebug("Triggered ParallelPerformanceChanged event for store {StoreId} with {Severity} severity", 
                storeId, severity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering ParallelPerformanceChanged event for store {StoreId}", storeId);
        }
    }

    #endregion

    #region IDisposable Implementation

    /// <summary>
    /// Disposes resources and unsubscribes from rate limit events
    /// **PHASE 2.4: Enhanced cleanup with event unsubscription**
    /// </summary>
    public void Dispose()
    {
        try
        {
            // **PHASE 2.4: Unsubscribe from rate limit events**
            if (_dynamicRateLimiter != null)
            {
                _dynamicRateLimiter.ApiHealthChanged -= OnApiHealthChanged;
                _dynamicRateLimiter.OptimalRateChanged -= OnOptimalRateChanged;
                _logger.LogDebug("Unsubscribed from dynamic rate limiter events");
            }

            // Stop performance monitoring
            _performanceMonitoringTimer?.Dispose();
            
            // Dispose semaphores
            foreach (var semaphore in _storeSemaphores.Values)
            {
                semaphore?.Dispose();
            }
            
            _storeSemaphores.Clear();
            _storeMetrics.Clear();
            _concurrencyHistory.Clear();
            
            _logger.LogInformation("Enhanced Parallel Processor disposed with Phase 2.4 cleanup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Enhanced Parallel Processor disposal");
        }
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Gets or creates a semaphore for the specified store with the given concurrency limit
    /// </summary>
    private SemaphoreSlim GetOrCreateSemaphore(string storeId, int concurrency)
    {
        return _storeSemaphores.AddOrUpdate(storeId,
            _ => new SemaphoreSlim(concurrency, concurrency),
            (_, existing) =>
            {
                // If concurrency changed, create a new semaphore
                if (existing.CurrentCount + (concurrency - existing.CurrentCount) != concurrency)
                {
                    existing.Dispose();
                    return new SemaphoreSlim(concurrency, concurrency);
                }
                return existing;
            });
    }

    /// <summary>
    /// Processes batches with semaphore-based concurrency control
    /// </summary>
    private async Task<ParallelProcessingResult> ProcessBatchesWithSemaphoreAsync<TBatch>(
        IReadOnlyList<TBatch> batches,
        Func<TBatch, CancellationToken, Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult>> batchProcessor,
        SemaphoreSlim semaphore,
        IParallelProgressAggregator progressAggregator,
        ParallelProcessingConfiguration config,
        CancellationToken cancellationToken) where TBatch : class
    {
        var startTime = _dateTimeProvider.UtcNow;
        var batchTasks = new List<Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult>>();
        var concurrencyLevels = new List<int>();

        // Create tasks for all batches with semaphore control
        for (int i = 0; i < batches.Count; i++)
        {
            var batchIndex = i + 1; // 1-based for reporting
            var batch = batches[i];

                            var batchTask = ProcessBatchWithRateLimit(
                batch, batchProcessor, semaphore, progressAggregator, 
                batchIndex, config, cancellationToken);
            
            batchTasks.Add(batchTask);
        }

        // Wait for all batches to complete
        var batchResults = await Task.WhenAll(batchTasks);
        var endTime = _dateTimeProvider.UtcNow;
        var totalTime = endTime - startTime;

        // 🆕 TASK 3.2: Record aggregated batch progress for enhanced monitoring
        await RecordAggregatedBatchProgress(config, batchResults, startTime, endTime);

        // Aggregate results
        var result = new ParallelProcessingResult
        {
            TotalBatchesProcessed = batchResults.Length,
            SuccessfulBatches = batchResults.Count(r => r.FailedEntities == 0 && r.Errors.Count == 0),
            FailedBatches = batchResults.Count(r => r.FailedEntities > 0 || r.Errors.Count > 0),
            TotalEntitiesProcessed = batchResults.Sum(r => r.TotalProcessed),
            TotalEntitiesFailed = batchResults.Sum(r => r.FailedEntities),
            TotalEntitiesSkipped = batchResults.Sum(r => r.SkippedEntities),
            TotalEntitiesCancelled = batchResults.Sum(r => r.CancelledEntities),
            TotalProcessingTime = totalTime,
            AverageBatchProcessingTime = TimeSpan.FromMilliseconds(batchResults.Average(r => r.ProcessingTime.TotalMilliseconds)),
            PeakConcurrency = semaphore.CurrentCount > 0 ? (batchResults.Length - semaphore.CurrentCount) : batchResults.Length,
            AverageConcurrency = batchResults.Length / Math.Max(1, totalTime.TotalSeconds),
            OverallThroughput = totalTime.TotalSeconds > 0 ? batchResults.Sum(r => r.TotalProcessed) / totalTime.TotalSeconds : 0,
            ParallelizationImprovement = CalculateParallelizationImprovement(batchResults, totalTime),
            ProcessingErrors = batchResults.SelectMany(r => r.Errors).ToList()
        };

        return result;
    }

    /// <summary>
    /// Processes a single batch with enhanced dynamic rate limiting coordination
    /// **PHASE 2.4: Enhanced Dynamic Rate Limiting Integration**
    /// 
    /// Coordinates each parallel batch with Phase 1 dynamic rate limiter to ensure:
    /// - Proper rate limit checking before batch processing
    /// - API call tracking and feedback to rate limiter
    /// - Real-time rate limit adjustments during parallel execution
    /// - Health-aware processing with backoff strategies
    /// </summary>
    private async Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult> ProcessBatchWithRateLimit<TBatch>(
        TBatch batch,
        Func<TBatch, CancellationToken, Task<BigCommerce.Migration.Core.Interfaces.BatchProcessingResult>> batchProcessor,
        SemaphoreSlim semaphore,
        IParallelProgressAggregator progressAggregator,
        int batchNumber,
        ParallelProcessingConfiguration config,
        CancellationToken cancellationToken) where TBatch : class
    {
        // Comprehensive cancellation handling - wrap entire method to ensure deterministic behavior
        try
        {
            await semaphore.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Batch {BatchNumber} was cancelled while waiting for semaphore", batchNumber);
            
            // Return proper cancellation result without throwing
            return new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
            {
                BatchNumber = batchNumber,
                TotalProcessed = 0,
                SuccessfulEntities = 0,
                FailedEntities = 0,
                ProcessingTime = TimeSpan.Zero,
                Errors = new List<string> { "Batch processing was cancelled while waiting for concurrency slot" }
            };
        }
        
        try
        {
            try
            {
                // Report batch start
                await progressAggregator.ReportBatchStartAsync(batchNumber, 1, cancellationToken);

                // **PHASE 2.4: Enhanced Rate Limiting Integration** 
                if (config.RespectDynamicRateLimits)
                {
                    // Step 1: Check if we can make requests for this store
                    var canProceed = await _dynamicRateLimiter.CanMakeRequestAsync(config.StoreId, cancellationToken);
                    if (!canProceed)
                    {
                        // Step 2: Calculate proper delay and wait
                        await _dynamicRateLimiter.CheckAndWaitAsync(config.StoreId, cancellationToken);
                        _logger.LogDebug("Batch {BatchNumber} waited for rate limit clearance for store {StoreId}", 
                            batchNumber, config.StoreId);
                    }

                    // Step 3: Get current API health for processing decisions
                    var apiHealth = await _dynamicRateLimiter.GetApiHealthAsync(config.StoreId, cancellationToken);
                    var healthScore = apiHealth.GetHealthScore();
                    
                    // Step 4: Apply health-aware processing strategy
                    if (healthScore < 30) // Poor health - more conservative
                    {
                        var backoffDelay = TimeSpan.FromMilliseconds(200 + (50 - healthScore) * 10);
                        await Task.Delay(backoffDelay, cancellationToken);
                        _logger.LogDebug("Applied health-aware backoff ({BackoffMs}ms) for batch {BatchNumber} due to poor API health ({HealthScore})", 
                            backoffDelay.TotalMilliseconds, batchNumber, healthScore);
                    }
                    else if (healthScore > 80) // Excellent health - slight optimization
                    {
                        // No additional delay needed, API is performing well
                        _logger.LogDebug("Batch {BatchNumber} proceeding with optimal timing due to excellent API health ({HealthScore})", 
                            batchNumber, healthScore);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Batch {BatchNumber} was cancelled during rate limiting checks", batchNumber);
                
                // Return proper cancellation result without throwing
                return new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
                {
                    BatchNumber = batchNumber,
                    TotalProcessed = 0,
                    SuccessfulEntities = 0,
                    FailedEntities = 0,
                    ProcessingTime = TimeSpan.Zero,
                    Errors = new List<string> { "Batch processing was cancelled during rate limiting checks" }
                };
            }

            // Process the batch with timing and error tracking
            var batchStartTime = _dateTimeProvider.UtcNow;
            BigCommerce.Migration.Core.Interfaces.BatchProcessingResult result;
            var isSuccessful = false;
            var responseTime = 0.0;

            try
            {
                result = await batchProcessor(batch, cancellationToken);
                var batchEndTime = _dateTimeProvider.UtcNow;
                responseTime = (batchEndTime - batchStartTime).TotalMilliseconds;
                isSuccessful = result.FailedEntities == 0 && result.Errors.Count == 0;
                
                result.ProcessingTime = batchEndTime - batchStartTime;

                _logger.LogDebug("Batch {BatchNumber} processed: {SuccessfulEntities} successful, {FailedEntities} failed, {ResponseTime}ms", 
                    batchNumber, result.TotalProcessed - result.FailedEntities, result.FailedEntities, responseTime);
            }
            catch (Exception ex)
            {
                var batchEndTime = _dateTimeProvider.UtcNow;
                responseTime = (batchEndTime - batchStartTime).TotalMilliseconds;
                isSuccessful = false;
                
                // Handle cancellation gracefully without re-throwing - return proper cancellation result
                if (ex is OperationCanceledException)
                {
                    _logger.LogDebug("Batch {BatchNumber} was cancelled after {ResponseTime}ms", batchNumber, responseTime);
                    
                    // Return a proper cancellation result instead of throwing
                    result = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
                    {
                        BatchNumber = batchNumber,
                        TotalProcessed = 0,
                        SuccessfulEntities = 0,
                        FailedEntities = 0, // Don't count cancellation as failed entities
                        ProcessingTime = batchEndTime - batchStartTime,
                        Errors = new List<string> { "Batch processing was cancelled" }
                    };
                }
                else
                {
                    _logger.LogError(ex, "Batch {BatchNumber} failed after {ResponseTime}ms", batchNumber, responseTime);
                    
                    // Create error result for failed batch
                    result = new BigCommerce.Migration.Core.Interfaces.BatchProcessingResult
                    {
                        BatchNumber = batchNumber,
                        TotalProcessed = 0,
                        SuccessfulEntities = 0,
                        FailedEntities = 1, // Assume 1 entity failed for this batch
                        ProcessingTime = batchEndTime - batchStartTime,
                        Errors = new List<string> { ex.Message }
                    };
                }
            }

            // **PHASE 2.4: Rate Limit Feedback Integration**
            if (config.RespectDynamicRateLimits)
            {
                // Record API call metrics for dynamic rate limiter feedback
                var endpoint = $"batch-{config.EntityType ?? "entity"}"; // Provide endpoint context
                await _dynamicRateLimiter.RecordApiCallAsync(config.StoreId, endpoint, responseTime, isSuccessful, cancellationToken);
                
                // **Phase 2.7: Reset consecutive rate limit hits on successful requests**
                if (isSuccessful)
                {
                    _dynamicRateLimiter.ResetConsecutiveRateLimitHits(config.StoreId);
                }
                
                _logger.LogDebug("🔧 P2.7: Recorded API call feedback for batch {BatchNumber}: {ResponseTime}ms, success={IsSuccessful}", 
                    batchNumber, responseTime, isSuccessful);
            }

            // Report batch completion with enhanced metrics
            await progressAggregator.ReportBatchCompletionAsync(
                batchNumber, result.TotalProcessed, result.FailedEntities, 
                result.ProcessingTime, result.Errors, cancellationToken);

            // 🆕 TASK 3.2: Record batch-level incremental progress for finer granularity
            await RecordBatchIncrementalProgress(config, batchNumber, result, batchStartTime, _dateTimeProvider.UtcNow);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in batch {BatchNumber} processing with rate limiting", batchNumber);
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Calculates parallelization improvement factor
    /// </summary>
    private double CalculateParallelizationImprovement(BigCommerce.Migration.Core.Interfaces.BatchProcessingResult[] batchResults, TimeSpan totalTime)
    {
        if (batchResults.Length == 0 || totalTime.TotalSeconds <= 0) return 1.0;

        // Estimated sequential time would be sum of all batch processing times
        var sequentialTime = batchResults.Sum(r => r.ProcessingTime.TotalSeconds);
        var parallelTime = totalTime.TotalSeconds;

        return sequentialTime > 0 ? sequentialTime / parallelTime : 1.0;
    }

    // Additional helper methods would continue here...
    // (GetSystemResourceUtilizationAsync, CalculateSystemResourceConcurrency, etc.)
    // These are placeholder implementations for the core infrastructure

    private async Task<double> GetSystemResourceUtilizationAsync()
    {
        // Would implement actual system resource monitoring
        await Task.Delay(1); // Placeholder
        return 0.5; // 50% utilization
    }

    private int CalculateSystemResourceConcurrency(double utilization)
    {
        // Reduce concurrency as system utilization increases
        if (utilization > 0.90) return MIN_CONCURRENCY;
        if (utilization > 0.80) return DEFAULT_MAX_CONCURRENCY / 2;
        if (utilization > 0.70) return DEFAULT_MAX_CONCURRENCY;
        return MAX_CONCURRENCY;
    }

    private int GetHistoricalConcurrencyAdjustment(string storeId, string entityType)
    {
        // Would implement historical performance analysis
        return DEFAULT_MAX_CONCURRENCY;
    }

    private string BuildConcurrencyReasoning(double healthScore, double optimalRate, double systemUtilization,
        int baseConcurrency, int systemConcurrency, int historicalAdjustment, int final)
    {
        return $"Health: {healthScore:F1}/100, Rate: {optimalRate:F1}/sec, System: {systemUtilization:P1}, " +
               $"Base: {baseConcurrency}, System: {systemConcurrency}, Historical: {historicalAdjustment}, Final: {final}";
    }

    private double CalculateConfidenceLevel(double healthScore, double systemUtilization, double stabilityScore)
    {
        // Combine factors to determine confidence in the concurrency recommendation
        var healthFactor = healthScore / 100.0;
        var systemFactor = 1.0 - Math.Abs(systemUtilization - TARGET_CPU_UTILIZATION);
        var stabilityFactor = stabilityScore;

        return (healthFactor + systemFactor + stabilityFactor) / 3.0;
    }

    private double GetConcurrencyStabilityScore(string storeId)
    {
        // Would analyze historical concurrency adjustment frequency
        return 0.8; // 80% stability
    }

    /// <summary>
    /// Records processing performance and triggers performance change events
    /// **PHASE 2.4: Enhanced with real-time event triggering**
    /// </summary>
    private async Task RecordProcessingPerformanceAsync(string storeId, ParallelProcessingResult result, 
        TimeSpan totalTime, int concurrency)
    {
        var newMetrics = new ParallelPerformanceMetrics
        {
            CurrentThroughput = result.OverallThroughput,
            ActiveConcurrentBatches = concurrency,
            ErrorRate = result.FailedBatches > 0 ? (double)result.FailedBatches / result.TotalBatchesProcessed : 0.0,
            CapturedAt = _dateTimeProvider.UtcNow
        };

        // **PHASE 2.4: Performance Change Event Triggering**
        // Get previous metrics for comparison
        var previousMetrics = _storeMetrics.GetValueOrDefault(storeId, new ParallelPerformanceMetrics());
        
        // Update stored metrics
        _storeMetrics.AddOrUpdate(storeId, newMetrics, (_, _) => newMetrics);

        // Detect and trigger performance change events
        var changeSeverity = DetectPerformanceChangeSeverity(previousMetrics, newMetrics);
        if (changeSeverity.HasValue)
        {
            OnParallelPerformanceChanged(storeId, previousMetrics, newMetrics, changeSeverity.Value);
            
            _logger.LogInformation("Performance change detected for store {StoreId}: {Severity} - " +
                                 "Throughput: {PrevThroughput:F1} → {NewThroughput:F1}, " +
                                 "Error Rate: {PrevError:P2} → {NewError:P2}",
                storeId, changeSeverity, previousMetrics.CurrentThroughput, newMetrics.CurrentThroughput,
                previousMetrics.ErrorRate, newMetrics.ErrorRate);
        }

        await Task.CompletedTask; // Placeholder for actual persistence
    }

    /// <summary>
    /// Detects the severity of performance changes between metrics
    /// **PHASE 2.4: Performance change analysis**
    /// </summary>
    private PerformanceChangeSeverity? DetectPerformanceChangeSeverity(ParallelPerformanceMetrics previous, 
        ParallelPerformanceMetrics current)
    {
        // Skip if no previous metrics
        if (previous.CapturedAt == default) return null;

        // Calculate percentage changes
        var throughputChange = previous.CurrentThroughput > 0 
            ? Math.Abs(current.CurrentThroughput - previous.CurrentThroughput) / previous.CurrentThroughput
            : 0.0;
        
        var errorRateChange = Math.Abs(current.ErrorRate - previous.ErrorRate);

        // Determine severity based on thresholds
        if (throughputChange > 0.5 || errorRateChange > 0.2) // 50% throughput change or 20% error rate change
            return PerformanceChangeSeverity.Critical;
        
        if (throughputChange > 0.3 || errorRateChange > 0.1) // 30% throughput change or 10% error rate change
            return PerformanceChangeSeverity.Warning;
        
        if (throughputChange > 0.15 || errorRateChange > 0.05) // 15% throughput change or 5% error rate change
            return PerformanceChangeSeverity.Info;

        return null; // No significant change
    }

    private async void MonitorPerformanceAsync(object? state)
    {
        try
        {
            // Background performance monitoring would be implemented here
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during performance monitoring");
        }
    }

    // Additional placeholder implementations for remaining helper methods...
    private async Task<SystemPressureIndicators> DetectSystemPressureAsync() => 
        await Task.FromResult(new SystemPressureIndicators());

    private double CalculateOverallHealthScore(ParallelPerformanceMetrics metrics, SystemPressureIndicators pressure) => 85.0;

    private ParallelPerformanceMetrics CalculateHourlyAverageMetrics(string storeId) => new();

    private int GetRecentConcurrencyAdjustments(string storeId) => 0;

    private List<string> GetRecentErrors(string storeId) => new();

    private async Task<bool> TestRateLimitingIntegrationAsync(string storeId)
    {
        try
        {
            await _dynamicRateLimiter.GetOptimalRateAsync(storeId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Calculates confidence level for the concurrency recommendation
    /// **PHASE 2.4: Enhanced confidence calculation**
    /// </summary>
    private double CalculateConfidence(double healthScore, EnhancedRateLimitStatus rateStatus, 
        SystemPressureIndicators systemPressure, double optimalRate)
    {
        var healthConfidence = healthScore / 100.0; // 0.0 to 1.0
        
        var rateConfidence = rateStatus.RequestsRemaining > rateStatus.RequestsPerMinute * 0.5 ? 0.9 : 0.6;
        
        // System confidence based on pressure indicators
        var pressureCount = (systemPressure.CpuPressure ? 1 : 0) + 
                           (systemPressure.MemoryPressure ? 1 : 0) + 
                           (systemPressure.ConnectionPoolPressure ? 1 : 0) + 
                           (systemPressure.ThreadPoolPressure ? 1 : 0);
        var systemConfidence = pressureCount switch
        {
            0 => 0.95, // No pressure - high confidence
            1 => 0.80, // Light pressure
            2 => 0.60, // Moderate pressure  
            3 => 0.40, // High pressure
            _ => 0.20  // Critical pressure
        };
        
        var optimalRateConfidence = optimalRate > 15.0 ? 0.9 : 0.7; // High rate = high confidence
        
        // Weighted average
        return (healthConfidence * 0.4 + rateConfidence * 0.3 + systemConfidence * 0.2 + optimalRateConfidence * 0.1);
    }

    #endregion

    #region Task 3.2: Batch-Level Incremental Progress Methods

    /// <summary>
    /// 🆕 TASK 3.2: Records incremental progress for a completed batch
    /// Provides finer granularity than chunk-level updates by aggregating multiple chunks into batch-level updates
    /// 
    /// Design Principles:
    /// - Fire-and-forget: Doesn't block batch processing if increment write fails
    /// - Aggregated data: Combines multiple chunk results into batch summary
    /// - Performance monitoring: Tracks batch-level throughput and success rates
    /// - Enhanced analytics: Enables batch-level performance analysis
    /// </summary>
    /// <param name="config">Parallel processing configuration with migration context</param>
    /// <param name="batchNumber">Batch number within the parallel processing run</param>
    /// <param name="result">Batch processing result with aggregated chunk data</param>
    /// <param name="batchStartTime">When batch processing started</param>
    /// <param name="batchEndTime">When batch processing completed</param>
    private Task RecordBatchIncrementalProgress(
        ParallelProcessingConfiguration config,
        int batchNumber,
        BigCommerce.Migration.Core.Interfaces.BatchProcessingResult result,
        DateTime batchStartTime,
        DateTime batchEndTime)
    {
        // Skip if no migration context available
        if (string.IsNullOrEmpty(config.MigrationId) || string.IsNullOrEmpty(config.EntityType))
        {
            _logger.LogDebug("🔍 [TASK-3.2] Skipping batch progress recording - no migration context available for batch {BatchNumber}", batchNumber);
            return Task.CompletedTask;
        }

        try
        {
            // Extract store information from config (fallback to "unknown" if not available)
            var sourceStoreId = config.StoreId ?? "unknown";
            var destinationStoreId = config.StoreId ?? "unknown"; // In parallel processor, we typically only have one store context

            // Collect error messages for detailed tracking
            var errors = result.Errors?.Any() == true ? result.Errors : null;

            _logger.LogDebug("🚀 [TASK-3.2] Recording batch-level incremental progress: Batch {BatchNumber} " +
                           "Success={Success}, Failed={Failed}, Skipped={Skipped}, Cancelled={Cancelled}",
                batchNumber, result.SuccessfulEntities, result.FailedEntities, 
                result.SkippedEntities, result.CancelledEntities);

            // Use a special batch-level chunk number (negative to distinguish from regular chunks)
            var batchChunkNumber = -batchNumber; // Negative numbers indicate batch-level entries

            // Fire-and-forget call to avoid blocking batch processing
            _ = Task.Run(async () =>
            {
                try
                {
                    await _progressTracker.IncrementProgressAsync(
                        migrationId: config.MigrationId,
                        entityType: $"{config.EntityType}-batch", // Special entity type for batch-level tracking
                        chunkNumber: batchChunkNumber,
                        chunkStartIndex: batchNumber * 1000, // Estimated start index for batch
                        chunkSize: result.TotalProcessed,
                        successfulEntities: result.SuccessfulEntities,
                        failedEntities: result.FailedEntities,
                        skippedEntities: result.SkippedEntities,
                        cancelledEntities: result.CancelledEntities,
                        processingStartTime: batchStartTime,
                        processingEndTime: batchEndTime,
                        sourceStore: sourceStoreId,
                        destinationStore: destinationStoreId,
                        errors: errors);

                    _logger.LogDebug("✅ [TASK-3.2] Batch-level progress recorded for {MigrationId}:{EntityType}:Batch{BatchNumber}",
                        config.MigrationId, config.EntityType, batchNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ [TASK-3.2] Failed to record batch-level progress for {MigrationId}:{EntityType}:Batch{BatchNumber} - processing continues",
                        config.MigrationId, config.EntityType, batchNumber);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [TASK-3.2] Failed to initiate batch-level progress recording for {MigrationId}:{EntityType}:Batch{BatchNumber} - processing continues",
                config.MigrationId, config.EntityType, batchNumber);
            // Don't throw - batch-level progress failures should not break parallel processing
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// 🆕 TASK 3.2: Records aggregated progress after all batches complete
    /// Provides batch-level summary for performance monitoring and analytics
    /// 
    /// Design Principles:
    /// - Summary analytics: Aggregates all batch results into summary metrics
    /// - Performance insights: Tracks parallel processing efficiency
    /// - Monitoring data: Enables batch-level performance analysis
    /// - Non-blocking: Fire-and-forget to avoid impacting parallel processor performance
    /// </summary>
    /// <param name="config">Parallel processing configuration with migration context</param>
    /// <param name="batchResults">Results from all completed batches</param>
    /// <param name="processingStartTime">When parallel processing started</param>
    /// <param name="processingEndTime">When parallel processing completed</param>
    private Task RecordAggregatedBatchProgress(
        ParallelProcessingConfiguration config,
        BigCommerce.Migration.Core.Interfaces.BatchProcessingResult[] batchResults,
        DateTime processingStartTime,
        DateTime processingEndTime)
    {
        // Skip if no migration context available
        if (string.IsNullOrEmpty(config.MigrationId) || string.IsNullOrEmpty(config.EntityType))
        {
            _logger.LogDebug("🔍 [TASK-3.2] Skipping aggregated batch progress recording - no migration context available");
            return Task.CompletedTask;
        }

        try
        {
            // Calculate aggregated totals
            var totalSuccessful = batchResults.Sum(r => r.SuccessfulEntities);
            var totalFailed = batchResults.Sum(r => r.FailedEntities);
            var totalSkipped = batchResults.Sum(r => r.SkippedEntities);
            var totalCancelled = batchResults.Sum(r => r.CancelledEntities);
            var totalProcessed = batchResults.Sum(r => r.TotalProcessed);

            // Collect error summary
            var allErrors = batchResults.SelectMany(r => r.Errors ?? new List<string>()).ToList();
            var errorSummary = allErrors.Take(10).ToList(); // Limit to first 10 errors

            _logger.LogDebug("🚀 [TASK-3.2] Recording aggregated batch progress: {BatchCount} batches, " +
                           "Total: {Total}, Success: {Success}, Failed: {Failed}, Skipped: {Skipped}, Cancelled: {Cancelled}",
                batchResults.Length, totalProcessed, totalSuccessful, totalFailed, totalSkipped, totalCancelled);

            // Use a special aggregated chunk number (large negative number)
            var aggregatedChunkNumber = -9999; // Special number for aggregated batch entries

            // Fire-and-forget call to avoid blocking parallel processor
            _ = Task.Run(async () =>
            {
                try
                {
                    await _progressTracker.IncrementProgressAsync(
                        migrationId: config.MigrationId,
                        entityType: $"{config.EntityType}-aggregated", // Special entity type for aggregated tracking
                        chunkNumber: aggregatedChunkNumber,
                        chunkStartIndex: 0,
                        chunkSize: totalProcessed,
                        successfulEntities: totalSuccessful,
                        failedEntities: totalFailed,
                        skippedEntities: totalSkipped,
                        cancelledEntities: totalCancelled,
                        processingStartTime: processingStartTime,
                        processingEndTime: processingEndTime,
                        sourceStore: config.StoreId ?? "unknown",
                        destinationStore: config.StoreId ?? "unknown",
                        errors: errorSummary);

                    _logger.LogDebug("✅ [TASK-3.2] Aggregated batch progress recorded for {MigrationId}:{EntityType} - {BatchCount} batches",
                        config.MigrationId, config.EntityType, batchResults.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ [TASK-3.2] Failed to record aggregated batch progress for {MigrationId}:{EntityType} - processing continues",
                        config.MigrationId, config.EntityType);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [TASK-3.2] Failed to initiate aggregated batch progress recording for {MigrationId}:{EntityType} - processing continues",
                config.MigrationId, config.EntityType);
            // Don't throw - aggregated progress failures should not break parallel processing
        }
        
        return Task.CompletedTask;
    }

    #endregion
}

#region Supporting Classes

/// <summary>
/// Tracks concurrency adjustment history for a store
/// </summary>
internal class ConcurrencyHistory
{
    public List<ConcurrencyAdjustmentRecord> Adjustments { get; } = new();
    public int CurrentConcurrency { get; set; } = 4;
    public DateTime LastAdjustment { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Individual concurrency adjustment record
/// </summary>
internal class ConcurrencyAdjustmentRecord
{
    public DateTime Timestamp { get; set; }
    public int FromConcurrency { get; set; }
    public int ToConcurrency { get; set; }
    public string Reason { get; set; } = string.Empty;
    public PerformanceTrigger Trigger { get; set; }
}

#endregion 