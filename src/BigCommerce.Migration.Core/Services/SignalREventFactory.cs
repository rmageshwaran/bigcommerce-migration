using System;
using System.Collections.Generic;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Services
{
    /// <summary>
    /// 🎯 CENTRALIZED SIGNALR EVENT FACTORY
    /// Single source of truth for all SignalR event creation across the entire system.
    /// 
    /// ✅ SOLVES:
    /// - 21+ scattered event creation points
    /// - Inconsistent property naming (PascalCase vs camelCase)
    /// - Missing base properties (Timestamp, IsCancelled, etc.)
    /// - Inconsistent HubMethod naming
    /// - No validation of required properties
    /// </summary>
    public interface ISignalREventFactory
    {
        /// <summary>
        /// Creates a MigrationProgressEvent with consistent properties and validation
        /// </summary>
        MigrationProgressEvent CreateMigrationProgress(string migrationId, MigrationProgressOptions options);
        
        /// <summary>
        /// Creates a BatchProgressEvent with consistent properties and validation
        /// </summary>
        BatchProgressEvent CreateBatchProgress(string migrationId, BatchProgressOptions options);
        
        /// <summary>
        /// Creates an EntityProgressEvent with consistent properties and validation
        /// </summary>
        EntityProgressEvent CreateEntityProgress(string migrationId, EntityProgressOptions options);
        
        /// <summary>
        /// Creates an ErrorProgressEvent with consistent properties and validation
        /// </summary>
        ErrorProgressEvent CreateErrorProgress(string migrationId, ErrorProgressOptions options);
        
        /// <summary>
        /// Creates a StatusProgressEvent with consistent properties and validation
        /// </summary>
        StatusProgressEvent CreateStatusProgress(string migrationId, StatusProgressOptions options);
        
        /// <summary>
        /// Creates a SubBatchStartedEvent with consistent properties and validation
        /// </summary>
        SubBatchStartedEvent CreateSubBatchStarted(string migrationId, SubBatchStartedOptions options);
        
        /// <summary>
        /// Creates a SubBatchCompletedEvent with consistent properties and validation
        /// </summary>
        SubBatchCompletedEvent CreateSubBatchCompleted(string migrationId, SubBatchCompletedOptions options);
        
        /// <summary>
        /// Creates a SubBatchMigrationProgressEvent with consistent properties and validation
        /// </summary>
        SubBatchMigrationProgressEvent CreateSubBatchProgress(string migrationId, SubBatchProgressOptions options);

        /// <summary>
        /// Creates a CancellationProgressEvent with consistent properties and validation
        /// Task 7.1 Live Cancellation Integration
        /// </summary>
        CancellationProgressEvent CreateCancellationProgress(string migrationId, CancellationProgressOptions options);
    }

    /// <summary>
    /// Centralized SignalR Event Factory implementation
    /// </summary>
    public class SignalREventFactory : ISignalREventFactory
    {
        private readonly IDateTimeProvider _dateTimeProvider;

        /// <summary>
    /// Initializes a new instance of the SignalREventFactory
    /// </summary>
    /// <param name="dateTimeProvider">Date time provider for timestamp generation</param>
    public SignalREventFactory(IDateTimeProvider dateTimeProvider)
        {
            _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        }

        /// <summary>
        /// Creates a MigrationProgressEvent with auto-populated base properties
        /// </summary>
        public MigrationProgressEvent CreateMigrationProgress(string migrationId, MigrationProgressOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var progressEvent = new MigrationProgressEvent
            {
                // Base properties (auto-populated)
                MigrationId = migrationId,
                Timestamp = _dateTimeProvider.UtcNow,
                IsCancelled = options.IsCancelled ?? false,
                CancellationReason = options.CancellationReason,
                CancelledAt = options.CancelledAt,
                ConnectionId = options.ConnectionId,
                GroupName = options.GroupName,
                
                // Specific properties
                OverallProgress = options.OverallProgress,
                Status = options.Status ?? "running",
                TotalEntities = options.TotalEntities,
                ProcessedEntities = options.ProcessedEntities,
                FailedEntities = options.FailedEntities,
                SuccessfulEntities = options.SuccessfulEntities ?? Math.Max(0, options.ProcessedEntities - options.FailedEntities), // 🎯 AUTO-CALCULATE: Backend computes SuccessfulEntities to avoid frontend calculation
                CurrentEntityType = options.CurrentEntityType,
                
                // 🎯 TIME TRACKING FIX: Auto-calculate time properties to fix "0m 0s" displays
                StartTime = options.StartTime,
                ElapsedTime = options.ElapsedTime ?? (options.StartTime.HasValue ? _dateTimeProvider.UtcNow - options.StartTime.Value : null),
                
                // 🎯 BATCH DETAILS FIX: Add real batch tracking for "Current Processing Status" section
                CurrentBatchNumber = options.CurrentBatchNumber,
                CurrentActivity = options.CurrentActivity,
                CurrentBatch = options.CurrentBatch
            };
            
            // Calculate EntitiesPerSecond first so it's available for EstimatedTimeRemaining calculation
            progressEvent.EntitiesPerSecond = options.EntitiesPerSecond ?? CalculateEntitiesPerSecond(options.ProcessedEntities, options.ElapsedTime, options.StartTime);
            
            // Calculate EstimatedTimeRemaining using the calculated EntitiesPerSecond
            progressEvent.EstimatedTimeRemaining = options.EstimatedTimeRemaining ?? CalculateEstimatedTimeRemaining(options.TotalEntities, options.ProcessedEntities, progressEvent.EntitiesPerSecond);

            return progressEvent;
        }

        /// <summary>
        /// Creates a BatchProgressEvent with auto-populated base properties
        /// </summary>
        public BatchProgressEvent CreateBatchProgress(string migrationId, BatchProgressOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var batchEvent = new BatchProgressEvent
            {
                // Base properties (auto-populated)
                MigrationId = migrationId,
                Timestamp = _dateTimeProvider.UtcNow,
                IsCancelled = options.IsCancelled ?? false,
                CancellationReason = options.CancellationReason,
                CancelledAt = options.CancelledAt,
                ConnectionId = options.ConnectionId,
                GroupName = options.GroupName,
                
                // Specific properties
                EntityType = options.EntityType ?? throw new ArgumentException("EntityType is required", nameof(options)),
                BatchNumber = options.BatchNumber,
                TotalBatches = options.TotalBatches,
                BatchSize = options.BatchSize,
                ProcessedCount = options.ProcessedCount,
                FailedCount = options.FailedCount,
                Status = options.Status ?? "processing",
                ProcessingTime = options.ProcessingTime
            };

            return batchEvent;
        }

        /// <summary>
        /// Creates an EntityProgressEvent with auto-populated base properties
        /// </summary>
        public EntityProgressEvent CreateEntityProgress(string migrationId, EntityProgressOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var entityEvent = new EntityProgressEvent
            {
                // Base properties (auto-populated)
                MigrationId = migrationId,
                Timestamp = _dateTimeProvider.UtcNow,
                IsCancelled = options.IsCancelled ?? false,
                CancellationReason = options.CancellationReason,
                CancelledAt = options.CancelledAt,
                ConnectionId = options.ConnectionId,
                GroupName = options.GroupName,
                
                // Specific properties (match actual EntityProgressEvent properties)
                EntityType = options.EntityType ?? throw new ArgumentException("EntityType is required", nameof(options)),
                Status = options.Status ?? "processing",
                ProcessedCount = options.ProcessedCount,
                TotalCount = options.TotalCount,
                SuccessCount = options.ProcessedCount, // Map ProcessedCount to SuccessCount
                FailureCount = options.TotalCount - options.ProcessedCount, // Calculate failures
                ProcessingTime = options.ProcessingTime
            };

            return entityEvent;
        }

        /// <summary>
        /// Creates an ErrorProgressEvent with auto-populated base properties
        /// </summary>
        public ErrorProgressEvent CreateErrorProgress(string migrationId, ErrorProgressOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var errorEvent = new ErrorProgressEvent
            {
                // Base properties (auto-populated)
                MigrationId = migrationId,
                Timestamp = _dateTimeProvider.UtcNow,
                IsCancelled = options.IsCancelled ?? false,
                CancellationReason = options.CancellationReason,
                CancelledAt = options.CancelledAt,
                ConnectionId = options.ConnectionId,
                GroupName = options.GroupName,
                
                // Specific properties (match actual ErrorProgressEvent properties)
                Message = options.ErrorMessage ?? throw new ArgumentException("ErrorMessage is required", nameof(options)),
                EntityType = options.EntityType,
                EntityId = options.EntityId,
                BatchNumber = options.BatchNumber,
                Severity = options.Severity ?? "error",
                Details = options.Exception // Map Exception to Details
            };

            return errorEvent;
        }

        /// <summary>
        /// Creates a StatusProgressEvent with auto-populated base properties
        /// </summary>
        public StatusProgressEvent CreateStatusProgress(string migrationId, StatusProgressOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var statusEvent = new StatusProgressEvent
            {
                // Base properties (auto-populated)
                MigrationId = migrationId,
                Timestamp = _dateTimeProvider.UtcNow,
                IsCancelled = options.IsCancelled ?? false,
                CancellationReason = options.CancellationReason,
                CancelledAt = options.CancelledAt,
                ConnectionId = options.ConnectionId,
                GroupName = options.GroupName,
                
                // Specific properties (match actual StatusProgressEvent properties)
                Status = options.Status ?? throw new ArgumentException("Status is required", nameof(options)),
                Message = options.Message ?? string.Empty,
                Metadata = options.Data // Map Data to Metadata
            };

            return statusEvent;
        }

        /// <summary>
        /// Creates a SubBatchStartedEvent with auto-populated base properties
        /// </summary>
        public SubBatchStartedEvent CreateSubBatchStarted(string migrationId, SubBatchStartedOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var subBatchEvent = new SubBatchStartedEvent
            {
                // Base properties (auto-populated)
                MigrationId = migrationId,
                Timestamp = _dateTimeProvider.UtcNow,
                IsCancelled = options.IsCancelled ?? false,
                CancellationReason = options.CancellationReason,
                CancelledAt = options.CancelledAt,
                ConnectionId = options.ConnectionId,
                GroupName = options.GroupName,
                
                // Specific properties (match actual SubBatchStartedEvent properties)
                ParentBatchNumber = options.ParentBatchNumber,
                SubBatchNumber = options.SubBatchNumber,
                TotalSubBatches = options.TotalSubBatches,
                EntityType = options.EntityType ?? throw new ArgumentException("EntityType is required", nameof(options)),
                EntitiesInSubBatch = options.EntitiesInBatch,
                MaxConcurrency = 5, // Default concurrency
                StartedAt = _dateTimeProvider.UtcNow
            };

            return subBatchEvent;
        }

        /// <summary>
        /// Creates a SubBatchCompletedEvent with auto-populated base properties
        /// </summary>
        public SubBatchCompletedEvent CreateSubBatchCompleted(string migrationId, SubBatchCompletedOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var subBatchEvent = new SubBatchCompletedEvent
            {
                // Base properties (auto-populated)
                MigrationId = migrationId,
                Timestamp = _dateTimeProvider.UtcNow,
                IsCancelled = options.IsCancelled ?? false,
                CancellationReason = options.CancellationReason,
                CancelledAt = options.CancelledAt,
                ConnectionId = options.ConnectionId,
                GroupName = options.GroupName,
                
                // Specific properties
                ParentBatchNumber = options.ParentBatchNumber,
                SubBatchNumber = options.SubBatchNumber,
                TotalSubBatches = options.TotalSubBatches,
                SuccessfulEntities = options.SuccessfulEntities,
                FailedEntities = options.FailedEntities,
                TotalEntities = options.TotalEntities,
                EntityType = options.EntityType ?? throw new ArgumentException("EntityType is required", nameof(options)),
                ProcessingTime = options.ProcessingTime ?? TimeSpan.Zero,
                CompletedAt = options.CompletedAt ?? _dateTimeProvider.UtcNow,
                Errors = options.Errors ?? new List<string>(),
                CumulativeSuccessfulEntities = options.CumulativeSuccessfulEntities,
                CumulativeFailedEntities = options.CumulativeFailedEntities,
                TotalMigrationEntities = options.TotalMigrationEntities,
                ProgressPercentage = options.ProgressPercentage,
                EstimatedTimeRemaining = options.EstimatedTimeRemaining
            };

            return subBatchEvent;
        }

        /// <summary>
        /// Creates a SubBatchMigrationProgressEvent with auto-populated base properties
        /// </summary>
        public SubBatchMigrationProgressEvent CreateSubBatchProgress(string migrationId, SubBatchProgressOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var subBatchProgressEvent = new SubBatchMigrationProgressEvent
            {
                // Base properties (auto-populated)
                MigrationId = migrationId,
                Timestamp = _dateTimeProvider.UtcNow,
                IsCancelled = options.IsCancelled ?? false,
                CancellationReason = options.CancellationReason,
                CancelledAt = options.CancelledAt,
                ConnectionId = options.ConnectionId,
                GroupName = options.GroupName,
                
                // Specific properties
                TotalPages = options.TotalPages,
                CompletedPages = options.CompletedPages,
                TotalSubBatches = options.TotalSubBatches,
                CompletedSubBatches = options.CompletedSubBatches,
                TotalSuccessfulEntities = options.TotalSuccessfulEntities,
                TotalFailedEntities = options.TotalFailedEntities,
                TotalExpectedEntities = options.TotalExpectedEntities,
                ProcessingRate = options.ProcessingRate,
                OverallProgressPercentage = options.OverallProgressPercentage,
                EstimatedTimeRemaining = options.EstimatedTimeRemaining ?? TimeSpan.Zero,
                UpdatedAt = options.UpdatedAt ?? _dateTimeProvider.UtcNow,
                ElapsedTime = options.ElapsedTime ?? TimeSpan.Zero,
                RecentErrors = options.RecentErrors ?? new List<string>(),
                PerformanceMetrics = options.PerformanceMetrics ?? new Dictionary<string, object>()
            };

            return subBatchProgressEvent;
        }

        #region Private Helper Methods

        private static void ValidateMigrationId(string migrationId)
        {
            if (string.IsNullOrWhiteSpace(migrationId))
                throw new ArgumentException("MigrationId cannot be null or empty", nameof(migrationId));
        }

        private static void ValidateRequired<T>(T options, string paramName) where T : class
        {
            if (options == null)
                throw new ArgumentNullException(paramName);
        }

        /// <summary>
        /// Calculates entities per second processing speed
        /// Returns null if insufficient data for calculation
        /// </summary>
        private double? CalculateEntitiesPerSecond(int processedEntities, TimeSpan? elapsedTime, DateTime? startTime)
        {
            TimeSpan? actualElapsedTime = elapsedTime;
            
            // If no elapsed time provided but we have start time, calculate it
            if (!actualElapsedTime.HasValue && startTime.HasValue)
            {
                actualElapsedTime = _dateTimeProvider.UtcNow - startTime.Value;
            }
            
            // Need valid elapsed time and some processed entities
            if (!actualElapsedTime.HasValue || actualElapsedTime.Value.TotalSeconds <= 0 || processedEntities <= 0)
            {
                return null;
            }
            
            return processedEntities / actualElapsedTime.Value.TotalSeconds;
        }

        /// <summary>
        /// Calculates estimated time remaining for migration completion
        /// Returns null if insufficient data for calculation
        /// </summary>
        private TimeSpan? CalculateEstimatedTimeRemaining(int totalEntities, int processedEntities, double? entitiesPerSecond)
        {
            // Need valid processing speed to calculate time estimate
            if (!entitiesPerSecond.HasValue || entitiesPerSecond.Value <= 0)
            {
                return null;
            }
            
            var remainingEntities = totalEntities - processedEntities;
            
            // If no entities remain, time remaining is zero
            if (remainingEntities <= 0)
            {
                return TimeSpan.Zero;
            }
            
            // Calculate time remaining: remaining entities / entities per second
            var estimatedSecondsRemaining = remainingEntities / entitiesPerSecond.Value;
            return TimeSpan.FromSeconds(estimatedSecondsRemaining);
        }

        #endregion

        /// <summary>
        /// Creates a CancellationProgressEvent with auto-populated base properties
        /// Task 7.1 Live Cancellation Integration
        /// </summary>
        public CancellationProgressEvent CreateCancellationProgress(string migrationId, CancellationProgressOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var cancellationEvent = new CancellationProgressEvent
            {
                // Base properties (auto-populated) - DO NOT set base class cancellation properties to avoid conflicts
                MigrationId = migrationId,
                Timestamp = _dateTimeProvider.UtcNow,
                ConnectionId = options.ConnectionId,
                GroupName = options.GroupName,
                
                // Specific cancellation properties (CancellationProgressEvent has its own cancellation handling)
                Scope = options.Scope,
                Status = options.Status ?? "requested",
                Reason = options.Reason ?? throw new ArgumentException("Reason is required for cancellation events", nameof(options)),
                EntityType = options.EntityType,
                BatchId = options.BatchId,
                StoreId = options.StoreId,
                EstimatedTimeToComplete = options.EstimatedTimeToComplete,
                PropagatedAt = options.PropagatedAt,
                RequestedBy = options.RequestedBy,
                TotalInstances = options.TotalInstances,
                AcknowledgedInstances = options.AcknowledgedInstances,
                AdditionalContext = options.AdditionalContext ?? new Dictionary<string, object>()
            };

            return cancellationEvent;
        }
    }
} 