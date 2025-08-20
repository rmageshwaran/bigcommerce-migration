using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Activities.Activities
{
    /// <summary>
    /// Activity for broadcasting entity started events after discovery
    /// </summary>
    public class BroadcastEntityStartedActivity
    {
        private readonly ICentralizedProgressBroadcastService _centralizedBroadcastService;
        private readonly ILogger<BroadcastEntityStartedActivity> _logger;

        public BroadcastEntityStartedActivity(
            ICentralizedProgressBroadcastService centralizedBroadcastService,
            ILogger<BroadcastEntityStartedActivity> logger)
        {
            _centralizedBroadcastService = centralizedBroadcastService ?? throw new ArgumentNullException(nameof(centralizedBroadcastService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Broadcasts entity started event with discovered entity count
        /// </summary>
        [Function("BroadcastEntityStartedActivity")]
        public async Task<BroadcastEntityStartedResponse> Run(
            [ActivityTrigger] BroadcastEntityStartedRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            try
            {
                _logger.LogInformation("Broadcasting entity started event for MigrationId: {MigrationId}, EntityType: {EntityType}, TotalCount: {TotalCount}",
                    request.MigrationId, request.EntityType, request.TotalCount);

                // Broadcast the entity started event with discovered count
                await _centralizedBroadcastService.BroadcastEntityStartedAsync(
                    request.MigrationId, 
                    request.EntityType, 
                    request.TotalCount, 
                    request.Message ?? $"{request.EntityType} discovery completed - {request.TotalCount} entities found");

                _logger.LogInformation("Successfully broadcasted entity started event for MigrationId: {MigrationId}, EntityType: {EntityType}",
                    request.MigrationId, request.EntityType);

                return new BroadcastEntityStartedResponse
                {
                    IsSuccess = true,
                    MigrationId = request.MigrationId,
                    EntityType = request.EntityType,
                    BroadcastTimestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast entity started event for MigrationId: {MigrationId}, EntityType: {EntityType}",
                    request.MigrationId, request.EntityType);

                return new BroadcastEntityStartedResponse
                {
                    IsSuccess = false,
                    MigrationId = request.MigrationId,
                    EntityType = request.EntityType,
                    ErrorMessage = ex.Message,
                    BroadcastTimestamp = DateTime.UtcNow
                };
            }
        }
    }

    /// <summary>
    /// Request model for broadcasting entity started events
    /// </summary>
    public class BroadcastEntityStartedRequest
    {
        /// <summary>
        /// The unique identifier of the migration
        /// </summary>
        [Required]
        public string MigrationId { get; set; } = string.Empty;

        /// <summary>
        /// The type of entity that started (e.g., products, options, modifiers)
        /// </summary>
        [Required]
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// The total count of entities discovered for this type
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Optional message about the entity start
        /// </summary>
        public string? Message { get; set; }
    }

    /// <summary>
    /// Response model for broadcasting entity started events
    /// </summary>
    public class BroadcastEntityStartedResponse
    {
        /// <summary>
        /// Indicates whether the broadcast was successful
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// The migration ID that was broadcasted
        /// </summary>
        public string MigrationId { get; set; } = string.Empty;

        /// <summary>
        /// The entity type that was broadcasted
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Error message if the broadcast failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Timestamp when the broadcast was attempted
        /// </summary>
        public DateTime BroadcastTimestamp { get; set; }
    }
}