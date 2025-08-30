using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Azure Table Storage entity for tracking sequential RowNumber assignment
/// Enables atomic counter functionality for EntityMapping creation with composite RowKey
/// 
/// Design Principles:
/// - Thread-safe atomic increments using ETag optimistic concurrency
/// - Per-migration-entity-type counters for isolation
/// - Sequential RowNumber assignment for predictable range queries
/// - High-performance counter operations (target: >100 requests/second)
/// </summary>
public class RowNumberCounter : ITableEntity
{
    #region Azure Table Storage Required Fields

    /// <summary>
    /// Partition key: "{MigrationId}_{EntityType}" (e.g., "migration123_products")
    /// Groups counters by migration and entity type for efficient queries
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key: "counter" (constant for all counters)
    /// Simple RowKey since we only need one counter per partition
    /// </summary>
    public string RowKey { get; set; } = "counter";

    /// <summary>
    /// ETag for optimistic concurrency control (Azure Table Storage managed)
    /// Critical for atomic counter operations under concurrent access
    /// </summary>
    public ETag ETag { get; set; }

    /// <summary>
    /// Timestamp for Azure Table Storage (automatically managed)
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    #endregion

    #region Counter Data

    /// <summary>
    /// Current counter value - last assigned RowNumber
    /// Next RowNumber to assign will be CurrentValue + 1
    /// </summary>
    [Required]
    public long CurrentValue { get; set; } = 0;

    /// <summary>
    /// Migration ID this counter belongs to (redundant for queries, same as PartitionKey prefix)
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type this counter tracks (e.g., "products", "categories")
    /// </summary>
    [Required]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// When the counter was created
    /// </summary>
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the counter was last updated (last RowNumber assigned)
    /// </summary>
    [Required]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total number of RowNumbers allocated by this counter
    /// Useful for statistics and validation
    /// </summary>
    public long TotalAllocated { get; set; } = 0;

    /// <summary>
    /// Number of concurrent allocation conflicts (ETag failures)
    /// Monitoring metric for performance optimization
    /// </summary>
    public long ConcurrencyConflicts { get; set; } = 0;

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a new counter for the specified migration and entity type
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityType">Entity type (e.g., "products")</param>
    /// <returns>New counter instance</returns>
    public static RowNumberCounter Create(string migrationId, string entityType)
    {
        return new RowNumberCounter
        {
            PartitionKey = $"{migrationId}_{entityType}",
            RowKey = "counter",
            MigrationId = migrationId,
            EntityType = entityType,
            CurrentValue = 0,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            TotalAllocated = 0,
            ConcurrencyConflicts = 0
        };
    }

    /// <summary>
    /// Gets the next RowNumber and increments the counter
    /// This method should only be called within atomic operations with ETag validation
    /// </summary>
    /// <returns>Next sequential RowNumber</returns>
    public long GetNextRowNumber()
    {
        CurrentValue++;
        TotalAllocated++;
        LastUpdated = DateTime.UtcNow;
        return CurrentValue;
    }

    /// <summary>
    /// Records a concurrency conflict for monitoring purposes
    /// </summary>
    public void RecordConcurrencyConflict()
    {
        ConcurrencyConflicts++;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets a range of RowNumbers for batch operations
    /// </summary>
    /// <param name="count">Number of RowNumbers to allocate</param>
    /// <returns>Start and end RowNumbers (inclusive)</returns>
    public (long StartRowNumber, long EndRowNumber) AllocateRange(int count)
    {
        if (count <= 0)
            throw new ArgumentException("Count must be positive", nameof(count));

        var startRowNumber = CurrentValue + 1;
        CurrentValue += count;
        TotalAllocated += count;
        LastUpdated = DateTime.UtcNow;
        
        return (startRowNumber, CurrentValue);
    }

    #endregion

    #region Validation

    /// <summary>
    /// Validates the counter state
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(MigrationId) &&
               !string.IsNullOrEmpty(EntityType) &&
               !string.IsNullOrEmpty(PartitionKey) &&
               PartitionKey.Equals($"{MigrationId}_{EntityType}", StringComparison.OrdinalIgnoreCase) &&
               RowKey.Equals("counter", StringComparison.OrdinalIgnoreCase) &&
               CurrentValue >= 0 &&
               TotalAllocated >= 0 &&
               ConcurrencyConflicts >= 0 &&
               CreatedAt <= DateTime.UtcNow &&
               LastUpdated >= CreatedAt;
    }

    #endregion

    #region ToString Override

    /// <summary>
    /// Returns a string representation of the counter for debugging
    /// </summary>
    public override string ToString()
    {
        return $"RowNumberCounter[{MigrationId}/{EntityType}]: Current={CurrentValue}, " +
               $"Allocated={TotalAllocated}, Conflicts={ConcurrencyConflicts}, " +
               $"Updated={LastUpdated:yyyy-MM-dd HH:mm:ss}";
    }

    #endregion
}
