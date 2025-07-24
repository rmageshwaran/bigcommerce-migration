using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces
{
    /// <summary>
    /// Phase 4.3: Service for validating progress state consistency with cancellation state
    /// Ensures that progress updates honor cancellation state across all components
    /// </summary>
    public interface IProgressStateValidator
    {
        /// <summary>
        /// Validates that a progress update is consistent with the current cancellation state
        /// </summary>
        /// <param name="progressUpdate">The progress update to validate</param>
        /// <returns>Validation result indicating consistency and any issues found</returns>
        Task<ProgressValidationResult> ValidateProgressUpdateAsync(ProgressUpdate progressUpdate);

        /// <summary>
        /// Validates that a progress event is consistent with cancellation state
        /// </summary>
        /// <param name="progressEvent">The progress event to validate</param>
        /// <returns>Validation result indicating consistency and any issues found</returns>
        Task<ProgressValidationResult> ValidateProgressEventAsync(ProgressEvent progressEvent);

        /// <summary>
        /// Validates the overall consistency of progress state for a migration
        /// </summary>
        /// <param name="migrationId">The migration to validate</param>
        /// <returns>Comprehensive validation result for the migration</returns>
        Task<MigrationValidationResult> ValidateMigrationProgressConsistencyAsync(string migrationId);

        /// <summary>
        /// Validates that cancellation state propagation is working correctly
        /// </summary>
        /// <param name="migrationId">The migration to check</param>
        /// <returns>Validation result for cancellation propagation</returns>
        Task<CancellationPropagationValidationResult> ValidateCancellationPropagationAsync(string migrationId);
    }

    /// <summary>
    /// Phase 4.3: Result of progress update validation
    /// </summary>
    public class ProgressValidationResult
    {
        /// <summary>
        /// Indicates whether the progress update is valid and consistent
        /// </summary>
        public bool IsValid { get; set; }
        
        /// <summary>
        /// Optional validation message providing context about the validation result
        /// </summary>
        public string? ValidationMessage { get; set; }
        
        /// <summary>
        /// List of validation warnings that don't invalidate the result but indicate potential issues
        /// </summary>
        public List<string> Warnings { get; set; } = new();
        
        /// <summary>
        /// List of validation errors that invalidate the result and require attention
        /// </summary>
        public List<string> Errors { get; set; } = new();
        
        /// <summary>
        /// Recommended action to resolve validation issues, if any
        /// </summary>
        public string? RecommendedAction { get; set; }
    }

    /// <summary>
    /// Phase 4.3: Result of migration-wide validation
    /// </summary>
    public class MigrationValidationResult
    {
        /// <summary>
        /// The migration ID that was validated
        /// </summary>
        public string MigrationId { get; set; } = string.Empty;
        
        /// <summary>
        /// Indicates whether the overall migration validation passed
        /// </summary>
        public bool IsOverallValid { get; set; }
        
        /// <summary>
        /// Indicates whether the cancellation state is consistent across components
        /// </summary>
        public bool IsCancellationStateConsistent { get; set; }
        
        /// <summary>
        /// Indicates whether the progress state is consistent with cancellation state
        /// </summary>
        public bool IsProgressStateConsistent { get; set; }
        
        /// <summary>
        /// Timestamp when the validation was performed
        /// </summary>
        public DateTime ValidationTimestamp { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Summary of validation results for quick overview
        /// </summary>
        public List<string> ValidationSummary { get; set; } = new();
        
        /// <summary>
        /// Detailed validation results for individual components
        /// </summary>
        public List<ProgressValidationResult> ComponentValidations { get; set; } = new();
    }

    /// <summary>
    /// Phase 4.3: Result of cancellation propagation validation
    /// </summary>
    public class CancellationPropagationValidationResult
    {
        /// <summary>
        /// The migration ID that was validated for cancellation propagation
        /// </summary>
        public string MigrationId { get; set; } = string.Empty;
        
        /// <summary>
        /// Indicates whether cancellation propagation is working correctly across all components
        /// </summary>
        public bool IsPropagationWorking { get; set; }
        
        /// <summary>
        /// Indicates whether the orchestrator is aware of cancellation state
        /// </summary>
        public bool IsOrchestratorAware { get; set; }
        
        /// <summary>
        /// Indicates whether activities are receiving cancellation state via soft tokens
        /// </summary>
        public bool AreActivitiesAware { get; set; }
        
        /// <summary>
        /// Indicates whether the progress tracker is handling cancellation state correctly
        /// </summary>
        public bool IsProgressTrackerAware { get; set; }
        
        /// <summary>
        /// Indicates whether SignalR functions are filtering cancelled progress events correctly
        /// </summary>
        public bool IsSignalRFilteringWorking { get; set; }
        
        /// <summary>
        /// Timestamp when the propagation check was performed
        /// </summary>
        public DateTime CheckTimestamp { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Step-by-step trace of the cancellation propagation path through the system
        /// </summary>
        public List<string> PropagationPath { get; set; } = new();
        
        /// <summary>
        /// List of issues found in the cancellation propagation flow
        /// </summary>
        public List<string> Issues { get; set; } = new();
    }
} 