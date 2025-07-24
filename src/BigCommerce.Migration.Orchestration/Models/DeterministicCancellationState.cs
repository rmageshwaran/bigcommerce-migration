using System;

namespace BigCommerce.Migration.Orchestration.Models;

/// <summary>
/// Deterministic cancellation state that maintains consistency across orchestrator replays.
/// This class ensures that cancellation checks don't violate Durable Functions determinism requirements
/// by storing cancellation state within the orchestrator context rather than checking external sources repeatedly.
/// </summary>
public class DeterministicCancellationState
{
    /// <summary>
    /// Indicates whether the migration has been cancelled
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// UTC timestamp when the cancellation was detected
    /// </summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Reason for the cancellation (e.g., "User requested cancellation", "System timeout")
    /// </summary>
    public string CancellationReason { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the external cancellation state has been checked during this orchestrator execution.
    /// This ensures we only check the external cancellation token once per orchestrator run to maintain determinism.
    /// </summary>
    public bool StateChecked { get; set; }

    /// <summary>
    /// Migration ID this cancellation state belongs to
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Version of this cancellation state for tracking changes
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Creates a new deterministic cancellation state for the specified migration
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <returns>New cancellation state</returns>
    public static DeterministicCancellationState Create(string migrationId)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

        return new DeterministicCancellationState
        {
            MigrationId = migrationId,
            IsCancelled = false,
            StateChecked = false,
            Version = 1
        };
    }

    /// <summary>
    /// Marks the migration as cancelled with the specified reason
    /// </summary>
    /// <param name="reason">Cancellation reason</param>
    /// <param name="cancelledAt">Cancellation timestamp (uses UtcNow if not specified)</param>
    public void MarkAsCancelled(string reason, DateTime? cancelledAt = null)
    {
        IsCancelled = true;
        CancellationReason = reason ?? "Unknown reason";
        CancelledAt = cancelledAt ?? DateTime.UtcNow;
        Version++;
    }

    /// <summary>
    /// Marks the external cancellation state as checked
    /// </summary>
    public void MarkStateAsChecked()
    {
        StateChecked = true;
        Version++;
    }

    /// <summary>
    /// Validates that the cancellation state is in a consistent state
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValid()
    {
        // Basic validation rules
        if (string.IsNullOrWhiteSpace(MigrationId))
            return false;

        if (IsCancelled && string.IsNullOrWhiteSpace(CancellationReason))
            return false;

        if (IsCancelled && !CancelledAt.HasValue)
            return false;

        return true;
    }

    /// <summary>
    /// Creates a deep copy of this cancellation state
    /// </summary>
    /// <returns>Deep copy of the cancellation state</returns>
    public DeterministicCancellationState Clone()
    {
        return new DeterministicCancellationState
        {
            MigrationId = MigrationId,
            IsCancelled = IsCancelled,
            CancelledAt = CancelledAt,
            CancellationReason = CancellationReason,
            StateChecked = StateChecked,
            Version = Version
        };
    }

    /// <summary>
    /// Returns a string representation of the cancellation state for debugging
    /// </summary>
    /// <returns>String representation</returns>
    public override string ToString()
    {
        return $"DeterministicCancellationState(MigrationId={MigrationId}, IsCancelled={IsCancelled}, " +
               $"StateChecked={StateChecked}, CancelledAt={CancelledAt}, Reason='{CancellationReason}', Version={Version})";
    }
}

/// <summary>
/// Request model for checking external cancellation state
/// </summary>
public class CheckExternalCancellationRequest
{
    /// <summary>
    /// Migration ID to check cancellation for
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Current cancellation state (for context)
    /// </summary>
    public DeterministicCancellationState? CurrentState { get; set; }

    /// <summary>
    /// Validates the request
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(MigrationId);
    }
}

/// <summary>
/// Response model for external cancellation check
/// </summary>
public class CheckExternalCancellationResponse
{
    /// <summary>
    /// Whether the migration is cancelled according to external state
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Cancellation reason if cancelled
    /// </summary>
    public string CancellationReason { get; set; } = string.Empty;

    /// <summary>
    /// When the cancellation was requested
    /// </summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Whether the external cancellation token is processed (to prevent restarts)
    /// </summary>
    public bool IsProcessed { get; set; }
} 