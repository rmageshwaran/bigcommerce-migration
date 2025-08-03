using BigCommerce.Migration.Core.Models;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for managing live, real-time cancellation operations across the migration system.
/// Provides multi-level cancellation support with instant propagation capabilities.
/// 
/// <para><strong>Responsibility:</strong> Real-time cancellation management and propagation</para>
/// <para><strong>Performance:</strong> &lt;50ms cancellation checks, &lt;5s propagation time</para>
/// <para><strong>Scopes:</strong> Migration, EntityType, Batch, Store level cancellations</para>
/// 
/// <example>
/// Usage:
/// <code>
/// // Cancel entire migration
/// var result = await manager.CancelAsync("migration-123", CancellationScope.Migration, "User requested");
/// 
/// // Check if specific entity type is cancelled
/// var isCancelled = await manager.IsCancelledAsync("migration-123", CancellationScope.EntityType, "categories");
/// 
/// // Get comprehensive status
/// var status = await manager.GetCancellationStatusAsync("migration-123");
/// </code>
/// </example>
/// </summary>
public interface ILiveCancellationManager
{
    /// <summary>
    /// Initiates a cancellation request for the specified migration and scope.
    /// Creates cancellation entry, sends real-time notifications, and propagates to all instances.
    /// </summary>
    /// <param name="migrationId">Unique migration identifier to cancel</param>
    /// <param name="scope">Level of cancellation (Migration, EntityType, Batch, Store)</param>
    /// <param name="reason">Human-readable reason for cancellation</param>
    /// <param name="entityType">Entity type when scope is EntityType (e.g., "categories", "products")</param>
    /// <param name="batchId">Batch identifier when scope is Batch</param>
    /// <param name="storeId">Store identifier when scope is Store</param>
    /// <returns>Cancellation result indicating success/failure and details</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId or reason is null/empty</exception>
    /// <exception cref="ArgumentException">Thrown when required scope parameters are missing</exception>
    Task<CancellationResult> CancelAsync(
        string migrationId, 
        CancellationScope scope, 
        string reason,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null);

    /// <summary>
    /// Checks if cancellation is active for the specified migration and scope.
    /// Fast lookup operation optimized for frequent calls during processing.
    /// </summary>
    /// <param name="migrationId">Migration identifier to check</param>
    /// <param name="scope">Cancellation scope to check (defaults to Migration level)</param>
    /// <param name="entityType">Entity type to check when scope is EntityType</param>
    /// <param name="batchId">Batch identifier to check when scope is Batch</param>
    /// <param name="storeId">Store identifier to check when scope is Store</param>
    /// <returns>True if cancellation is active for the specified scope, false otherwise</returns>
    /// <remarks>
    /// Returns false on errors to follow continue-on-error policy.
    /// Performance target: &lt;50ms response time.
    /// </remarks>
    Task<bool> IsCancelledAsync(
        string migrationId,
        CancellationScope scope = CancellationScope.Migration,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null);

    /// <summary>
    /// Retrieves comprehensive cancellation status for a migration including all active scopes.
    /// Provides complete view of cancellation state for monitoring and dashboard display.
    /// </summary>
    /// <param name="migrationId">Migration identifier to get status for</param>
    /// <returns>Complete cancellation status including active scopes, counts, and timestamps</returns>
    /// <exception cref="ArgumentException">Thrown when migrationId is null or empty</exception>
    Task<CancellationStatus> GetCancellationStatusAsync(string migrationId);

    /// <summary>
    /// Propagates cancellation notification to all active Azure Function instances.
    /// Uses SignalR and Azure Service Bus for instant distribution across the system.
    /// </summary>
    /// <param name="migrationId">Migration identifier being cancelled</param>
    /// <param name="scope">Scope level of the cancellation</param>
    /// <param name="entityType">Entity type when scope is EntityType</param>
    /// <param name="batchId">Batch identifier when scope is Batch</param>
    /// <param name="storeId">Store identifier when scope is Store</param>
    /// <returns>Task representing the async propagation operation</returns>
    /// <remarks>
    /// Uses continue-on-error policy - propagation failures are logged but don't throw.
    /// Performance target: &lt;5 seconds end-to-end propagation.
    /// </remarks>
    Task PropagateToAllInstancesAsync(
        string migrationId, 
        CancellationScope scope,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null);

    /// <summary>
    /// Checks if any higher-level cancellation scope is active that would affect the specified scope.
    /// For example, if Migration is cancelled, all EntityType, Batch, and Store scopes are effectively cancelled.
    /// </summary>
    /// <param name="migrationId">Migration identifier to check</param>
    /// <param name="currentScope">Current scope being checked</param>
    /// <param name="entityType">Entity type context for hierarchical checking</param>
    /// <param name="batchId">Batch identifier context for hierarchical checking</param>
    /// <param name="storeId">Store identifier context for hierarchical checking</param>
    /// <returns>True if any higher-level cancellation is active, false otherwise</returns>
    /// <remarks>
    /// Hierarchy: Migration > EntityType > Batch > Store
    /// If Migration is cancelled, all other scopes are considered cancelled.
    /// </remarks>
    Task<bool> IsHierarchicallyCancelledAsync(
        string migrationId,
        CancellationScope currentScope,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null);

    /// <summary>
    /// Marks a cancellation as processed/completed and updates the entry status.
    /// Used when cancellation has been fully processed and resources cleaned up.
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="scope">Scope that was processed</param>
    /// <param name="entityType">Entity type when scope is EntityType</param>
    /// <param name="batchId">Batch identifier when scope is Batch</param>
    /// <param name="storeId">Store identifier when scope is Store</param>
    /// <returns>Task representing the async operation</returns>
    Task MarkCancellationProcessedAsync(
        string migrationId,
        CancellationScope scope,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null);
}