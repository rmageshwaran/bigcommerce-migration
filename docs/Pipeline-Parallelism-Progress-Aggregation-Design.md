# Pipeline Parallelism Progress Aggregation - Design Document

## 🎯 **CHALLENGE OVERVIEW**

**Problem**: With Pipeline Parallelism, we have multiple entity types (Options, Modifiers, Images, Reviews) processing simultaneously across independent channels. We need to:

1. **Track individual entity progress** for each type (Options: 150/200, Modifiers: 45/50)
2. **Aggregate into overall progress** (Combined progress across all entity types)
3. **Send real-time SignalR updates** that are meaningful to users
4. **Maintain thread-safety** across concurrent processing channels

## 🏗️ **PROPOSED ARCHITECTURE**

### **1. Multi-Channel Progress Aggregator**

```csharp
/// <summary>
/// Aggregates progress across multiple entity processing channels in pipeline parallelism
/// Thread-safe implementation for concurrent entity type processing
/// </summary>
public class PipelineProgressAggregator : IPipelineProgressAggregator
{
    private readonly string _migrationId;
    private readonly ISignalREventFactory _signalREventFactory;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ILogger<PipelineProgressAggregator> _logger;
    
    // Thread-safe per-entity progress tracking
    private readonly ConcurrentDictionary<string, EntityChannelProgress> _entityProgress;
    private readonly object _aggregationLock = new();
    private readonly Timer _progressUpdateTimer;
    
    // Overall pipeline progress
    private PipelineProgress _overallProgress;
    private DateTime _lastSignalRUpdate = DateTime.MinValue;
    private readonly TimeSpan _signalRUpdateInterval = TimeSpan.FromMilliseconds(250); // 4 updates per second
    
    public PipelineProgressAggregator(
        string migrationId,
        List<EntityChannelConfig> channelConfigs,
        ISignalREventFactory signalREventFactory,
        IProgressEventPublisher progressEventPublisher,
        ILogger<PipelineProgressAggregator> logger)
    {
        _migrationId = migrationId;
        _signalREventFactory = signalREventFactory;
        _progressEventPublisher = progressEventPublisher;
        _logger = logger;
        
        // Initialize progress tracking for each entity type
        _entityProgress = new ConcurrentDictionary<string, EntityChannelProgress>();
        foreach (var config in channelConfigs)
        {
            _entityProgress[config.EntityType] = new EntityChannelProgress
            {
                EntityType = config.EntityType,
                TotalCount = config.EstimatedCount,
                ProcessedCount = 0,
                SuccessCount = 0,
                FailureCount = 0,
                Status = "starting",
                StartTime = DateTime.UtcNow
            };
        }
        
        _overallProgress = new PipelineProgress
        {
            MigrationId = migrationId,
            StartTime = DateTime.UtcNow,
            EntityChannels = _entityProgress.Values.ToList()
        };
        
        // Start timer for periodic SignalR updates
        _progressUpdateTimer = new Timer(SendPeriodicUpdate, null, _signalRUpdateInterval, _signalRUpdateInterval);
    }
}
```

### **2. Entity Channel Progress Tracking**

```csharp
/// <summary>
/// Progress tracking for individual entity type channels
/// </summary>
public class EntityChannelProgress
{
    public string EntityType { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double ProgressPercentage => TotalCount > 0 ? (double)ProcessedCount / TotalCount * 100.0 : 0.0;
    public string Status { get; set; } = "pending"; // pending, processing, completed, failed
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? ProcessingTime => EndTime?.Subtract(StartTime);
    
    // Rate tracking
    public double CurrentThroughputPerSecond { get; set; }
    public List<TimestampedProgress> RecentProgress { get; set; } = new();
}

/// <summary>
/// Overall pipeline progress combining all entity channels
/// </summary>
public class PipelineProgress
{
    public string MigrationId { get; set; } = string.Empty;
    public List<EntityChannelProgress> EntityChannels { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    
    // Calculated properties
    public int TotalEntitiesAcrossAllTypes => EntityChannels.Sum(e => e.TotalCount);
    public int ProcessedEntitiesAcrossAllTypes => EntityChannels.Sum(e => e.ProcessedCount);
    public int SuccessEntitiesAcrossAllTypes => EntityChannels.Sum(e => e.SuccessCount);
    public int FailedEntitiesAcrossAllTypes => EntityChannels.Sum(e => e.FailureCount);
    public double OverallProgressPercentage => TotalEntitiesAcrossAllTypes > 0 
        ? (double)ProcessedEntitiesAcrossAllTypes / TotalEntitiesAcrossAllTypes * 100.0 : 0.0;
    
    public string OverallStatus => CalculateOverallStatus();
    
    private string CalculateOverallStatus()
    {
        if (!EntityChannels.Any()) return "starting";
        if (EntityChannels.All(e => e.Status == "completed")) return "completed";
        if (EntityChannels.Any(e => e.Status == "processing")) return "processing";
        if (EntityChannels.All(e => e.Status == "pending")) return "starting";
        return "processing";
    }
}
```

### **3. Thread-Safe Progress Updates**

```csharp
/// <summary>
/// Thread-safe method to update progress for specific entity channel
/// Called by individual channel processors
/// </summary>
public async Task UpdateEntityChannelProgressAsync(
    string entityType, 
    int processedCount, 
    int successCount, 
    int failureCount,
    CancellationToken cancellationToken = default)
{
    if (!_entityProgress.TryGetValue(entityType, out var entityProgress))
    {
        _logger.LogWarning("Attempted to update progress for unknown entity type: {EntityType}", entityType);
        return;
    }
    
    // Thread-safe update
    lock (entityProgress)
    {
        entityProgress.ProcessedCount = processedCount;
        entityProgress.SuccessCount = successCount;
        entityProgress.FailureCount = failureCount;
        entityProgress.Status = processedCount >= entityProgress.TotalCount ? "completed" : "processing";
        
        if (entityProgress.Status == "completed" && !entityProgress.EndTime.HasValue)
        {
            entityProgress.EndTime = DateTime.UtcNow;
        }
        
        // Update throughput calculation
        UpdateThroughputMetrics(entityProgress);
    }
    
    // Trigger immediate SignalR update if significant progress
    await TriggerSignalRUpdateIfNeeded(entityType, cancellationToken);
}

/// <summary>
/// Calculate real-time throughput for entity channel
/// </summary>
private void UpdateThroughputMetrics(EntityChannelProgress entityProgress)
{
    var now = DateTime.UtcNow;
    var recentProgress = new TimestampedProgress
    {
        Timestamp = now,
        ProcessedCount = entityProgress.ProcessedCount
    };
    
    entityProgress.RecentProgress.Add(recentProgress);
    
    // Keep only last 10 seconds of progress data
    var cutoff = now.AddSeconds(-10);
    entityProgress.RecentProgress.RemoveAll(p => p.Timestamp < cutoff);
    
    // Calculate throughput from recent progress
    if (entityProgress.RecentProgress.Count >= 2)
    {
        var oldest = entityProgress.RecentProgress.First();
        var newest = entityProgress.RecentProgress.Last();
        var timeSpan = newest.Timestamp - oldest.Timestamp;
        var processedDiff = newest.ProcessedCount - oldest.ProcessedCount;
        
        entityProgress.CurrentThroughputPerSecond = timeSpan.TotalSeconds > 0 
            ? processedDiff / timeSpan.TotalSeconds : 0.0;
    }
}
```

### **4. Intelligent SignalR Updates**

```csharp
/// <summary>
/// Sends periodic SignalR updates with aggregated progress
/// Rate-limited to prevent UI flooding while maintaining real-time feel
/// </summary>
private async void SendPeriodicUpdate(object? state)
{
    try
    {
        await SendAggregatedProgressUpdateAsync();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error sending periodic pipeline progress update for migration {MigrationId}", _migrationId);
    }
}

/// <summary>
/// Sends comprehensive progress update combining all entity channels
/// </summary>
private async Task SendAggregatedProgressUpdateAsync(CancellationToken cancellationToken = default)
{
    PipelineProgress currentProgress;
    
    lock (_aggregationLock)
    {
        // Create snapshot of current progress
        currentProgress = new PipelineProgress
        {
            MigrationId = _migrationId,
            StartTime = _overallProgress.StartTime,
            EntityChannels = _entityProgress.Values.Select(e => new EntityChannelProgress
            {
                EntityType = e.EntityType,
                TotalCount = e.TotalCount,
                ProcessedCount = e.ProcessedCount,
                SuccessCount = e.SuccessCount,
                FailureCount = e.FailureCount,
                Status = e.Status,
                StartTime = e.StartTime,
                EndTime = e.EndTime,
                CurrentThroughputPerSecond = e.CurrentThroughputPerSecond
            }).ToList()
        };
        
        if (currentProgress.OverallStatus == "completed" && !currentProgress.EndTime.HasValue)
        {
            currentProgress.EndTime = DateTime.UtcNow;
        }
    }
    
    // Send individual entity progress events
    foreach (var entityProgress in currentProgress.EntityChannels)
    {
        var entityEvent = _signalREventFactory.CreateEntityProgress(_migrationId, new EntityProgressOptions
        {
            EntityType = entityProgress.EntityType,
            TotalCount = entityProgress.TotalCount,
            ProcessedCount = entityProgress.ProcessedCount,
            SuccessCount = entityProgress.SuccessCount,
            FailedCount = entityProgress.FailureCount,
            Status = entityProgress.Status,
            ProcessingTime = entityProgress.ProcessingTime,
            // Additional pipeline-specific data
            ThroughputPerSecond = entityProgress.CurrentThroughputPerSecond,
            ProgressPercentage = entityProgress.ProgressPercentage
        });
        
        await _progressEventPublisher.PublishEntityProgressAsync(entityEvent, cancellationToken);
    }
    
    // Send overall pipeline progress event
    var overallEvent = _signalREventFactory.CreateMigrationProgress(_migrationId, new MigrationProgressOptions
    {
        CurrentEntityType = "comprehensive_entities", // Phase 2 identifier
        OverallProgress = currentProgress.OverallProgressPercentage,
        Status = currentProgress.OverallStatus,
        TotalEntities = currentProgress.TotalEntitiesAcrossAllTypes,
        ProcessedEntities = currentProgress.ProcessedEntitiesAcrossAllTypes,
        SuccessfulEntities = currentProgress.SuccessEntitiesAcrossAllTypes,
        FailedEntities = currentProgress.FailedEntitiesAcrossAllTypes,
        
        // Pipeline-specific details
        CurrentBatchDetails = CreateBatchDetailsFromChannels(currentProgress.EntityChannels),
        ElapsedTime = DateTime.UtcNow - currentProgress.StartTime,
        StartTime = currentProgress.StartTime
    });
    
    await _progressEventPublisher.PublishMigrationProgressAsync(overallEvent, cancellationToken);
    
    _logger.LogDebug("📊 [PIPELINE-PROGRESS] Overall: {ProcessedEntities}/{TotalEntities} ({OverallProgress:F1}%) - " +
                    "Options: {OptionsProgress}, Modifiers: {ModifiersProgress}, Images: {ImagesProgress}, Reviews: {ReviewsProgress}",
        currentProgress.ProcessedEntitiesAcrossAllTypes, currentProgress.TotalEntitiesAcrossAllTypes, currentProgress.OverallProgressPercentage,
        GetEntityProgressSummary(currentProgress.EntityChannels, "options"),
        GetEntityProgressSummary(currentProgress.EntityChannels, "modifiers"),
        GetEntityProgressSummary(currentProgress.EntityChannels, "images"),
        GetEntityProgressSummary(currentProgress.EntityChannels, "reviews"));
}

/// <summary>
/// Creates batch details string for UI display
/// </summary>
private string CreateBatchDetailsFromChannels(List<EntityChannelProgress> channels)
{
    var details = channels.Select(c => 
        $"{c.EntityType}: {c.ProcessedCount}/{c.TotalCount} ({c.ProgressPercentage:F0}%)");
    return string.Join(", ", details);
}

/// <summary>
/// Gets progress summary for specific entity type
/// </summary>
private string GetEntityProgressSummary(List<EntityChannelProgress> channels, string entityType)
{
    var channel = channels.FirstOrDefault(c => c.EntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase));
    return channel != null ? $"{channel.ProcessedCount}/{channel.TotalCount}" : "0/0";
}
```

## 🔗 **INTEGRATION WITH PIPELINE PARALLELISM**

### **5. Channel Worker Progress Reporting**

```csharp
/// <summary>
/// Enhanced channel worker with progress reporting
/// </summary>
public class EntityChannelWorker<T> where T : class
{
    private readonly string _entityType;
    private readonly IPipelineProgressAggregator _progressAggregator;
    private readonly ILogger<EntityChannelWorker<T>> _logger;
    private int _processedCount = 0;
    private int _successCount = 0;
    private int _failureCount = 0;
    
    public async Task ProcessChannelAsync(
        ChannelReader<Product> reader,
        Func<Product, Task<T?>> processor,
        SemaphoreSlim concurrencyLimiter,
        CancellationToken cancellationToken)
    {
        await foreach (var product in reader.ReadAllAsync(cancellationToken))
        {
            await concurrencyLimiter.WaitAsync(cancellationToken);
            
            try
            {
                var result = await processor(product);
                
                // Update counters
                Interlocked.Increment(ref _processedCount);
                if (result != null)
                    Interlocked.Increment(ref _successCount);
                else
                    Interlocked.Increment(ref _failureCount);
                
                // Report progress every 5 processed items or every 2 seconds
                if (_processedCount % 5 == 0 || ShouldReportProgress())
                {
                    await _progressAggregator.UpdateEntityChannelProgressAsync(
                        _entityType, _processedCount, _successCount, _failureCount, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _processedCount);
                Interlocked.Increment(ref _failureCount);
                
                _logger.LogError(ex, "Error processing {EntityType} for product {ProductId}", 
                    _entityType, product.GetValueOrDefault("id"));
                
                // Report progress on errors immediately
                await _progressAggregator.UpdateEntityChannelProgressAsync(
                    _entityType, _processedCount, _successCount, _failureCount, cancellationToken);
            }
            finally
            {
                concurrencyLimiter.Release();
            }
        }
        
        // Final progress update
        await _progressAggregator.UpdateEntityChannelProgressAsync(
            _entityType, _processedCount, _successCount, _failureCount, cancellationToken);
            
        _logger.LogInformation("✅ [{EntityType}] Channel processing completed: {ProcessedCount} processed, {SuccessCount} successful, {FailureCount} failed",
            _entityType, _processedCount, _successCount, _failureCount);
    }
    
    private bool ShouldReportProgress()
    {
        // Report progress every 2 seconds for real-time updates
        return DateTime.UtcNow - _lastProgressReport > TimeSpan.FromSeconds(2);
    }
}
```

### **6. Pipeline Processor Integration**

```csharp
/// <summary>
/// Enhanced PipelineParallelProcessor with integrated progress reporting
/// </summary>
public class PipelineParallelProcessor : IPipelineParallelProcessor
{
    public async Task ProcessComprehensiveEntitiesAsync(
        List<Product> products,
        PipelineConfiguration config,
        CancellationToken cancellationToken = default)
    {
        // Initialize progress aggregator with entity counts
        var channelConfigs = new List<EntityChannelConfig>
        {
            new() { EntityType = "options", EstimatedCount = EstimateOptionsCount(products) },
            new() { EntityType = "modifiers", EstimatedCount = EstimateModifiersCount(products) },
            new() { EntityType = "images", EstimatedCount = EstimateImagesCount(products) },
            new() { EntityType = "reviews", EstimatedCount = EstimateReviewsCount(products) }
        };
        
        var progressAggregator = new PipelineProgressAggregator(
            config.MigrationId, channelConfigs, _signalREventFactory, _progressEventPublisher, _logger);
        
        // Create processing channels
        var optionsChannel = Channel.CreateUnbounded<Product>();
        var modifiersChannel = Channel.CreateUnbounded<Product>();
        var imagesChannel = Channel.CreateUnbounded<Product>();
        var reviewsChannel = Channel.CreateUnbounded<Product>();
        
        // Start channel workers with progress reporting
        var workers = Task.WhenAll(
            new EntityChannelWorker<Option>("options", progressAggregator, _logger)
                .ProcessChannelAsync(optionsChannel.Reader, ProcessOptionsForProduct, _optionsSemaphore, cancellationToken),
            new EntityChannelWorker<Modifier>("modifiers", progressAggregator, _logger)
                .ProcessChannelAsync(modifiersChannel.Reader, ProcessModifiersForProduct, _modifiersSemaphore, cancellationToken),
            new EntityChannelWorker<Image>("images", progressAggregator, _logger)
                .ProcessChannelAsync(imagesChannel.Reader, ProcessImagesForProduct, _imagesSemaphore, cancellationToken),
            new EntityChannelWorker<Review>("reviews", progressAggregator, _logger)
                .ProcessChannelAsync(reviewsChannel.Reader, ProcessReviewsForProduct, _reviewsSemaphore, cancellationToken)
        );
        
        // Feed products to all channels simultaneously
        foreach (var product in products)
        {
            await optionsChannel.Writer.WriteAsync(product, cancellationToken);
            await modifiersChannel.Writer.WriteAsync(product, cancellationToken);
            await imagesChannel.Writer.WriteAsync(product, cancellationToken);
            await reviewsChannel.Writer.WriteAsync(product, cancellationToken);
        }
        
        // Complete channels and wait for workers
        optionsChannel.Writer.Complete();
        modifiersChannel.Writer.Complete();
        imagesChannel.Writer.Complete();
        reviewsChannel.Writer.Complete();
        
        await workers;
        
        // Final aggregated progress update
        await progressAggregator.CompleteProcessingAsync(cancellationToken);
        
        _logger.LogInformation("🎉 [PIPELINE-COMPLETED] All entity channels completed processing for {ProductCount} products", 
            products.Count);
    }
}
```

## 📊 **FRONTEND INTEGRATION**

### **7. Dashboard Progress Display**

The frontend will receive multiple types of progress events:

```typescript
// Individual entity progress events
interface EntityProgressUpdate {
    entityType: string;           // "options", "modifiers", "images", "reviews"
    totalCount: number;
    processedCount: number;
    successCount: number;
    failedCount: number;
    progressPercentage: number;
    status: string;               // "pending", "processing", "completed"
    throughputPerSecond: number;
}

// Overall pipeline progress event
interface PipelineProgressUpdate {
    overallProgress: number;      // Combined percentage across all entity types
    totalEntities: number;        // Sum of all entity counts
    processedEntities: number;    // Sum of all processed entities
    currentBatchDetails: string;  // "Options: 150/200, Modifiers: 45/50, ..."
    status: string;              // "starting", "processing", "completed"
}
```

**Dashboard Display Example:**
```
📊 Comprehensive Entity Migration Progress: 67.3% Complete

Overall Progress: ████████████░░░░░░░░ 856/1,200 entities

Entity Breakdown:
├── Options:    ████████████████████ 200/200 (100%) ✅ 15.2/sec
├── Modifiers:  ████████████████░░░░ 150/180 (83%)  🔄 8.7/sec  
├── Images:     ████████████░░░░░░░░ 320/520 (62%)  🔄 12.1/sec
└── Reviews:    ██████████████████░░ 186/200 (93%)  🔄 9.3/sec

Status: Processing • Elapsed: 2m 34s • ETA: 1m 12s
```

## ⚡ **PERFORMANCE CHARACTERISTICS**

### **Progress Update Frequency**
- **Entity Channel Updates**: Every 5 entities processed OR every 2 seconds
- **SignalR Broadcast**: Every 250ms (4 updates per second)
- **Dashboard Refresh**: Real-time with 250ms updates

### **Memory Efficiency**
- **Per-Channel Tracking**: Lightweight counters and minimal state
- **Rate-Limited Updates**: Prevents SignalR flooding
- **Garbage Collection**: Minimal object allocation in hot paths

### **Thread Safety**
- **Interlocked Operations**: For counter updates
- **Concurrent Collections**: For thread-safe progress storage
- **Lock Minimization**: Only for critical aggregation operations

## 🎯 **BENEFITS**

1. **Real-Time Granular Progress**: Users see progress for each entity type individually
2. **Overall Progress Clarity**: Combined progress shows migration completion status
3. **Performance Visibility**: Throughput metrics show processing speed per entity type
4. **Thread-Safe Operation**: Works correctly with concurrent pipeline processing
5. **UI Performance**: Rate-limited updates prevent frontend overload
6. **Diagnostic Information**: Detailed logging for troubleshooting

This design provides comprehensive progress tracking that matches the sophisticated Pipeline Parallelism architecture while maintaining excellent user experience and system performance.