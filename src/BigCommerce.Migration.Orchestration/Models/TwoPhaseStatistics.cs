using System;
using System.Collections.Generic;

namespace BigCommerce.Migration.Orchestration.Models;

/// <summary>
/// 📊 TWO-PHASE STATISTICS TRACKING
/// 
/// Enhanced statistics tracking for entities that require two-phase processing
/// (e.g., categories with bulk creation + parent relationship fixup).
/// 
/// Provides clear visibility into:
/// - Phase 1: Entity creation success/failure
/// - Phase 2: Relationship fixup success/failure  
/// - Overall: Combined success metrics
/// </summary>
public class TwoPhaseStatistics
{
    /// <summary>
    /// Phase 1: Entity Creation Statistics
    /// </summary>
    public PhaseStatistics Phase1 { get; set; } = new();
    
    /// <summary>
    /// Phase 2: Relationship Fixup Statistics (optional)
    /// </summary>
    public PhaseStatistics? Phase2 { get; set; }
    
    /// <summary>
    /// Overall combined statistics across both phases
    /// </summary>
    public TwoPhaseOverallStatistics Overall { get; set; } = new();
    
    /// <summary>
    /// Whether this entity type uses two-phase processing
    /// </summary>
    public bool IsTwoPhase => Phase2 != null;
    
    /// <summary>
    /// Calculates overall success rate considering both phases
    /// </summary>
    public double GetOverallSuccessRate()
    {
        if (Overall.TotalEntities == 0) return 0.0;
        return (double)Overall.FullySuccessfulEntities / Overall.TotalEntities * 100.0;
    }
    
    /// <summary>
    /// Gets a human-readable summary of the two-phase results
    /// </summary>
    public string GetSummary()
    {
        if (!IsTwoPhase)
        {
            // Single phase (normal entities)
            return $"{Phase1.SuccessfulEntities}/{Phase1.TotalEntities} entities created successfully " +
                   $"({GetOverallSuccessRate():F1}% success rate)";
        }
        else
        {
            // Two phase (e.g., categories)
            var phase2Info = Phase2 != null ? 
                $", {Phase2.SuccessfulEntities}/{Phase2.TotalEntities} relationships fixed" : "";
            
            return $"{Overall.FullySuccessfulEntities}/{Overall.TotalEntities} entities fully migrated " +
                   $"({GetOverallSuccessRate():F1}% overall success). " +
                   $"Details: {Phase1.SuccessfulEntities}/{Phase1.TotalEntities} created{phase2Info}";
        }
    }
}

/// <summary>
/// Statistics for a single phase of processing
/// </summary>
public class PhaseStatistics
{
    public string PhaseName { get; set; } = string.Empty;
    public int TotalEntities { get; set; }
    public int ProcessedEntities { get; set; }
    public int SuccessfulEntities { get; set; }
    public int FailedEntities { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = new();
    
    public double GetSuccessRate()
    {
        if (TotalEntities == 0) return 0.0;
        return (double)SuccessfulEntities / TotalEntities * 100.0;
    }
}

/// <summary>
/// Overall statistics combining both phases
/// </summary>
public class TwoPhaseOverallStatistics
{
    /// <summary>
    /// Total entities discovered initially
    /// </summary>
    public int TotalEntities { get; set; }
    
    /// <summary>
    /// Entities that completed BOTH phases successfully (fully migrated)
    /// For single-phase entities, this equals Phase1.SuccessfulEntities
    /// </summary>
    public int FullySuccessfulEntities { get; set; }
    
    /// <summary>
    /// Entities that were created but have broken relationships
    /// (Phase 1 success, Phase 2 failure)
    /// </summary>
    public int PartiallySuccessfulEntities { get; set; }
    
    /// <summary>
    /// Entities that failed to be created at all
    /// (Phase 1 failure)
    /// </summary>
    public int FailedEntities { get; set; }
    
    /// <summary>
    /// Total processing time for both phases
    /// </summary>
    public TimeSpan TotalDuration { get; set; }
    
    /// <summary>
    /// All errors from both phases
    /// </summary>
    public List<string> AllErrors { get; set; } = new();
    
    /// <summary>
    /// Gets the classification of results
    /// </summary>
    public ResultClassification GetClassification()
    {
        if (FailedEntities == 0 && PartiallySuccessfulEntities == 0)
            return ResultClassification.FullSuccess;
        else if (FullySuccessfulEntities > 0 && FailedEntities < TotalEntities / 2)
            return ResultClassification.MostlySuccessful;
        else if (FullySuccessfulEntities > 0)
            return ResultClassification.PartialSuccess;
        else
            return ResultClassification.Failed;
    }
}

/// <summary>
/// Classification of overall migration results
/// </summary>
public enum ResultClassification
{
    FullSuccess,        // All entities fully migrated
    MostlySuccessful,   // >50% success, some failures/partial
    PartialSuccess,     // Some success, but significant issues
    Failed              // No successful entities
}

/// <summary>
/// Extension methods for integrating two-phase statistics with existing EntityMigrationResult
/// </summary>
public static class EntityMigrationResultExtensions
{
    /// <summary>
    /// Converts EntityMigrationResult to use two-phase statistics tracking
    /// </summary>
    public static void UpdateWithTwoPhaseStatistics(this EntityMigrationResult result, TwoPhaseStatistics stats)
    {
        // Update main statistics to reflect overall outcome
        result.TotalEntities = stats.Overall.TotalEntities;
        result.ProcessedEntities = stats.Phase1.ProcessedEntities;
        result.SuccessfulEntities = stats.Overall.FullySuccessfulEntities; // Only count fully successful
        result.FailedEntities = stats.Overall.FailedEntities + stats.Overall.PartiallySuccessfulEntities; // Count partial as failed
        result.Duration = stats.Overall.TotalDuration;
        result.Errors.AddRange(stats.Overall.AllErrors);
        
        // Add detailed breakdown to error messages for visibility
        if (stats.IsTwoPhase && stats.Overall.PartiallySuccessfulEntities > 0)
        {
            result.Errors.Add($"Note: {stats.Overall.PartiallySuccessfulEntities} entities were created but have relationship issues");
        }
    }
    
    /// <summary>
    /// Creates TwoPhaseStatistics from Phase 1 results
    /// </summary>
    public static TwoPhaseStatistics CreatePhase1Statistics(this EntityMigrationResult result)
    {
        return new TwoPhaseStatistics
        {
            Phase1 = new PhaseStatistics
            {
                PhaseName = "Entity Creation",
                TotalEntities = result.TotalEntities,
                ProcessedEntities = result.ProcessedEntities,
                SuccessfulEntities = result.SuccessfulEntities,
                FailedEntities = result.FailedEntities,
                Duration = result.Duration,
                Errors = new List<string>(result.Errors)
            },
            Overall = new TwoPhaseOverallStatistics
            {
                TotalEntities = result.TotalEntities,
                FullySuccessfulEntities = result.SuccessfulEntities, // Will be updated after Phase 2
                FailedEntities = result.FailedEntities,
                TotalDuration = result.Duration,
                AllErrors = new List<string>(result.Errors)
            }
        };
    }
}