using System;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Core.Services
{
    /// <summary>
    /// Simplified SignalR Message Converter - Converts progress events to SignalR messages
    /// 
    /// ✅ SIMPLIFIED APPROACH:
    /// - Only handles 4 event types
    /// - Clean JSON serialization for SignalR
    /// - Consistent message format
    /// </summary>
    public interface ISignalRMessageConverter
    {
        /// <summary>
        /// Converts a progress event to a SignalR message JSON string
        /// </summary>
        /// <param name="progressEvent">The progress event to convert</param>
        /// <returns>JSON string ready for SignalR broadcasting</returns>
        string ConvertToSignalRMessage(ProgressEvent progressEvent);

        /// <summary>
        /// Converts a progress event to a strongly-typed SignalR message object
        /// </summary>
        /// <param name="progressEvent">The progress event to convert</param>
        /// <returns>SignalR message object</returns>
        SignalRMessage ConvertToSignalRMessageObject(ProgressEvent progressEvent);
    }

    /// <summary>
    /// SignalR message wrapper for broadcasting
    /// </summary>
    public class SignalRMessage
    {
        /// <summary>
        /// The SignalR hub method to invoke
        /// </summary>
        public string HubMethod { get; set; } = string.Empty;

        /// <summary>
        /// The event data to send
        /// </summary>
        public object EventData { get; set; } = new();

        /// <summary>
        /// Optional connection ID for targeted messaging
        /// </summary>
        public string? ConnectionId { get; set; }

        /// <summary>
        /// Optional group name for group messaging
        /// </summary>
        public string? GroupName { get; set; }
    }

    /// <summary>
    /// Simplified SignalR Message Converter implementation
    /// </summary>
    public class SignalRMessageConverter : ISignalRMessageConverter
    {
        private readonly ILogger<SignalRMessageConverter> _logger;

        /// <summary>
        /// Initializes a new instance of the SignalRMessageConverter
        /// </summary>
        /// <param name="logger">Logger for diagnostic information</param>
        public SignalRMessageConverter(ILogger<SignalRMessageConverter> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Converts a progress event to a JSON string for SignalR broadcasting
        /// </summary>
        public string ConvertToSignalRMessage(ProgressEvent progressEvent)
        {
            try
            {
                if (progressEvent == null)
                {
                    _logger.LogWarning("⚠️ Cannot convert null progress event to SignalR message");
                    return "{}";
                }

                var message = ConvertToSignalRMessageObject(progressEvent);
                var json = System.Text.Json.JsonSerializer.Serialize(message, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                _logger.LogDebug("✅ Converted {EventType} event to SignalR message for migration {MigrationId}", 
                    progressEvent.EventType, progressEvent.MigrationId);

                return json;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to convert progress event to SignalR message: {EventType} for migration {MigrationId}", 
                    progressEvent?.EventType, progressEvent?.MigrationId);
                return "{}";
            }
        }

        /// <summary>
        /// Converts a progress event to a SignalR message object
        /// </summary>
        public SignalRMessage ConvertToSignalRMessageObject(ProgressEvent progressEvent)
        {
            if (progressEvent == null)
                throw new ArgumentNullException(nameof(progressEvent));

            return new SignalRMessage
            {
                HubMethod = progressEvent.HubMethod,
                EventData = progressEvent,
                ConnectionId = progressEvent.ConnectionId,
                GroupName = progressEvent.GroupName
            };
        }
    }

    /// <summary>
    /// Enhanced Progress Event Publisher that uses the simplified factory and converter
    /// 
    /// ✅ SIMPLIFIED APPROACH:
    /// - Only 4 methods for 4 event types
    /// - Uses the centralized factory for event creation
    /// - Consistent publishing pattern
    /// </summary>
    public interface IEnhancedProgressEventPublisher
    {
        /// <summary>
        /// Publishes a migration started event using the centralized factory
        /// </summary>
        Task PublishMigrationStartedAsync(string migrationId, MigrationStartedOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes an entity chunk progress event using the centralized factory
        /// This is the main progress event published during migration
        /// </summary>
        Task PublishEntityChunkProgressAsync(string migrationId, EntityChunkProgressOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes an entity completed event using the centralized factory
        /// </summary>
        Task PublishEntityCompletedAsync(string migrationId, EntityCompletedOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes a migration completed event using the centralized factory
        /// </summary>
        Task PublishMigrationCompletedAsync(string migrationId, MigrationCompletedOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes an error progress event using the centralized factory
        /// </summary>
        Task PublishErrorProgressAsync(string migrationId, ErrorProgressOptions options, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Enhanced Progress Event Publisher implementation
    /// </summary>
    public class EnhancedProgressEventPublisher : IEnhancedProgressEventPublisher
    {
        private readonly ISignalREventFactory _eventFactory;
        private readonly IProgressEventPublisher _publisher;
        private readonly ILogger<EnhancedProgressEventPublisher> _logger;

        /// <summary>
        /// Initializes a new instance of the EnhancedProgressEventPublisher
        /// </summary>
        public EnhancedProgressEventPublisher(
            ISignalREventFactory eventFactory,
            IProgressEventPublisher publisher,
            ILogger<EnhancedProgressEventPublisher> logger)
        {
            _eventFactory = eventFactory ?? throw new ArgumentNullException(nameof(eventFactory));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Publishes a migration started event
        /// </summary>
        public async Task PublishMigrationStartedAsync(string migrationId, MigrationStartedOptions options, CancellationToken cancellationToken = default)
        {
            try
            {
                var startedEvent = _eventFactory.CreateMigrationStarted(migrationId, options);
                await _publisher.PublishMigrationStartedAsync(startedEvent, cancellationToken).ConfigureAwait(false);
                
                _logger.LogInformation("📢 Published migration started event for {MigrationId}", migrationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to publish migration started event for {MigrationId}", migrationId);
                throw;
            }
        }

        /// <summary>
        /// Publishes an entity chunk progress event
        /// </summary>
        public async Task PublishEntityChunkProgressAsync(string migrationId, EntityChunkProgressOptions options, CancellationToken cancellationToken = default)
        {
            try
            {
                var chunkEvent = _eventFactory.CreateEntityChunkProgress(migrationId, options);
                await _publisher.PublishEntityChunkProgressAsync(chunkEvent, cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug("📢 Published chunk progress event for {MigrationId} - {EntityType} chunk {ChunkNumber}/{TotalChunks}", 
                    migrationId, options.EntityType, options.ChunkNumber, options.TotalChunks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to publish chunk progress event for {MigrationId} - {EntityType} chunk {ChunkNumber}", 
                    migrationId, options.EntityType, options.ChunkNumber);
                throw;
            }
        }

        /// <summary>
        /// Publishes an entity completed event
        /// </summary>
        public async Task PublishEntityCompletedAsync(string migrationId, EntityCompletedOptions options, CancellationToken cancellationToken = default)
        {
            try
            {
                var completedEvent = _eventFactory.CreateEntityCompleted(migrationId, options);
                await _publisher.PublishEntityCompletedAsync(completedEvent, cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug("📢 Published entity completed event for {MigrationId} - {EntityType}", 
                    migrationId, options.EntityType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to publish entity completed event for {MigrationId} - {EntityType}", 
                    migrationId, options.EntityType);
                throw;
            }
        }

        /// <summary>
        /// Publishes a migration completed event
        /// </summary>
        public async Task PublishMigrationCompletedAsync(string migrationId, MigrationCompletedOptions options, CancellationToken cancellationToken = default)
        {
            try
            {
                var completedEvent = _eventFactory.CreateMigrationCompleted(migrationId, options);
                await _publisher.PublishMigrationCompletedAsync(completedEvent, cancellationToken).ConfigureAwait(false);
                
                _logger.LogInformation("📢 Published migration completed event for {MigrationId} with status {Status}", 
                    migrationId, options.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to publish migration completed event for {MigrationId}", migrationId);
                throw;
            }
        }

        /// <summary>
        /// Publishes an error progress event
        /// </summary>
        public async Task PublishErrorProgressAsync(string migrationId, ErrorProgressOptions options, CancellationToken cancellationToken = default)
        {
            try
            {
                var errorEvent = _eventFactory.CreateErrorProgress(migrationId, options);
                await _publisher.PublishErrorAsync(errorEvent, cancellationToken).ConfigureAwait(false);
                
                _logger.LogWarning("📢 Published error event for {MigrationId}: {ErrorMessage}", 
                    migrationId, options.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to publish error event for {MigrationId}", migrationId);
                throw;
            }
        }
    }
}