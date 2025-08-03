using BigCommerce.Migration.Core.Models;
using System.Collections.Generic;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Repository interface for cancellation token operations following Interface Segregation Principle (ISP)
/// 
/// <para><strong>Responsibility:</strong> Handles only cancellation token management</para>
/// <para><strong>Segregation:</strong> Extracted from IMigrationStorageService to achieve single responsibility</para>
/// <para><strong>Purpose:</strong> Manages migration cancellation tokens for graceful shutdown coordination</para>
/// 
/// <example>
/// Usage:
/// <code>
/// var token = await repository.CreateAsync("migration-123", "User requested cancellation");
/// var existing = await repository.GetAsync("migration-123");
/// if (existing?.IsCancelled == true) {
///     // Handle cancellation logic
/// }
/// </code>
/// </example>
/// </summary>
public interface ICancellationTokenRepository
{
    /// <summary>
    /// Creates a new cancellation token for a migration to initiate graceful shutdown
    /// </summary>
    /// <param name="migrationId">Unique migration identifier (GUID format)</param>
    /// <param name="reason">Human-readable cancellation reason for audit purposes</param>
    /// <returns>Created cancellation token with unique ID and timestamps</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    /// <exception cref="ArgumentException">Thrown when reason is null, empty, or exceeds maximum length</exception>
    /// <exception cref="InvalidOperationException">Thrown when cancellation token already exists for the migration</exception>
    Task<CancellationTokenEntry> CreateAsync(string migrationId, string reason);

    /// <summary>
    /// Retrieves an existing cancellation token for a specific migration
    /// </summary>
    /// <param name="migrationId">Unique migration identifier to query cancellation token for</param>
    /// <returns>Cancellation token if found, null if no cancellation token exists</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    Task<CancellationTokenEntry?> GetAsync(string migrationId);

    /// <summary>
    /// Updates an existing cancellation token with status changes or additional metadata
    /// </summary>
    /// <param name="token">Cancellation token to update. Must include valid ID and ETag</param>
    /// <returns>Updated cancellation token with new ETag and LastModified timestamp</returns>
    /// <exception cref="ArgumentNullException">Thrown when token is null</exception>
    /// <exception cref="ArgumentException">Thrown when token has invalid ID or required fields</exception>
    /// <exception cref="InvalidOperationException">Thrown when token doesn't exist or ETag conflict</exception>
    Task<CancellationTokenEntry> UpdateAsync(CancellationTokenEntry token);

    /// <summary>
    /// Permanently deletes a cancellation token when migration is completed or reset
    /// </summary>
    /// <param name="migrationId">Unique migration identifier to delete cancellation token for</param>
    /// <returns>True if cancellation token was deleted, false if token was not found</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    Task<bool> DeleteAsync(string migrationId);

    // ========================================
    // LIVE CANCELLATION EXTENSIONS (Task 7.1)
    // ========================================
    // The following methods extend the interface for multi-scope cancellation support
    // while maintaining backward compatibility with existing implementations.

    /// <summary>
    /// Creates a new scoped cancellation token for multi-level cancellation support.
    /// Extends the original CreateAsync method with scope-specific parameters.
    /// </summary>
    /// <param name="migrationId">Unique migration identifier (GUID format)</param>
    /// <param name="scope">Cancellation scope level (Migration, EntityType, Batch, Store)</param>
    /// <param name="reason">Human-readable cancellation reason for audit purposes</param>
    /// <param name="requestedBy">User or system that requested the cancellation</param>
    /// <param name="entityType">Entity type when scope is EntityType (e.g., "categories", "products")</param>
    /// <param name="batchId">Batch identifier when scope is Batch</param>
    /// <param name="storeId">Store identifier when scope is Store</param>
    /// <returns>Created enhanced cancellation token with scope details</returns>
    /// <exception cref="ArgumentException">Thrown when required parameters are invalid</exception>
    /// <exception cref="InvalidOperationException">Thrown when cancellation token already exists for the scope</exception>
    Task<EnhancedCancellationTokenEntry> CreateScopedAsync(
        string migrationId,
        CancellationScope scope,
        string reason,
        string requestedBy,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null);

    /// <summary>
    /// Retrieves all active cancellation tokens for a specific migration across all scopes.
    /// Used for comprehensive cancellation status reporting.
    /// </summary>
    /// <param name="migrationId">Unique migration identifier to query</param>
    /// <returns>List of all active enhanced cancellation tokens for the migration</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    Task<List<EnhancedCancellationTokenEntry>> GetActiveByMigrationAsync(string migrationId);

    /// <summary>
    /// Checks if there is an active cancellation for the specified migration and scope.
    /// Optimized for frequent checks during processing with &lt;50ms response time target.
    /// </summary>
    /// <param name="migrationId">Migration identifier to check</param>
    /// <param name="scope">Cancellation scope to check</param>
    /// <param name="entityType">Entity type when scope is EntityType</param>
    /// <param name="batchId">Batch identifier when scope is Batch</param>
    /// <param name="storeId">Store identifier when scope is Store</param>
    /// <returns>True if active cancellation exists for the scope, false otherwise</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    Task<bool> IsActiveCancellationAsync(
        string migrationId,
        CancellationScope scope,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null);

    /// <summary>
    /// Updates an enhanced cancellation token entry (e.g., marking as processed).
    /// Extends the original UpdateAsync method for scoped cancellation tokens.
    /// </summary>
    /// <param name="token">Enhanced cancellation token to update</param>
    /// <returns>Updated enhanced cancellation token with new timestamps</returns>
    /// <exception cref="ArgumentNullException">Thrown when token is null</exception>
    /// <exception cref="ArgumentException">Thrown when token has invalid required fields</exception>
    Task<EnhancedCancellationTokenEntry> UpdateScopedAsync(EnhancedCancellationTokenEntry token);

    /// <summary>
    /// Deletes a specific scoped cancellation token.
    /// More granular than the original DeleteAsync method.
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="scope">Cancellation scope to delete</param>
    /// <param name="entityType">Entity type when scope is EntityType</param>
    /// <param name="batchId">Batch identifier when scope is Batch</param>
    /// <param name="storeId">Store identifier when scope is Store</param>
    /// <returns>True if cancellation token was deleted, false if not found</returns>
    /// <exception cref="ArgumentException">Thrown when required parameters are invalid</exception>
    Task<bool> DeleteScopedAsync(
        string migrationId,
        CancellationScope scope,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null);

    // ========================================
    // HYBRID DISTRIBUTED STORAGE EXTENSIONS (Task 7.3)
    // ========================================
    // The following methods implement the cost-effective hybrid approach:
    // Layer 1: Azure Table Storage (persistence) - $0 (existing)
    // Layer 2: Azure Storage Queue (propagation) - ~$0.40/month  
    // Layer 3: SignalR Hub (real-time UI) - $0 (existing)
    // Layer 4: In-Memory Cache (performance) - $0 (built-in)
    // Total Cost: ~$0.40/month vs $50+/month for Service Bus (125x cheaper!)

    /// <summary>
    /// Propagates cancellation across all Azure Function instances using hybrid 4-layer approach.
    /// 
    /// <para><strong>4-Layer Architecture:</strong></para>
    /// <para>Layer 1: Azure Table Storage (persistence and source of truth)</para>
    /// <para>Layer 2: Azure Storage Queue (instant propagation to other instances)</para>
    /// <para>Layer 3: SignalR Hub (real-time UI updates)</para>
    /// <para>Layer 4: In-Memory Cache (invalidation for performance)</para>
    /// 
    /// <para><strong>Performance Target:</strong> Less than 100ms multi-instance propagation</para>
    /// <para><strong>Cost:</strong> Approximately $0.40/month vs $50+/month for Azure Service Bus</para>
    /// </summary>
    /// <param name="migrationId">Migration identifier (GUID format)</param>
    /// <param name="scope">Cancellation scope level (Migration, EntityType, Batch, Store)</param>
    /// <param name="reason">Human-readable cancellation reason for audit purposes</param>
    /// <param name="requestedBy">User or system that requested the cancellation</param>
    /// <returns>Task that completes when cancellation has been propagated to all layers</returns>
    /// <exception cref="ArgumentException">Thrown when required parameters are invalid</exception>
    /// <exception cref="InvalidOperationException">Thrown when propagation fails</exception>
    Task PropagateToAllInstancesAsync(string migrationId, CancellationScope scope, 
        string reason, string requestedBy);

    /// <summary>
    /// Fast cancellation check with cache-first strategy for optimal performance.
    /// 
    /// <para><strong>Performance Strategy:</strong></para>
    /// <para>1. Check in-memory cache first (fastest, less than 10ms)</para>
    /// <para>2. If cache miss, check Azure Table Storage and cache result (less than 100ms)</para>
    /// 
    /// <para><strong>Performance Target:</strong> Less than 50ms average response time</para>
    /// <para><strong>Cache Duration:</strong> 5 minutes with automatic invalidation</para>
    /// </summary>
    /// <param name="migrationId">Migration identifier to check</param>
    /// <param name="scope">Cancellation scope to check</param>
    /// <param name="entityType">Entity type when scope is EntityType (e.g., "categories", "products")</param>
    /// <param name="batchId">Batch identifier when scope is Batch</param>
    /// <param name="storeId">Store identifier when scope is Store</param>
    /// <returns>True if active cancellation exists for the scope, false otherwise</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    Task<bool> IsFastCancellationAsync(string migrationId, CancellationScope scope,
        string? entityType = null, string? batchId = null, string? storeId = null);

    /// <summary>
    /// Invalidates local cache for cancellation state across all related cache entries.
    /// 
    /// <para><strong>Usage:</strong> Called when receiving queue notifications from other instances</para>
    /// <para><strong>Purpose:</strong> Ensures cache consistency across multi-instance deployments</para>
    /// <para><strong>Performance:</strong> Immediate local operation, less than 1ms execution time</para>
    /// </summary>
    /// <param name="migrationId">Migration identifier for cache invalidation</param>
    /// <param name="scope">Cancellation scope to invalidate</param>
    /// <returns>Task that completes when cache invalidation is finished</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null, empty, or invalid format</exception>
    Task InvalidateCacheAsync(string migrationId, CancellationScope scope);
} 