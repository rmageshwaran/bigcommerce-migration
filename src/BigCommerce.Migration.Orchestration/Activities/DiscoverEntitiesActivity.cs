using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for discovering entities in BigCommerce stores
/// Implements Azure Durable Functions activity pattern
/// </summary>
public class DiscoverEntitiesActivity
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<DiscoverEntitiesActivity> _logger;

    public DiscoverEntitiesActivity(IBigCommerceApiClient apiClient, ILogger<DiscoverEntitiesActivity> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers entities of a specific type from source store
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Entity discovery result</returns>
    [Function("DiscoverEntities")]
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync([ActivityTrigger] EntityDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Discovering entities for migration {MigrationId}, Entity Type: {EntityType}", 
                request.MigrationId, request.EntityType);

            // Validate request
            var validationErrors = ValidateRequest(request);
            if (validationErrors.Any())
            {
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0,
                    EntityIds = new List<string>(),
                    Errors = validationErrors
                };
            }

            // Discover entities using pagination approach
            var result = await DiscoverEntitiesWithPaginationAsync(request, cancellationToken);

            _logger.LogInformation("Discovery completed for {EntityType}: {Count} entities found", 
                request.EntityType, result.TotalCount);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Entity discovery was cancelled for migration {MigrationId}, Entity Type: {EntityType}",
                request.MigrationId, request.EntityType);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { "Discovery was cancelled" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering entities for migration {MigrationId}, Entity Type: {EntityType}", 
                request.MigrationId, request.EntityType);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { ex.Message }
            };
        }
    }

    /// <summary>
    /// Discovers entities using pagination approach - gets first page and metadata for coordination
    /// </summary>
    /// <param name="request">Entity discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task<EntityDiscoveryResult> DiscoverEntitiesWithPaginationAsync(EntityDiscoveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation(
                "Starting paginated discovery for {EntityType} in migration {MigrationId}",
                request.EntityType,
                request.MigrationId);

            // Create pagination request for first page
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 50, // Start with reasonable page size
                IncludeDeleted = request.EntityConfig.IncludeDeleted,
                IncludeDrafts = request.EntityConfig.IncludeDrafts,
                SortBy = "id",
                SortDirection = "asc"
            };

            cancellationToken.ThrowIfCancellationRequested();

            // Get first page to understand pagination structure and total counts
            var firstPageResponse = await _apiClient.GetPaginatedEntitiesAsync(
                request.SourceStore,
                request.EntityType,
                paginationRequest,
                cancellationToken);

            if (firstPageResponse == null)
            {
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0,
                    EntityIds = new List<string>(),
                    Errors = new List<string> { "No response received from BigCommerce API" }
                };
            }

            // Extract entity IDs from first page
            var firstPageEntityIds = ExtractEntityIds(firstPageResponse.Data, request.EntityType);

            // Create enhanced discovery result with pagination metadata
            var result = new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = firstPageResponse.TotalItems ?? EstimateTotalCount(firstPageResponse),
                EntityIds = firstPageEntityIds,
                Errors = new List<string>()
            };

            // Add pagination metadata for orchestrator coordination
            result.PaginationMetadata = new Dictionary<string, object>
            {
                ["ApiVersion"] = firstPageResponse.ApiVersion.ToString(),
                ["CurrentPage"] = firstPageResponse.CurrentPage,
                ["PerPage"] = firstPageResponse.PerPage,
                ["TotalPages"] = firstPageResponse.TotalPages ?? EstimateTotalPages(firstPageResponse),
                ["HasNextPage"] = firstPageResponse.HasNextPage,
                ["IsLastPage"] = firstPageResponse.IsLastPage,
                ["ResponseTimeMs"] = firstPageResponse.ResponseTimeMs,
                ["RequiresPagination"] = (firstPageResponse.TotalItems ?? EstimateTotalCount(firstPageResponse)) > firstPageResponse.PerPage
            };

            _logger.LogInformation(
                "First page discovery completed for {EntityType} - API: {ApiVersion}, Total: {TotalCount}, Pages: {TotalPages}, First page: {FirstPageCount} entities",
                request.EntityType,
                firstPageResponse.ApiVersion,
                result.TotalCount,
                firstPageResponse.TotalPages ?? EstimateTotalPages(firstPageResponse),
                firstPageEntityIds.Count);

            return result;
        }
        catch (BigCommerceApiException ex)
        {
            _logger.LogError(ex,
                "BigCommerce API error during discovery for {EntityType}: {ErrorMessage}",
                request.EntityType,
                ex.Message);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { $"BigCommerce API error: {ex.Message}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error during discovery for {EntityType}: {ErrorMessage}",
                request.EntityType,
                ex.Message);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { $"Unexpected error: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Estimates total count for V2 API or when metadata is unavailable
    /// </summary>
    private static int EstimateTotalCount(BigCommercePaginatedResponse<Dictionary<string, object>> response)
    {
        if (response.TotalItems.HasValue)
            return response.TotalItems.Value;

        // For V2 API, estimate based on first page data
        if (response.Data.Count < response.PerPage)
        {
            // If first page is not full, it's likely the only page
            return response.Data.Count;
        }

        // Conservative estimate - assume there are more pages
        return response.Data.Count * 10; // Rough estimate for planning
    }

    /// <summary>
    /// Estimates total pages for V2 API or when metadata is unavailable
    /// </summary>
    private static int EstimateTotalPages(BigCommercePaginatedResponse<Dictionary<string, object>> response)
    {
        if (response.TotalPages.HasValue)
            return response.TotalPages.Value;

        var estimatedTotal = EstimateTotalCount(response);
        return (int)Math.Ceiling((double)estimatedTotal / response.PerPage);
    }

    /// <summary>
    /// Extracts entity IDs from response data
    /// </summary>
    private static List<string> ExtractEntityIds(List<Dictionary<string, object>> entities, string entityType)
    {
        var entityIds = new List<string>();
        
        foreach (var entity in entities)
        {
            if (entity.TryGetValue("id", out var idValue))
            {
                entityIds.Add(idValue.ToString() ?? string.Empty);
            }
            else if (entity.TryGetValue("Id", out var idValueCapital))
            {
                entityIds.Add(idValueCapital.ToString() ?? string.Empty);
            }
        }
        
        return entityIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
    }

    // Legacy DiscoverProductsAsync method removed - now handled by DiscoverEntitiesWithPaginationAsync

    // Legacy DiscoverCategoriesAsync method removed - now handled by DiscoverEntitiesWithPaginationAsync

    // Legacy DiscoverBrandsAsync method removed - now handled by DiscoverEntitiesWithPaginationAsync

    // Legacy DiscoverProductVariantsAsync, DiscoverProductImagesAsync, and DiscoverProductModifiersAsync methods removed - now handled by DiscoverEntitiesWithPaginationAsync

    /// <summary>
    /// Validates the entity discovery request
    /// </summary>
    private List<string> ValidateRequest(EntityDiscoveryRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(request.MigrationId))
        {
            errors.Add("MigrationId is required");
        }

        if (string.IsNullOrEmpty(request.EntityType))
        {
            errors.Add("EntityType is required");
        }

        if (request.SourceStore == null)
        {
            errors.Add("SourceStore is required");
        }
        else
        {
            if (string.IsNullOrEmpty(request.SourceStore.StoreId))
            {
                errors.Add("SourceStore.StoreId is required");
            }

            if (string.IsNullOrEmpty(request.SourceStore.AccessToken))
            {
                errors.Add("SourceStore.AccessToken is required");
            }
        }

        return errors;
    }
} 