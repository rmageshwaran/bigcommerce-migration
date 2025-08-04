using System;
using System.Text.Json.Serialization;

namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// 📊 ENHANCED TWO-PHASE PROGRESS EVENT
/// 
/// Extends the standard EntityProgressEvent to handle entities that require two-phase processing
/// (e.g., categories with bulk creation + parent relationship fixup).
/// 
/// Provides detailed progress breakdown for both phases while maintaining backward compatibility
/// with existing SignalR infrastructure and dashboard components.
/// </summary>
public class TwoPhaseEntityProgressEvent : EntityProgressEvent
{
    /// <summary>
    /// Initializes a new instance of TwoPhaseEntityProgressEvent
    /// </summary>
    [JsonConstructor]
    public TwoPhaseEntityProgressEvent()
    {
        EventType = "entity-twophase";
        HubMethod = "TwoPhaseEntityProgressUpdated";
    }

    /// <summary>
    /// Whether this entity type uses two-phase processing
    /// </summary>
    public bool IsTwoPhase { get; set; } = true;

    /// <summary>
    /// Current phase being executed (1 or 2)
    /// </summary>
    public int CurrentPhase { get; set; }

    /// <summary>
    /// Total number of phases (typically 2)
    /// </summary>
    public int TotalPhases { get; set; } = 2;

    /// <summary>
    /// Phase 1: Entity creation statistics
    /// </summary>
    public PhaseProgress Phase1 { get; set; } = new();

    /// <summary>
    /// Phase 2: Relationship fixup statistics (nullable - only populated after Phase 1)
    /// </summary>
    public PhaseProgress? Phase2 { get; set; }

    /// <summary>
    /// Overall statistics combining both phases
    /// </summary>
    public TwoPhaseOverallProgress Overall { get; set; } = new();

    /// <summary>
    /// User-friendly status message for the current phase
    /// </summary>
    public string PhaseDescription { get; set; } = string.Empty;

    /// <summary>
    /// Detailed breakdown message for UI display
    /// </summary>
    public string DetailedStatus { get; set; } = string.Empty;

    /// <summary>
    /// Override standard Progress to show overall progress across both phases
    /// </summary>
    [JsonIgnore]
    public new double Progress => Overall.OverallProgress;
}

/// <summary>
/// Progress statistics for a single phase
/// </summary>
public class PhaseProgress
{
    /// <summary>
    /// Name of the phase (e.g., "Entity Creation", "Parent Relationship Fixup")
    /// </summary>
    public string PhaseName { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of entities to process in this phase
    /// </summary>
    public int TotalEntities { get; set; }
    
    /// <summary>
    /// Number of entities processed so far in this phase
    /// </summary>
    public int ProcessedEntities { get; set; }
    
    /// <summary>
    /// Number of entities successfully processed in this phase
    /// </summary>
    public int SuccessfulEntities { get; set; }
    
    /// <summary>
    /// Number of entities that failed processing in this phase
    /// </summary>
    public int FailedEntities { get; set; }
    
    /// <summary>
    /// Current status of this phase (pending, processing, completed, failed)
    /// </summary>
    public string Status { get; set; } = "pending";
    
    /// <summary>
    /// Progress percentage for this phase (0-100)
    /// </summary>
    public double Progress => TotalEntities > 0 ? (double)ProcessedEntities / TotalEntities * 100 : 0;
    
    /// <summary>
    /// Time taken to process this phase
    /// </summary>
    public TimeSpan? Duration { get; set; }
}

/// <summary>
/// Overall progress combining both phases
/// </summary>
public class TwoPhaseOverallProgress
{
    /// <summary>
    /// Total entities discovered initially
    /// </summary>
    public int TotalEntities { get; set; }

    /// <summary>
    /// Entities that completed BOTH phases successfully (fully migrated)
    /// </summary>
    public int FullySuccessfulEntities { get; set; }

    /// <summary>
    /// Entities that were created but have broken relationships
    /// (Phase 1 success, Phase 2 failure)
    /// </summary>
    public int PartiallySuccessfulEntities { get; set; }

    /// <summary>
    /// Entities that failed to be created at all (Phase 1 failure)
    /// </summary>
    public int FailedEntities { get; set; }

    /// <summary>
    /// Overall progress percentage considering both phases (0-100)
    /// For two-phase: (FullySuccessful / Total) * 100
    /// </summary>
    public double OverallProgress => TotalEntities > 0 ? (double)FullySuccessfulEntities / TotalEntities * 100 : 0;

    /// <summary>
    /// Overall success classification
    /// </summary>
    public string Classification { get; set; } = "Unknown";

    /// <summary>
    /// Total processing time for both phases
    /// </summary>
    public TimeSpan? TotalDuration { get; set; }
}

/// <summary>
/// Factory options for creating TwoPhaseEntityProgressEvent
/// </summary>
public class TwoPhaseEntityProgressOptions
{
    /// <summary>
    /// Type of entity being processed (e.g., "categories")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Current phase being executed (1 or 2)
    /// </summary>
    public int CurrentPhase { get; set; }
    
    /// <summary>
    /// User-friendly description of the current phase
    /// </summary>
    public string PhaseDescription { get; set; } = string.Empty;
    
    /// <summary>
    /// Phase 1 statistics (entity creation)
    /// </summary>
    public PhaseProgress Phase1 { get; set; } = new();
    
    /// <summary>
    /// Phase 2 statistics (relationship fixup, nullable)
    /// </summary>
    public PhaseProgress? Phase2 { get; set; }
    
    /// <summary>
    /// Overall combined statistics across both phases
    /// </summary>
    public TwoPhaseOverallProgress Overall { get; set; } = new();
    
    /// <summary>
    /// Whether the migration has been cancelled
    /// </summary>
    public bool IsCancelled { get; set; }
    
    /// <summary>
    /// Reason for cancellation (if cancelled)
    /// </summary>
    public string? CancellationReason { get; set; }
    
    /// <summary>
    /// When the cancellation occurred (if cancelled)
    /// </summary>
    public DateTime? CancelledAt { get; set; }
}

/// <summary>
/// Extension methods for backward compatibility with existing EntityProgressEvent
/// </summary>
public static class TwoPhaseProgressExtensions
{
    /// <summary>
    /// Converts TwoPhaseEntityProgressEvent to standard EntityProgressEvent for backward compatibility
    /// </summary>
    public static EntityProgressEvent ToStandardEntityProgress(this TwoPhaseEntityProgressEvent twoPhaseEvent)
    {
        return new EntityProgressEvent
        {
            MigrationId = twoPhaseEvent.MigrationId,
            EntityType = twoPhaseEvent.EntityType,
            TotalCount = twoPhaseEvent.Overall.TotalEntities,
            ProcessedCount = twoPhaseEvent.Overall.FullySuccessfulEntities + twoPhaseEvent.Overall.PartiallySuccessfulEntities + twoPhaseEvent.Overall.FailedEntities,
            SuccessCount = twoPhaseEvent.Overall.FullySuccessfulEntities, // Only count fully successful
            FailureCount = twoPhaseEvent.Overall.PartiallySuccessfulEntities + twoPhaseEvent.Overall.FailedEntities, // Count partial as failed for simplicity
            Status = twoPhaseEvent.Status,
            ProcessingTime = twoPhaseEvent.Overall.TotalDuration,
            Timestamp = twoPhaseEvent.Timestamp,
            IsCancelled = twoPhaseEvent.IsCancelled,
            CancellationReason = twoPhaseEvent.CancellationReason,
            CancelledAt = twoPhaseEvent.CancelledAt
        };
    }

    /// <summary>
    /// Creates a user-friendly status message for two-phase progress
    /// </summary>
    public static string GetUserFriendlyStatus(this TwoPhaseEntityProgressEvent twoPhaseEvent)
    {
        if (twoPhaseEvent.IsCancelled)
            return $"Cancelled: {twoPhaseEvent.CancellationReason}";

        return twoPhaseEvent.CurrentPhase switch
        {
            1 => $"Phase 1: {twoPhaseEvent.PhaseDescription} - {twoPhaseEvent.Phase1.Progress:F1}% complete",
            2 => $"Phase 2: {twoPhaseEvent.PhaseDescription} - {twoPhaseEvent.Phase2?.Progress ?? 0:F1}% complete",
            _ => $"Overall: {twoPhaseEvent.Overall.OverallProgress:F1}% complete"
        };
    }
}