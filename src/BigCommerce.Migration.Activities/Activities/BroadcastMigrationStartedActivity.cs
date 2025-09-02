using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;
using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity to broadcast migration started events through the centralized progress broadcast service
/// Part of Phase 4: Migration Lifecycle Events
/// </summary>
public class BroadcastMigrationStartedActivity
{
    private readonly ICentralizedProgressBroadcastService _centralizedBroadcastService;
    private readonly ILogger<BroadcastMigrationStartedActivity> _logger;

    public BroadcastMigrationStartedActivity(
        ICentralizedProgressBroadcastService centralizedBroadcastService,
        ILogger<BroadcastMigrationStartedActivity> logger)
    {
        _centralizedBroadcastService = centralizedBroadcastService ?? throw new ArgumentNullException(nameof(centralizedBroadcastService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Broadcasts a migration started event to notify the dashboard and other consumers
    /// </summary>
    /// <param name="request">The migration started broadcast request</param>
    [Function("BroadcastMigrationStartedActivity")]
    public async Task<BroadcastMigrationStartedResponse> BroadcastMigrationStarted(
        [ActivityTrigger] BroadcastMigrationStartedRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            _logger.LogInformation("Broadcasting migration started event for MigrationId: {MigrationId}, Source: {SourceStore}, Destination: {DestinationStore}",
                request.MigrationId, request.SourceStore, request.DestinationStore);

            // 🎯 COMPONENT PROGRESS: Transform entities list for UI - replace product-components with individual components
            var transformedEntities = TransformEntitiesForComponentTracking(request.Entities ?? new List<EntityInfo>());
            
            // Broadcast the migration started event with transformed entity list (counts will be 0 initially)
            await _centralizedBroadcastService.BroadcastMigrationStartedAsync(
                request.MigrationId, 
                request.SourceStore, 
                request.DestinationStore, 
                transformedEntities);

            _logger.LogInformation("Successfully broadcasted migration started event for MigrationId: {MigrationId}",
                request.MigrationId);

            return new BroadcastMigrationStartedResponse
            {
                IsSuccess = true,
                MigrationId = request.MigrationId,
                BroadcastTimestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast migration started event for MigrationId: {MigrationId}", 
                request.MigrationId);

            return new BroadcastMigrationStartedResponse
            {
                IsSuccess = false,
                MigrationId = request.MigrationId,
                ErrorMessage = ex.Message,
                BroadcastTimestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Transforms entities list for UI display - replaces product-components with individual options, modifiers, reviews
    /// This allows efficient processing (single product-components phase) while providing granular UI visibility
    /// </summary>
    /// <param name="entities">Original entities list from orchestrator</param>
    /// <returns>Transformed entities list with individual components</returns>
    private List<EntityInfo> TransformEntitiesForComponentTracking(List<EntityInfo> entities)
    {
        var transformedEntities = new List<EntityInfo>();

        foreach (var entity in entities)
        {
            if (entity.EntityType.Equals("product-components", StringComparison.OrdinalIgnoreCase))
            {
                // 🎯 REPLACE: product-components with individual component types for UI visibility
                _logger.LogInformation("🔄 Transforming product-components into individual component entities for UI tracking");
                
                var componentTypes = new[] { "options", "modifiers", "reviews" };
                foreach (var componentType in componentTypes)
                {
                    transformedEntities.Add(new EntityInfo
                    {
                        EntityType = componentType,
                        TotalCount = 0, // Will be updated by individual EntityStarted events
                        EstimatedDuration = null
                    });
                }
            }
            else
            {
                // Keep other entities as-is
                transformedEntities.Add(entity);
            }
        }

        _logger.LogInformation("🔄 Entity transformation complete: {OriginalCount} → {TransformedCount} entities", 
            entities.Count, transformedEntities.Count);

        return transformedEntities;
    }
}

/// <summary>
/// Request model for broadcasting migration started events
/// </summary>
public class BroadcastMigrationStartedRequest
{
    /// <summary>
    /// The unique identifier of the migration
    /// </summary>
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// The name of the source store
    /// </summary>
    [Required]
    public string SourceStore { get; set; } = string.Empty;

    /// <summary>
    /// The name of the destination store
    /// </summary>
    [Required]
    public string DestinationStore { get; set; } = string.Empty;

    /// <summary>
    /// The date and time when the migration started
    /// </summary>
    public DateTime StartDateTime { get; set; }

    /// <summary>
    /// The estimated end time of the migration (optional)
    /// </summary>
    public DateTime? EstimatedEndTime { get; set; }

    /// <summary>
    /// List of entities to be migrated
    /// </summary>
    public List<EntityInfo> Entities { get; set; } = new();
}

/// <summary>
/// Response model for broadcasting migration started events
/// </summary>
public class BroadcastMigrationStartedResponse
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
    /// Error message if the broadcast failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Timestamp when the broadcast was attempted
    /// </summary>
    public DateTime BroadcastTimestamp { get; set; }
}