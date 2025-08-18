using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Azure Table Storage implementation for chunk increment events
/// Enables real-time progress tracking and prevents data loss during migration cancellations
/// 
/// Key Features:
/// - Fire-and-forget writes with graceful error handling
/// - Unique RowKey generation prevents write conflicts
/// - Query-time aggregation ensures data consistency
/// - Retry logic handles transient Azure storage failures
/// - Comprehensive logging for monitoring and debugging
/// </summary>
public class IncrementEventsService : IIncrementEventsService, IDisposable
{
    #region Private Fields

    private readonly IAzureTableInitializationService _tableInitializationService;
    private readonly ILogger<IncrementEventsService> _logger;
    private const string TableName = "chunkincrementevents";
    
    // Unique sequence counter for RowKey generation (thread-safe)
    private static readonly object _sequenceLock = new();
    private static int _sequenceCounter = 0;
    
    // Retry configuration
    private static readonly TimeSpan[] RetryDelays = 
    {
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(2)
    };

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the IncrementEventsService
    /// </summary>
    /// <param name="tableInitializationService">Centralized table initialization service</param>
    /// <param name="logger">Logger instance for monitoring and debugging</param>
    public IncrementEventsService(IAzureTableInitializationService tableInitializationService, ILogger<IncrementEventsService> logger)
    {
        _tableInitializationService = tableInitializationService ?? throw new ArgumentNullException(nameof(tableInitializationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.LogInformation("✅ IncrementEventsService initialized with centralized table management for: {TableName}", TableName);
    }

    #endregion

    #region Write Operations

    /// <inheritdoc />
    public async Task WriteChunkIncrementAsync(ChunkIncrementEvent incrementEvent, CancellationToken cancellationToken = default)
    {
        if (incrementEvent == null)
        {
            _logger.LogWarning("❌ INCREMENTAL-STORAGE: WriteChunkIncrementAsync called with null incrementEvent - skipping");
            return;
        }

        _logger.LogInformation("🔄 INCREMENTAL-STORAGE: Starting WriteChunkIncrementAsync for {MigrationId}:{EntityType}:Chunk{ChunkNumber} " +
            "with Success={Success}, Failed={Failed}, Skipped={Skipped}, Cancelled={Cancelled}",
            incrementEvent.MigrationId, incrementEvent.EntityType, incrementEvent.ChunkNumber,
            incrementEvent.SuccessfulEntities, incrementEvent.FailedEntities, 
            incrementEvent.SkippedEntities, incrementEvent.CancelledEntities);

        try
        {
            // Validate the event data
            _logger.LogInformation("🔍 INCREMENTAL-STORAGE: Validating chunk increment event for {MigrationId}:{EntityType}:Chunk{ChunkNumber}",
                incrementEvent.MigrationId, incrementEvent.EntityType, incrementEvent.ChunkNumber);
                
            var validationErrors = incrementEvent.Validate();
            if (validationErrors.Any())
            {
                _logger.LogError("❌ INCREMENTAL-STORAGE: Invalid chunk increment event for {MigrationId}:{EntityType}:Chunk{ChunkNumber}: {Errors} - skipping write", 
                    incrementEvent.MigrationId, incrementEvent.EntityType, incrementEvent.ChunkNumber,
                    string.Join(", ", validationErrors));
                return;
            }

            _logger.LogInformation("✅ INCREMENTAL-STORAGE: Event validation passed for {MigrationId}:{EntityType}:Chunk{ChunkNumber}",
                incrementEvent.MigrationId, incrementEvent.EntityType, incrementEvent.ChunkNumber);

            // Generate unique RowKey to prevent conflicts
            var originalRowKey = incrementEvent.RowKey;
            incrementEvent.RowKey = GenerateUniqueRowKey(incrementEvent.EntityType, incrementEvent.ChunkNumber);
            
            _logger.LogInformation("🔑 INCREMENTAL-STORAGE: Generated unique RowKey for {MigrationId}:{EntityType}:Chunk{ChunkNumber}: " +
                "Original={OriginalRowKey}, New={NewRowKey}",
                incrementEvent.MigrationId, incrementEvent.EntityType, incrementEvent.ChunkNumber,
                originalRowKey, incrementEvent.RowKey);

            // Write with retry logic
            _logger.LogInformation("💾 INCREMENTAL-STORAGE: Starting Azure Table write with retry logic for {MigrationId}:{EntityType}:Chunk{ChunkNumber}",
                incrementEvent.MigrationId, incrementEvent.EntityType, incrementEvent.ChunkNumber);
                
            await WriteWithRetryAsync(incrementEvent, cancellationToken);

            _logger.LogInformation("✅ INCREMENTAL-STORAGE: Chunk increment written successfully to Azure Table: {MigrationId}:{EntityType}:Chunk{ChunkNumber} " +
                                 "Success={Success}, Failed={Failed}, Skipped={Skipped}, Cancelled={Cancelled}, RowKey={RowKey}",
                incrementEvent.MigrationId, incrementEvent.EntityType, incrementEvent.ChunkNumber,
                incrementEvent.SuccessfulEntities, incrementEvent.FailedEntities, 
                incrementEvent.SkippedEntities, incrementEvent.CancelledEntities, incrementEvent.RowKey);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🚫 INCREMENTAL-STORAGE: WriteChunkIncrementAsync cancelled for {MigrationId}:{EntityType}:Chunk{ChunkNumber}",
                incrementEvent.MigrationId, incrementEvent.EntityType, incrementEvent.ChunkNumber);
            // Don't throw - cancellation during increment write is expected
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ INCREMENTAL-STORAGE: Failed to write chunk increment to Azure Table: {MigrationId}:{EntityType}:Chunk{ChunkNumber} - migration continues. Error: {ErrorMessage}",
                incrementEvent.MigrationId, incrementEvent.EntityType, incrementEvent.ChunkNumber, ex.Message);
            
            // Don't throw - increment write failures should not break migration
            // The end-of-migration update will still provide fallback data
        }
    }

    /// <inheritdoc />
    public async Task WriteBatchChunkIncrementsAsync(IList<ChunkIncrementEvent> incrementEvents, CancellationToken cancellationToken = default)
    {
        if (incrementEvents == null || !incrementEvents.Any())
        {
            _logger.LogDebug("WriteBatchChunkIncrementsAsync called with empty list - skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Writing batch of {Count} chunk increments", incrementEvents.Count);

            // Validate all events and generate unique RowKeys
            var validEvents = new List<ChunkIncrementEvent>();
            
            foreach (var incrementEvent in incrementEvents)
            {
                var validationErrors = incrementEvent.Validate();
                if (validationErrors.Any())
                {
                    _logger.LogWarning("Invalid chunk increment event in batch: {Errors} - skipping", 
                        string.Join(", ", validationErrors));
                    continue;
                }

                incrementEvent.RowKey = GenerateUniqueRowKey(incrementEvent.EntityType, incrementEvent.ChunkNumber);
                validEvents.Add(incrementEvent);
            }

            if (!validEvents.Any())
            {
                _logger.LogWarning("No valid events in batch - skipping write");
                return;
            }

            // Try batch operation first (Azure Table Storage supports up to 100 operations per batch)
            if (validEvents.Count <= 100)
            {
                await WriteBatchWithRetryAsync(validEvents, cancellationToken);
            }
            else
            {
                // Split into multiple batches
                var batches = validEvents.Chunk(100).ToList();
                _logger.LogInformation("Splitting {Count} events into {BatchCount} batches", validEvents.Count, batches.Count);

                foreach (var batch in batches)
                {
                    await WriteBatchWithRetryAsync(batch.ToList(), cancellationToken);
                }
            }

            _logger.LogInformation("✅ Successfully wrote batch of {Count} chunk increments", validEvents.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WriteBatchChunkIncrementsAsync cancelled for batch of {Count} events", incrementEvents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to write batch of {Count} chunk increments - falling back to individual writes", 
                incrementEvents.Count);

            // Fallback: Try individual writes
            foreach (var incrementEvent in incrementEvents)
            {
                await WriteChunkIncrementAsync(incrementEvent, cancellationToken);
            }
        }
    }

    #endregion

    #region Read Operations

    /// <inheritdoc />
    public async Task<List<ChunkIncrementEvent>> GetChunkIncrementsAsync(string migrationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

        try
        {
            var tableClient = await GetTableClientAsync();
            var query = tableClient.QueryAsync<ChunkIncrementEvent>(
                filter: $"PartitionKey eq '{migrationId}'",
                cancellationToken: cancellationToken);

            var results = new List<ChunkIncrementEvent>();
            await foreach (var incrementEvent in query)
            {
                results.Add(incrementEvent);
            }

            _logger.LogDebug("Retrieved {Count} chunk increments for migration {MigrationId}", results.Count, migrationId);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve chunk increments for migration {MigrationId}", migrationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<ChunkIncrementEvent>> GetChunkIncrementsAsync(string migrationId, string entityType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrEmpty(entityType))
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));

        try
        {
            var tableClient = await GetTableClientAsync();
            var query = tableClient.QueryAsync<ChunkIncrementEvent>(
                filter: $"PartitionKey eq '{migrationId}' and EntityType eq '{entityType}'",
                cancellationToken: cancellationToken);

            var results = new List<ChunkIncrementEvent>();
            await foreach (var incrementEvent in query)
            {
                results.Add(incrementEvent);
            }

            _logger.LogDebug("Retrieved {Count} chunk increments for migration {MigrationId}, entity {EntityType}", 
                results.Count, migrationId, entityType);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve chunk increments for migration {MigrationId}, entity {EntityType}", 
                migrationId, entityType);
            throw;
        }
    }

    #endregion

    #region Aggregation Operations

    /// <inheritdoc />
    public async Task<Dictionary<string, EntityProgressSummary>> GetAggregatedProgressAsync(string migrationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

        try
        {
            var tableClient = await GetTableClientAsync();
            
            // Query all chunks for this migration with optimized projection
            var query = tableClient.QueryAsync<ChunkIncrementEvent>(
                filter: $"PartitionKey eq '{migrationId}'",
                select: new[] { 
                    "EntityType", "SuccessfulEntities", "FailedEntities", 
                    "SkippedEntities", "CancelledEntities", "ProcessingStartTime", 
                    "ProcessingEndTime", "ProcessingTimeMs" 
                },
                cancellationToken: cancellationToken);

            // Group chunks by entity type and aggregate
            var entityGroups = new Dictionary<string, List<ChunkIncrementEvent>>();
            
            await foreach (var chunk in query)
            {
                if (!entityGroups.ContainsKey(chunk.EntityType))
                    entityGroups[chunk.EntityType] = new List<ChunkIncrementEvent>();
                    
                entityGroups[chunk.EntityType].Add(chunk);
            }

            // Calculate aggregated progress for each entity type
            var result = new Dictionary<string, EntityProgressSummary>();
            
            foreach (var (entityType, chunks) in entityGroups)
            {
                result[entityType] = CalculateEntityProgressSummary(entityType, chunks);
            }

            _logger.LogDebug("Calculated aggregated progress for migration {MigrationId}: {EntityCount} entity types, {TotalChunks} total chunks",
                migrationId, result.Count, result.Values.Sum(s => s.TotalChunks));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate aggregated progress for migration {MigrationId}", migrationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<EntityProgressSummary> GetEntityProgressSummaryAsync(string migrationId, string entityType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));
        
        if (string.IsNullOrEmpty(entityType))
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));

        try
        {
            var chunks = await GetChunkIncrementsAsync(migrationId, entityType, cancellationToken);
            var summary = CalculateEntityProgressSummary(entityType, chunks);

            _logger.LogDebug("Calculated progress summary for {MigrationId}:{EntityType}: {Summary}", 
                migrationId, entityType, summary);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate progress summary for migration {MigrationId}, entity {EntityType}", 
                migrationId, entityType);
            throw;
        }
    }

    #endregion

    #region Health and Diagnostics

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        return await _tableInitializationService.IsTableHealthyAsync(TableName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IncrementEventsStatistics> GetStatisticsAsync(string? migrationId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var tableClient = await GetTableClientAsync();
            
            string filter = migrationId != null 
                ? $"PartitionKey eq '{migrationId}'"
                : string.Empty; // Query all if no migration specified

            var query = tableClient.QueryAsync<ChunkIncrementEvent>(
                filter: filter,
                select: new[] { "PartitionKey", "EntityType", "SuccessfulEntities", "FailedEntities" },
                cancellationToken: cancellationToken);

            var migrations = new HashSet<string>();
            var entityTypes = new HashSet<string>();
            var totalEvents = 0;
            var totalSuccessful = 0L;
            var totalFailed = 0L;

            await foreach (var chunk in query)
            {
                migrations.Add(chunk.PartitionKey);
                entityTypes.Add(chunk.EntityType);
                totalEvents++;
                totalSuccessful += chunk.SuccessfulEntities;
                totalFailed += chunk.FailedEntities;
            }

            var statistics = new IncrementEventsStatistics
            {
                MigrationId = migrationId,
                TotalEvents = totalEvents,
                UniqueMigrations = migrations.Count,
                UniqueEntityTypes = entityTypes.Count,
                TotalSuccessfulEntities = totalSuccessful,
                TotalFailedEntities = totalFailed,
                EstimatedStorageBytes = totalEvents * 500L, // Rough estimate: 500 bytes per event
                CalculatedAt = DateTime.UtcNow
            };

            _logger.LogDebug("Calculated increment events statistics: {Statistics}", statistics);
            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate increment events statistics");
            throw;
        }
    }

    #endregion

    #region Cleanup Operations

    /// <inheritdoc />
    public async Task<int> DeleteMigrationIncrementsAsync(string migrationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

        try
        {
            var tableClient = await GetTableClientAsync();
            var chunks = await GetChunkIncrementsAsync(migrationId, cancellationToken);
            
            var deletedCount = 0;
            
            // Delete in batches of 100 (Azure Table Storage limit)
            var batches = chunks.Chunk(100).ToList();
            
            foreach (var batch in batches)
            {
                var batchOperations = batch.Select(chunk => 
                    new TableTransactionAction(TableTransactionActionType.Delete, chunk)).ToList();

                if (batchOperations.Any())
                {
                    await tableClient.SubmitTransactionAsync(batchOperations, cancellationToken);
                    deletedCount += batchOperations.Count;
                }
            }

            _logger.LogInformation("Deleted {Count} chunk increment events for migration {MigrationId}", 
                deletedCount, migrationId);
            
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete chunk increment events for migration {MigrationId}", migrationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteOldIncrementsAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        try
        {
            var tableClient = await GetTableClientAsync();
            
            // Query for old events (this is not very efficient for large datasets, but works for cleanup)
            var query = tableClient.QueryAsync<ChunkIncrementEvent>(
                filter: $"CreatedAt lt datetime'{olderThan:yyyy-MM-ddTHH:mm:ss.fffZ}'",
                select: new[] { "PartitionKey", "RowKey" },
                cancellationToken: cancellationToken);

            var oldEvents = new List<ChunkIncrementEvent>();
            await foreach (var chunk in query)
            {
                oldEvents.Add(chunk);
            }

            var deletedCount = 0;
            
            // Group by partition key and delete in batches
            var partitionGroups = oldEvents.GroupBy(e => e.PartitionKey);
            
            foreach (var partitionGroup in partitionGroups)
            {
                var batches = partitionGroup.Chunk(100).ToList();
                
                foreach (var batch in batches)
                {
                    var batchOperations = batch.Select(chunk => 
                        new TableTransactionAction(TableTransactionActionType.Delete, chunk)).ToList();

                    if (batchOperations.Any())
                    {
                        await tableClient.SubmitTransactionAsync(batchOperations, cancellationToken);
                        deletedCount += batchOperations.Count;
                    }
                }
            }

            _logger.LogInformation("Deleted {Count} old chunk increment events older than {OlderThan}", 
                deletedCount, olderThan);
            
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete old chunk increment events older than {OlderThan}", olderThan);
            throw;
        }
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Gets the table client using the centralized table initialization service
    /// </summary>
    private async Task<TableClient> GetTableClientAsync(CancellationToken cancellationToken = default)
    {
        return await _tableInitializationService.GetTableClientAsync(TableName, cancellationToken);
    }

    /// <summary>
    /// Generates a unique RowKey to prevent write conflicts
    /// Format: {EntityType}-chunk-{ChunkNumber:D3}-{Timestamp}-{Sequence}
    /// </summary>
    private static string GenerateUniqueRowKey(string entityType, int chunkNumber)
    {
        lock (_sequenceLock)
        {
            _sequenceCounter++;
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var sequence = _sequenceCounter.ToString("D6");
            
            return $"{entityType}-chunk-{chunkNumber:D3}-{timestamp}-{sequence}";
        }
    }

    /// <summary>
    /// Writes a single chunk increment event with retry logic
    /// </summary>
    private async Task WriteWithRetryAsync(ChunkIncrementEvent incrementEvent, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        
        for (int attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    _logger.LogDebug("🔄 Retry attempt {Attempt} for chunk increment write: {RowKey}", 
                        attempt, incrementEvent.RowKey);
                    await Task.Delay(RetryDelays[attempt - 1], cancellationToken);
                }
                
                var tableClient = await GetTableClientAsync(cancellationToken);
                await tableClient.AddEntityAsync(incrementEvent, cancellationToken);
                
                if (attempt > 0)
                {
                    _logger.LogInformation("✅ Chunk increment write succeeded on retry {Attempt}: {RowKey}", 
                        attempt, incrementEvent.RowKey);
                }
                
                return; // Success
            }
            catch (RequestFailedException ex) when (IsTransientError(ex) && attempt < RetryDelays.Length)
            {
                lastException = ex;
                _logger.LogWarning("⚠️ Transient error on attempt {Attempt} for {RowKey}: {Error}", 
                    attempt + 1, incrementEvent.RowKey, ex.Message);
                continue;
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                // Conflict (duplicate RowKey) - this should be extremely rare with our generation strategy
                _logger.LogWarning("🚨 UNEXPECTED: Duplicate RowKey detected: {RowKey} - adding retry suffix", 
                    incrementEvent.RowKey);
                
                // Add retry suffix and try once more
                incrementEvent.RowKey += $"-retry-{DateTime.UtcNow.Ticks}";
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Non-transient error writing chunk increment: {RowKey} - giving up", 
                    incrementEvent.RowKey);
                return; // Don't retry non-transient errors
            }
        }
        
        _logger.LogWarning("⚠️ All retry attempts exhausted for chunk increment write: {RowKey}, Error: {Error}", 
            incrementEvent.RowKey, lastException?.Message);
        
        // Don't throw - increment write failures shouldn't break migration
    }

    /// <summary>
    /// Writes a batch of chunk increment events with retry logic
    /// </summary>
    private async Task WriteBatchWithRetryAsync(List<ChunkIncrementEvent> incrementEvents, CancellationToken cancellationToken)
    {
        try
        {
            var tableClient = await GetTableClientAsync();
            
            // All events in a batch must have the same partition key for Azure Table Storage
            var partitionGroups = incrementEvents.GroupBy(e => e.PartitionKey);
            
            foreach (var partitionGroup in partitionGroups)
            {
                var batchOperations = partitionGroup.Select(incrementEvent => 
                    new TableTransactionAction(TableTransactionActionType.Add, incrementEvent)).ToList();

                await tableClient.SubmitTransactionAsync(batchOperations, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch write failed for {Count} events - falling back to individual writes", 
                incrementEvents.Count);
            
            // Fallback to individual writes
            foreach (var incrementEvent in incrementEvents)
            {
                await WriteWithRetryAsync(incrementEvent, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Calculates aggregated progress summary for an entity type
    /// </summary>
    private static EntityProgressSummary CalculateEntityProgressSummary(string entityType, List<ChunkIncrementEvent> chunks)
    {
        if (!chunks.Any())
        {
            return new EntityProgressSummary { EntityType = entityType };
        }

        return new EntityProgressSummary
        {
            EntityType = entityType,
            TotalChunks = chunks.Count,
            TotalSuccessful = chunks.Sum(c => c.SuccessfulEntities),
            TotalFailed = chunks.Sum(c => c.FailedEntities),
            TotalSkipped = chunks.Sum(c => c.SkippedEntities),
            TotalCancelled = chunks.Sum(c => c.CancelledEntities),
            FirstChunkStartTime = chunks.Min(c => c.ProcessingStartTime),
            LastChunkEndTime = chunks.Max(c => c.ProcessingEndTime),
            TotalProcessingTime = TimeSpan.FromMilliseconds(chunks.Sum(c => c.ProcessingTimeMs))
        };
    }

    /// <summary>
    /// Determines if an Azure Table Storage error is transient and should be retried
    /// </summary>
    private static bool IsTransientError(RequestFailedException ex)
    {
        return ex.Status == 429 ||  // Throttling
               ex.Status == 500 ||  // Internal Server Error
               ex.Status == 502 ||  // Bad Gateway
               ex.Status == 503 ||  // Service Unavailable
               ex.Status == 504;    // Gateway Timeout
    }

    #endregion

    #region IDisposable Implementation

    /// <summary>
    /// Disposes of resources used by the IncrementEventsService
    /// </summary>
    public void Dispose()
    {
        // No resources to dispose - table management is handled by the centralized service
        GC.SuppressFinalize(this);
    }

    #endregion
}