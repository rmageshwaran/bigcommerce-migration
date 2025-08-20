using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Activities.Models;
using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity to broadcast migration completion events through the centralized progress broadcast service
/// Part of Phase 4: Migration Lifecycle Events
/// </summary>
public class BroadcastMigrationCompletedActivity
{
    private readonly ICentralizedProgressBroadcastService _centralizedBroadcastService;
    private readonly ILogger<BroadcastMigrationCompletedActivity> _logger;

    public BroadcastMigrationCompletedActivity(
        ICentralizedProgressBroadcastService centralizedBroadcastService,
        ILogger<BroadcastMigrationCompletedActivity> logger)
    {
        _centralizedBroadcastService = centralizedBroadcastService ?? throw new ArgumentNullException(nameof(centralizedBroadcastService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Broadcasts a migration completed/cancelled/failed event to notify the dashboard and other consumers
    /// </summary>
    /// <param name="request">The migration completed broadcast request</param>
    [Function("BroadcastMigrationCompletedActivity")]
    public async Task<BroadcastMigrationCompletedResponse> BroadcastMigrationCompleted(
        [ActivityTrigger] BroadcastMigrationCompletedRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            _logger.LogInformation("Broadcasting migration completed event for MigrationId: {MigrationId}, Status: {Status}, Processed: {ProcessedCount}",
                request.MigrationId, request.Status, request.TotalProcessedEntities);

            // Map to centralized broadcast service data structure
            var migrationCompletionInfo = new MigrationCompletionInfo
            {
                Status = request.Status,
                Message = request.Message,
                TotalProcessedEntities = request.TotalProcessedEntities,
                TotalFailedEntities = request.TotalFailedEntities,
                DurationMs = request.DurationMs
            };

            // Broadcast the migration completion event
            await _centralizedBroadcastService.BroadcastMigrationCompletedAsync(request.MigrationId, migrationCompletionInfo);

            _logger.LogInformation("Successfully broadcasted migration completed event for MigrationId: {MigrationId} with status: {Status}",
                request.MigrationId, request.Status);

            return new BroadcastMigrationCompletedResponse
            {
                IsSuccess = true,
                MigrationId = request.MigrationId,
                Status = request.Status,
                BroadcastTimestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast migration completed event for MigrationId: {MigrationId}", 
                request.MigrationId);

            return new BroadcastMigrationCompletedResponse
            {
                IsSuccess = false,
                MigrationId = request.MigrationId,
                Status = request.Status,
                ErrorMessage = ex.Message,
                BroadcastTimestamp = DateTime.UtcNow
            };
        }
    }
}

/// <summary>
/// Request model for broadcasting migration completion events
/// </summary>
public class BroadcastMigrationCompletedRequest
{
    /// <summary>
    /// The unique identifier of the migration
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// The final status of the migration (Completed, Failed, Cancelled)
    /// </summary>
    [Required]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Message describing the completion or failure reason
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Total number of entities that were processed
    /// </summary>
    public int TotalProcessedEntities { get; set; }

    /// <summary>
    /// Total number of entities that failed
    /// </summary>
    public int TotalFailedEntities { get; set; }

    /// <summary>
    /// Total migration duration in milliseconds
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Date and time when the migration ended
    /// </summary>
    public DateTime EndDateTime { get; set; }
}

/// <summary>
/// Response model for broadcasting migration completion events
/// </summary>
public class BroadcastMigrationCompletedResponse
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
    /// The status that was broadcasted
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Error message if the broadcast failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Timestamp when the broadcast was attempted
    /// </summary>
    public DateTime BroadcastTimestamp { get; set; }
}