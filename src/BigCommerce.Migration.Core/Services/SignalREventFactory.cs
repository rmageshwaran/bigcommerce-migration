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
                CurrentEntityType = options.CurrentEntityType,
                EstimatedTimeRemaining = options.EstimatedTimeRemaining
            };

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

        #endregion
    }
} 