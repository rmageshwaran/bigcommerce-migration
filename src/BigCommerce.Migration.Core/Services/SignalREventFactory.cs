using System;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Services
{
    /// <summary>
    /// Simplified SignalR Event Factory - Single source of truth for all SignalR events
    /// 
    /// ✅ SOLVES:
    /// - Event spam: 20+ events/second → 2-3 events per migration
    /// - Inconsistent properties and validation
    /// - Multiple scattered event creation points
    /// - Complex multi-tier aggregation
    /// 
    /// 🎯 SIMPLIFIED APPROACH:
    /// - Only 4 event types (vs 11 previously)
    /// - Chunk-level progress only (no sub-batches)
    /// - Clean lifecycle: Start → ChunkProgress → Complete/Error
    /// </summary>
    public interface ISignalREventFactory
    {
        /// <summary>
        /// Creates a MigrationStartedEvent when a migration begins
        /// </summary>
        MigrationStartedEvent CreateMigrationStarted(string migrationId, MigrationStartedOptions options);
        
        /// <summary>
        /// Creates an EntityStartedEvent when an entity type starts processing (after discovery)
        /// </summary>
        EntityStartedEvent CreateEntityStarted(string migrationId, EntityStartedOptions options);

        /// <summary>
        /// Creates an EntityChunkProgressEvent when a chunk completes processing
        /// This is the main progress event fired throughout the migration
        /// </summary>
        EntityChunkProgressEvent CreateEntityChunkProgress(string migrationId, EntityChunkProgressOptions options);
        
        /// <summary>
        /// Creates an EntityCompletedEvent when an entity phase finishes processing
        /// </summary>
        EntityCompletedEvent CreateEntityCompleted(string migrationId, EntityCompletedOptions options);
        
        /// <summary>
        /// Creates a MigrationCompletedEvent when a migration finishes
        /// </summary>
        MigrationCompletedEvent CreateMigrationCompleted(string migrationId, MigrationCompletedOptions options);
        
        /// <summary>
        /// Creates an ErrorProgressEvent when an error occurs
        /// </summary>
        ErrorProgressEvent CreateErrorProgress(string migrationId, ErrorProgressOptions options);
    }

    /// <summary>
    /// Simplified SignalR Event Factory implementation
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
        /// Creates a MigrationStartedEvent with auto-populated base properties
        /// </summary>
        public MigrationStartedEvent CreateMigrationStarted(string migrationId, MigrationStartedOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var migrationEvent = new MigrationStartedEvent
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
                SourceStore = options.SourceStore,
                DestinationStore = options.DestinationStore,
                StartDateTime = options.StartDateTime ?? _dateTimeProvider.UtcNow,
                EstimatedEndTime = options.EstimatedEndTime,
                Entities = options.Entities ?? new()
            };

            return migrationEvent;
        }

        /// <summary>
        /// Creates an EntityStartedEvent with auto-populated base properties
        /// </summary>
        public EntityStartedEvent CreateEntityStarted(string migrationId, EntityStartedOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            var entityEvent = new EntityStartedEvent
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
                EntityType = options.EntityType,
                TotalCount = options.TotalCount,
                EstimatedDurationMs = options.EstimatedDurationMs,
                Message = options.Message,
                StartDateTime = (options.StartDateTime ?? _dateTimeProvider.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture)
            };

            return entityEvent;
        }

        /// <summary>
        /// Creates an EntityChunkProgressEvent with auto-populated base properties
        /// </summary>
        public EntityChunkProgressEvent CreateEntityChunkProgress(string migrationId, EntityChunkProgressOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            if (string.IsNullOrWhiteSpace(options.EntityType))
                throw new ArgumentException("EntityType is required", nameof(options));

            var chunkEvent = new EntityChunkProgressEvent
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
                EntityType = options.EntityType,
                ChunkNumber = options.ChunkNumber,
                TotalChunks = options.TotalChunks,
                TotalProcessed = options.TotalProcessed,
                TotalSuccess = options.TotalSuccess,
                TotalFailed = options.TotalFailed,
                TotalSkipped = options.TotalSkipped,
                TotalCancelled = options.TotalCancelled,
                TotalEntitiesForType = options.TotalEntitiesForType ?? 0,
                ProgressPercentage = options.ProgressPercentage ?? CalculateProgressPercentage(options.ChunkNumber, options.TotalChunks),
                Status = options.Status ?? "completed",
                ShowTotalCount = options.ShowTotalCount ?? true, // 🎯 UI FLAG: Include display flag in SignalR event
                ProcessingTime = options.ProcessingTime ?? TimeSpan.Zero,
                EntitiesPerSecond = options.EntitiesPerSecond,
                EstimatedTimeRemaining = options.EstimatedTimeRemaining
            };

            return chunkEvent;
        }

        /// <summary>
        /// Creates an EntityCompletedEvent with auto-populated base properties
        /// </summary>
        public EntityCompletedEvent CreateEntityCompleted(string migrationId, EntityCompletedOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            if (string.IsNullOrWhiteSpace(options.EntityType))
                throw new ArgumentException("EntityType is required", nameof(options));

            var completedEvent = new EntityCompletedEvent
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
                EntityType = options.EntityType,
                TotalProcessed = options.TotalProcessed,
                TotalSuccess = options.TotalSuccess,
                TotalFailed = options.TotalFailed,
                TotalSkipped = options.TotalSkipped,
                TotalCancelled = options.TotalCancelled,
                Status = options.Status ?? "completed",
                ShowTotalCount = options.ShowTotalCount ?? true, // 🎯 UI FLAG: Include display flag in SignalR event
                ProcessingTimeMs = (long)(options.ProcessingTimeMs ?? 0), // 🎯 FRONTEND-COMPAT: Use milliseconds for frontend compatibility
                CompletedDateTime = (options.CompletedDateTime ?? _dateTimeProvider.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
                Message = options.Message ?? $"Entity {options.EntityType} completed processing"
            };

            return completedEvent;
        }

        /// <summary>
        /// Creates a MigrationCompletedEvent with auto-populated base properties
        /// </summary>
        public MigrationCompletedEvent CreateMigrationCompleted(string migrationId, MigrationCompletedOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            if (string.IsNullOrWhiteSpace(options.Status))
                throw new ArgumentException("Status is required", nameof(options));

            var completedEvent = new MigrationCompletedEvent
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
                Status = options.Status,
                EndDateTime = options.EndDateTime ?? _dateTimeProvider.UtcNow,
                TotalDuration = options.TotalDuration ?? TimeSpan.Zero,
                FinalCounts = options.FinalCounts ?? new(),
                Entities = options.Entities ?? new(),
                Message = options.Message
            };

            return completedEvent;
        }

        /// <summary>
        /// Creates an ErrorProgressEvent with auto-populated base properties
        /// </summary>
        public ErrorProgressEvent CreateErrorProgress(string migrationId, ErrorProgressOptions options)
        {
            ValidateMigrationId(migrationId);
            ValidateRequired(options, nameof(options));

            if (string.IsNullOrWhiteSpace(options.ErrorMessage))
                throw new ArgumentException("ErrorMessage is required", nameof(options));

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
                
                // Specific properties
                Message = options.ErrorMessage,
                Severity = options.Severity ?? "error",
                EntityType = options.EntityType,
                EntityId = options.EntityId,
                ChunkNumber = options.ChunkNumber,
                Details = options.Exception?.ToString(), // Convert exception to string for details
                IsContinuable = options.IsContinuable ?? true
            };

            return errorEvent;
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
        /// Calculates progress percentage based on completed chunks
        /// </summary>
        private static double CalculateProgressPercentage(int chunkNumber, int totalChunks)
        {
            if (totalChunks <= 0) return 0;
            return Math.Min(100.0, (double)chunkNumber / totalChunks * 100.0);
        }

        #endregion
    }
}