using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Defines the scope levels for cancellation operations in the migration system.
/// Supports hierarchical cancellation from broad (Migration) to specific (Store).
/// </summary>
public enum CancellationScope
{
    /// <summary>
    /// Cancel the entire migration - stops all entity types, batches, and stores
    /// </summary>
    Migration = 0,

    /// <summary>
    /// Cancel processing for a specific entity type (e.g., categories, products)
    /// Other entity types continue processing
    /// </summary>
    EntityType = 1,

    /// <summary>
    /// Cancel processing for a specific batch
    /// Other batches within the same entity type continue
    /// </summary>
    Batch = 2,

    /// <summary>
    /// Cancel processing for a specific store
    /// Other stores continue processing
    /// </summary>
    Store = 3
}

/// <summary>
/// Enhanced cancellation token entry supporting multi-level cancellation scopes.
/// Extends the original CancellationTokenEntry with hierarchical scope support.
/// </summary>
public class EnhancedCancellationTokenEntry
{
    /// <summary>
    /// Unique identifier for the migration being cancelled
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// The scope level of this cancellation request
    /// </summary>
    [Required]
    public CancellationScope Scope { get; set; }

    /// <summary>
    /// Entity type being cancelled (required when Scope = EntityType)
    /// Examples: "categories", "products", "brands", "variants"
    /// </summary>
    [StringLength(50)]
    public string? EntityType { get; set; }

    /// <summary>
    /// Batch identifier being cancelled (required when Scope = Batch)
    /// </summary>
    [StringLength(100)]
    public string? BatchId { get; set; }

    /// <summary>
    /// Store identifier being cancelled (required when Scope = Store)
    /// </summary>
    [StringLength(100)]
    public string? StoreId { get; set; }

    /// <summary>
    /// UTC timestamp when the cancellation was requested
    /// </summary>
    [Required]
    public DateTime RequestedAt { get; set; }

    /// <summary>
    /// User or system that requested the cancellation
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable reason for the cancellation
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if this cancellation is currently active
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// UTC timestamp when the cancellation was processed/completed
    /// Null if still in progress
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Validates the cancellation entry based on scope requirements
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValid()
    {
        // Basic validation
        if (string.IsNullOrWhiteSpace(MigrationId) ||
            string.IsNullOrWhiteSpace(RequestedBy) ||
            string.IsNullOrWhiteSpace(Reason))
        {
            return false;
        }

        // Scope-specific validation
        return Scope switch
        {
            CancellationScope.Migration => true, // No additional requirements
            CancellationScope.EntityType => !string.IsNullOrWhiteSpace(EntityType),
            CancellationScope.Batch => !string.IsNullOrWhiteSpace(BatchId),
            CancellationScope.Store => !string.IsNullOrWhiteSpace(StoreId),
            _ => false
        };
    }
}

/// <summary>
/// Result of a cancellation operation indicating success or failure details
/// </summary>
public class CancellationResult
{
    /// <summary>
    /// Indicates if the cancellation request was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Migration ID that was targeted for cancellation
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Scope level of the cancellation request
    /// </summary>
    public CancellationScope Scope { get; set; }

    /// <summary>
    /// UTC timestamp when the cancellation was requested
    /// </summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the cancellation was processed
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Human-readable message describing the result
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Detailed error information if the cancellation failed
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// Estimated time for the cancellation to complete (in seconds)
    /// </summary>
    public int? EstimatedCompletionTimeSeconds { get; set; }

    /// <summary>
    /// Unique identifier for this cancellation (typically the migration ID plus scope)
    /// </summary>
    public string? CancellationId { get; set; }
}

/// <summary>
/// Current cancellation status for a migration including all active cancellations
/// </summary>
public class CancellationStatus
{
    /// <summary>
    /// Migration ID being queried
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if there are any active cancellation requests
    /// </summary>
    public bool HasActiveCancellation { get; set; }

    /// <summary>
    /// List of all active cancellation scopes for this migration
    /// </summary>
    public List<CancellationScope> ActiveScopes { get; set; } = new();

    /// <summary>
    /// Total number of cancellation requests (active and completed) for this migration
    /// </summary>
    public int TotalCancellationRequests { get; set; }

    /// <summary>
    /// UTC timestamp of the most recent cancellation request
    /// </summary>
    public DateTime? LastCancellationAt { get; set; }

    /// <summary>
    /// Details of all active cancellation entries
    /// </summary>
    public List<EnhancedCancellationTokenEntry> ActiveCancellations { get; set; } = new();

    /// <summary>
    /// Primary reason for the most recent cancellation (convenience property)
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Who requested the most recent cancellation (convenience property)
    /// </summary>
    public string? RequestedBy { get; set; }

    /// <summary>
    /// When the most recent cancellation was requested (convenience property)
    /// </summary>
    public DateTime? RequestedAt { get; set; }
}

/// <summary>
/// Request model for cancellation check operations
/// </summary>
public class CancellationCheckRequest
{
    /// <summary>
    /// Migration ID to check for cancellation
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Scope level to check for cancellation
    /// </summary>
    public CancellationScope Scope { get; set; } = CancellationScope.Migration;

    /// <summary>
    /// Entity type to check (when Scope = EntityType)
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Batch ID to check (when Scope = Batch)
    /// </summary>
    public string? BatchId { get; set; }

    /// <summary>
    /// Store ID to check (when Scope = Store)
    /// </summary>
    public string? StoreId { get; set; }
}

/// <summary>
/// Result of a cancellation check operation, including performance metrics and detailed status.
/// </summary>
public class CancellationCheckResult
{
    /// <summary>
    /// The migration ID that was checked
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// The scope that was checked for cancellation
    /// </summary>
    public CancellationScope Scope { get; set; }

    /// <summary>
    /// Indicates if cancellation is active for the requested scope
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// When the check was performed
    /// </summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// Response time in milliseconds (target: less than 1000ms)
    /// </summary>
    public int ResponseTimeMs { get; set; }

    /// <summary>
    /// Whether the check operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the check failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Detailed cancellation status if cancellation was detected
    /// </summary>
    public CancellationStatus? CancellationDetails { get; set; }

    /// <summary>
    /// Reason for the cancellation (if cancelled) - legacy compatibility
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// UTC timestamp when the cancellation was requested - legacy compatibility
    /// </summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Who requested the cancellation - legacy compatibility
    /// </summary>
    public string? RequestedBy { get; set; }
}



/// <summary>
/// Request for processing a cancellation operation.
/// </summary>
public class CancellationProcessRequest
{
    /// <summary>
    /// Unique identifier for the migration to cancel
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// The scope level for this cancellation
    /// </summary>
    public CancellationScope Scope { get; set; }

    /// <summary>
    /// Reason for the cancellation (required for audit trail)
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Who requested the cancellation (user ID, system, etc.)
    /// </summary>
    public string? RequestedBy { get; set; }

    /// <summary>
    /// Entity type (required for EntityType, Batch, and Store scopes)
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Batch ID (required for Batch and Store scopes)
    /// </summary>
    public string? BatchId { get; set; }

    /// <summary>
    /// Store ID (required for Store scope)
    /// </summary>
    public string? StoreId { get; set; }

    /// <summary>
    /// Whether to cascade cancellation to child scopes
    /// </summary>
    public bool CascadeToChildren { get; set; } = false;

    /// <summary>
    /// Optional additional context for the cancellation
    /// </summary>
    public Dictionary<string, object>? AdditionalContext { get; set; }
}

/// <summary>
/// Result of a cancellation processing operation.
/// </summary>
public class CancellationProcessResult
{
    /// <summary>
    /// The migration ID that was processed
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// The scope that was cancelled
    /// </summary>
    public CancellationScope Scope { get; set; }

    /// <summary>
    /// Whether the cancellation processing was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if processing failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When the cancellation was processed
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public int ProcessingTimeMs { get; set; }

    /// <summary>
    /// Number of instances the cancellation was propagated to
    /// </summary>
    public int PropagatedInstances { get; set; }

    /// <summary>
    /// Unique identifier for the created cancellation
    /// </summary>
    public string? CancellationId { get; set; }

    /// <summary>
    /// Number of cascaded cancellations created (if cascade was requested)
    /// </summary>
    public int CascadedCancellations { get; set; }

    /// <summary>
    /// Whether propagation to all instances was completed successfully
    /// </summary>
    public bool PropagationCompleted { get; set; }

    /// <summary>
    /// Error message if propagation failed (processing can still succeed)
    /// </summary>
    public string? PropagationError { get; set; }

    /// <summary>
    /// Reason for the cancellation (for backward compatibility)
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Who requested the cancellation (for backward compatibility)
    /// </summary>
    public string? RequestedBy { get; set; }
}

/// <summary>
/// Request for batch cancellation processing.
/// </summary>
public class BatchCancellationProcessRequest
{
    /// <summary>
    /// List of individual cancellation requests to process
    /// </summary>
    [Required]
    public List<CancellationProcessRequest> CancellationRequests { get; set; } = new();

    /// <summary>
    /// Optional batch identifier for tracking
    /// </summary>
    public string? BatchId { get; set; }

    /// <summary>
    /// Who requested this batch cancellation
    /// </summary>
    public string? RequestedBy { get; set; }

    /// <summary>
    /// Alias for CancellationRequests for backward compatibility with tests
    /// </summary>
    public List<CancellationProcessRequest> Requests
    {
        get => CancellationRequests;
        set => CancellationRequests = value;
    }
}

/// <summary>
/// Result of a batch cancellation processing operation.
/// </summary>
public class BatchCancellationProcessResult
{
    /// <summary>
    /// Total number of cancellation requests in the batch
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// Number of successfully processed cancellations
    /// </summary>
    public int SuccessfulCancellations { get; set; }

    /// <summary>
    /// Number of failed cancellation processing attempts
    /// </summary>
    public int FailedCancellations { get; set; }

    /// <summary>
    /// When the batch processing started
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Total time to process the entire batch in milliseconds
    /// </summary>
    public int TotalProcessingTimeMs { get; set; }

    /// <summary>
    /// Whether the entire batch was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Individual results for each cancellation request
    /// </summary>
    public List<CancellationProcessResult> Results { get; set; } = new();

    /// <summary>
    /// Error message if batch processing failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Request for cleanup operations after cancellation.
/// </summary>
public class CleanupRequest
{
    /// <summary>
    /// Unique identifier for the migration to clean up
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// The scope level for this cleanup
    /// </summary>
    public CancellationScope Scope { get; set; }

    /// <summary>
    /// Entity type (for EntityType, Batch, and Store scopes)
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Batch ID (for Batch and Store scopes)
    /// </summary>
    public string? BatchId { get; set; }

    /// <summary>
    /// Store ID (for Store scope)
    /// </summary>
    public string? StoreId { get; set; }

    /// <summary>
    /// Whether to perform deep cleanup (resource-intensive operations)
    /// </summary>
    public bool DeepCleanup { get; set; } = false;

    /// <summary>
    /// Whether to force cleanup even if some operations fail
    /// </summary>
    public bool ForceCleanup { get; set; } = false;

    /// <summary>
    /// Optional additional context for cleanup operations
    /// </summary>
    public Dictionary<string, object>? AdditionalContext { get; set; }
}

/// <summary>
/// Result of a cleanup operation.
/// </summary>
public class CleanupResult
{
    /// <summary>
    /// The migration ID that was cleaned up
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// The scope that was cleaned up
    /// </summary>
    public CancellationScope Scope { get; set; }

    /// <summary>
    /// When the cleanup started
    /// </summary>
    public DateTime CleanupStartedAt { get; set; }

    /// <summary>
    /// When the cleanup completed
    /// </summary>
    public DateTime? CleanupCompletedAt { get; set; }

    /// <summary>
    /// Total cleanup time in milliseconds
    /// </summary>
    public int CleanupTimeMs { get; set; }

    /// <summary>
    /// Whether the cleanup was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if cleanup failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// List of cleanup steps that were performed
    /// </summary>
    public List<string> CleanupSteps { get; set; } = new();

    /// <summary>
    /// When the cleanup was completed (convenience property for tests)
    /// </summary>
    public DateTime CleanedAt 
    { 
        get => CleanupCompletedAt ?? CleanupStartedAt;
        set => CleanupCompletedAt = value;
    }

    /// <summary>
    /// Whether system resources were successfully freed during cleanup
    /// </summary>
    public bool ResourcesFreed { get; set; }

    /// <summary>
    /// Whether force cleanup was performed and completed
    /// </summary>
    public bool ForceCleanupPerformed { get; set; }

    /// <summary>
    /// Additional details about the cleanup operation
    /// </summary>
    public Dictionary<string, object> CleanupDetails { get; set; } = new();
}

/// <summary>
/// Request for batch cleanup operations.
/// </summary>
public class BatchCleanupRequest
{
    /// <summary>
    /// List of individual cleanup requests to process
    /// </summary>
    [Required]
    public List<CleanupRequest> CleanupRequests { get; set; } = new();

    /// <summary>
    /// Optional batch identifier for tracking
    /// </summary>
    public string? BatchId { get; set; }

    /// <summary>
    /// Who requested this batch cleanup
    /// </summary>
    public string? RequestedBy { get; set; }

    /// <summary>
    /// Alias for CleanupRequests for backward compatibility with tests
    /// </summary>
    public List<CleanupRequest> Requests
    {
        get => CleanupRequests;
        set => CleanupRequests = value;
    }
}

/// <summary>
/// Result of a batch cleanup operation.
/// </summary>
public class BatchCleanupResult
{
    /// <summary>
    /// Total number of cleanup requests in the batch
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// Number of successfully completed cleanups
    /// </summary>
    public int SuccessfulCleanups { get; set; }

    /// <summary>
    /// Number of failed cleanup attempts
    /// </summary>
    public int FailedCleanups { get; set; }

    /// <summary>
    /// When the batch cleanup started
    /// </summary>
    public DateTime CleanupStartedAt { get; set; }

    /// <summary>
    /// When the batch cleanup completed
    /// </summary>
    public DateTime CleanupCompletedAt { get; set; }

    /// <summary>
    /// Total time to complete the entire batch cleanup in milliseconds
    /// </summary>
    public int TotalCleanupTimeMs { get; set; }

    /// <summary>
    /// Whether the entire batch cleanup was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Individual results for each cleanup request
    /// </summary>
    public List<CleanupResult> Results { get; set; } = new();

    /// <summary>
    /// Error message if batch cleanup failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Enhanced request model for external cancellation check operations with multi-scope support.
/// </summary>
public class EnhancedCheckExternalCancellationRequest
{
    /// <summary>
    /// Migration ID to check for external cancellation
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Scope level to check for cancellation
    /// </summary>
    public CancellationScope Scope { get; set; } = CancellationScope.Migration;

    /// <summary>
    /// Entity type to check (when Scope = EntityType)
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Batch ID to check (when Scope = Batch)
    /// </summary>
    public string? BatchId { get; set; }

    /// <summary>
    /// Store ID to check (when Scope = Store)
    /// </summary>
    public string? StoreId { get; set; }

    /// <summary>
    /// Current deterministic cancellation state from orchestrator context
    /// </summary>
    public object? CurrentState { get; set; }

    /// <summary>
    /// Additional context for the check operation
    /// </summary>
    public Dictionary<string, object>? AdditionalContext { get; set; }
}

/// <summary>
/// Enhanced response model for external cancellation check operations with comprehensive status.
/// </summary>
public class EnhancedCheckExternalCancellationResponse
{
    /// <summary>
    /// The migration ID that was checked
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// The scope that was checked for cancellation
    /// </summary>
    public CancellationScope Scope { get; set; }

    /// <summary>
    /// Whether a cancellation was detected at the specified scope
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Comprehensive cancellation status including all active cancellations
    /// </summary>
    public CancellationStatus? CancellationStatus { get; set; }

    /// <summary>
    /// When the external check was performed
    /// </summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// Response time in milliseconds (target: &lt;1000ms)
    /// </summary>
    public int ResponseTimeMs { get; set; }

    /// <summary>
    /// Whether the external check operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the external check failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Reason for the cancellation (legacy compatibility)
    /// </summary>
    public string CancellationReason { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the cancellation was requested (legacy compatibility)
    /// </summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Whether the cancellation has been processed (legacy compatibility)
    /// </summary>
    public bool IsProcessed { get; set; }
}

/// <summary>
/// Request for hierarchical cancellation checking across all cancellation scopes.
/// Checks Migration -> EntityType -> Batch -> Store in hierarchical order.
/// </summary>
public class HierarchicalCancellationCheckRequest
{
    /// <summary>
    /// Migration ID to check for hierarchical cancellation
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Entity type to check (for EntityType, Batch, and Store scopes)
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Batch ID to check (for Batch and Store scopes)
    /// </summary>
    public string? BatchId { get; set; }

    /// <summary>
    /// Store ID to check (for Store scope)
    /// </summary>
    public string? StoreId { get; set; }

    /// <summary>
    /// Whether to stop checking at the first cancellation found (early exit)
    /// </summary>
    public bool EarlyExit { get; set; } = true;
}

/// <summary>
/// Result of a hierarchical cancellation check operation.
/// </summary>
public class HierarchicalCancellationCheckResult
{
    /// <summary>
    /// The migration ID that was checked
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Whether any cancellation was found in the hierarchy
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// The highest scope level where cancellation was detected (if any)
    /// </summary>
    public CancellationScope? HighestCancelledScope { get; set; }

    /// <summary>
    /// All scopes that were checked during the hierarchical check
    /// </summary>
    public List<CancellationScope> CheckedScopes { get; set; } = new();

    /// <summary>
    /// Detailed cancellation status for each checked scope
    /// </summary>
    public Dictionary<CancellationScope, bool> ScopeResults { get; set; } = new();

    /// <summary>
    /// When the hierarchical check was performed
    /// </summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// Total response time for all checks in milliseconds
    /// </summary>
    public int ResponseTimeMs { get; set; }

    /// <summary>
    /// Whether the hierarchical check operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the hierarchical check failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Comprehensive cancellation details (if cancellation was found)
    /// </summary>
    public CancellationStatus? CancellationDetails { get; set; }
}

/// <summary>
/// Event model for queue-based cancellation propagation between Azure Function instances.
/// Used in Layer 2 (Azure Storage Queue) of the hybrid architecture.
/// </summary>
public class CancellationPropagationEvent
{
    /// <summary>
    /// Migration identifier for the cancellation event
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Cancellation scope level (Migration, EntityType, Batch, Store)
    /// </summary>
    public CancellationScope Scope { get; set; }

    /// <summary>
    /// Event type for processing logic (e.g., "CancellationRequested")
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable reason for the cancellation
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// User or system that requested the cancellation
    /// </summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>
    /// When the cancellation event was created
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Live cancellation request DTO for Task 7.6 - Dashboard Integration
/// </summary>
public class LiveCancellationRequest
{
    /// <summary>
    /// Migration ID to cancel
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Cancellation scope (Migration, EntityType, Batch, Store)
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Reason for cancellation
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Entity type for EntityType scope cancellation
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Batch ID for Batch scope cancellation
    /// </summary>
    public string? BatchId { get; set; }

    /// <summary>
    /// Store ID for Store scope cancellation
    /// </summary>
    public string? StoreId { get; set; }
}

/// <summary>
/// Live cancellation response DTO for Task 7.6 - Dashboard Integration
/// </summary>
public class LiveCancellationResponse
{
    /// <summary>
    /// Migration ID that was cancelled
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Cancellation scope that was applied
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Status of the cancellation
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable message about the cancellation
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// When the cancellation was completed
    /// </summary>
    public string CancelledAt { get; set; } = string.Empty;

    /// <summary>
    /// Components affected by the cancellation
    /// </summary>
    public string[]? AffectedComponents { get; set; }
}