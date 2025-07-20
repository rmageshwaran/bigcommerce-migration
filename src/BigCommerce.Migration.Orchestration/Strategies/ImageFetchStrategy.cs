using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Strategies;

/// <summary>
/// Fetch strategy for product image entities
/// Handles image-specific fetching logic requiring product context
/// </summary>
public class ImageFetchStrategy : IEntityFetchStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<ImageFetchStrategy> _logger;

    public string EntityType => "images";

    public ImageFetchStrategy(IBigCommerceApiClient apiClient, ILogger<ImageFetchStrategy> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(
        List<string> entityIds,
        string migrationId,
        StoreConfiguration sourceStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        // LSP COMPLIANCE: Consistent parameter validation across all strategies
        if (entityIds == null) throw new ArgumentNullException(nameof(entityIds));
        if (string.IsNullOrWhiteSpace(migrationId)) throw new ArgumentNullException(nameof(migrationId));
        if (sourceStore == null) throw new ArgumentNullException(nameof(sourceStore));
        
        // Handle cancellation first
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Fetching {Count} specific images for migration {MigrationId}", 
            entityIds.Count, migrationId);

        var fetchedImages = new List<Dictionary<string, object>>();

        try
        {
            // LSP COMPLIANCE: Check for cancellation consistently across all strategies
            cancellationToken.ThrowIfCancellationRequested();

            if (!entityIds.Any())
            {
                _logger.LogInformation("No specific image IDs provided for migration {MigrationId}", migrationId);
                return fetchedImages;
            }

            // Images require product context - use pagination to find images
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 50,
                SortBy = "id",
                SortDirection = "asc"
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    sourceStore,
                    "images",
                    paginationRequest,
                    cancellationToken);

                if (response.Data == null || response.Data.Count == 0)
                    break;

                // Filter to get only the requested images
                var filteredImages = response.Data
                    .Where(image => 
                    {
                        var imageId = image.TryGetValue("id", out var id) ? id.ToString() : null;
                        return imageId != null && entityIds.Contains(imageId);
                    })
                    .ToList();

                if (filteredImages.Any())
                {
                    // Add original entity ID tracking for error reporting
                    foreach (var image in filteredImages)
                    {
                        var imageId = image.TryGetValue("id", out var id) ? id.ToString() : null;
                        image["_original_entity_id"] = imageId;
                    }

                    fetchedImages.AddRange(filteredImages);
                }

                _logger.LogDebug("Fetched {Count} images from page {Page} (filtered: {FilteredCount})", 
                    response.Data.Count, paginationRequest.Page, filteredImages.Count);

                // If we found all requested images, stop fetching
                if (fetchedImages.Count >= entityIds.Count)
                    break;

                if (!response.HasNextPage)
                    break;

                paginationRequest.Page++;
            }

            _logger.LogInformation("Successfully fetched {FetchedCount}/{RequestedCount} images for migration {MigrationId}", 
                fetchedImages.Count, entityIds.Count, migrationId);

            return fetchedImages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch images for migration {MigrationId}", migrationId);
            
            // Return empty list to allow migration to continue with other batches
            return new List<Dictionary<string, object>>();
        }
    }
} 