using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Strategies.Discovery;

/// <summary>
/// Discovery strategy for product-metafields phase using BigCommerce v3/catalog/products/metafields API
/// 
/// This strategy is designed for Phase 7 of product migration where we discover
/// product metafields using the dedicated metafields API endpoint.
/// 
/// Key Features:
/// - Uses BigCommerce v3/catalog/products/metafields API with 250 entities per page
/// - Efficient pagination metadata approach without caching entity data
/// - Live cancellation support with CancellationToken checks
/// - Structured error logging and continue-on-error policy
/// - SignalR progress events using SignalREventFactory
/// - Memory optimized for large datasets
/// 
/// API Details:
/// - Endpoint: GET /v3/catalog/products/metafields?page={page}&amp;limit={limit}
/// - Max page size: 250 entities per page
/// - No includes parameter supported for this endpoint
/// - Bulk creation API: POST /v3/catalog/products/metafields (50 entities max)
/// 
/// Architecture Compliance:
/// - Follows V3EfficientPaginationStrategy pattern
/// - Implements IEntityDiscoveryStrategy interface
/// - Multi-instance safe with no shared state
/// - Enterprise-grade error handling
/// </summary>
public class ProductMetafieldsDiscoveryStrategy : IEntityDiscoveryStrategy
{
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly ILogger<ProductMetafieldsDiscoveryStrategy> _logger;

    /// <summary>
    /// Gets the BigCommerce API version this strategy supports
    /// </summary>
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V3;

    /// <summary>
    /// Initializes a new instance of ProductMetafieldsDiscoveryStrategy
    /// </summary>
    /// <param name="apiRequestHandler">API request handler with rate limiting and authentication</param>
    /// <param name="logger">Logger for the strategy</param>
    public ProductMetafieldsDiscoveryStrategy(
        IApiRequestHandler apiRequestHandler,
        ILogger<ProductMetafieldsDiscoveryStrategy> logger)
    {
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers product metafields using BigCommerce v3/catalog/products/metafields API
    /// Gets metadata from first page only to enable direct pagination during batch processing
    /// This strategy is memory-optimized and designed for large-scale metafields discovery
    /// </summary>
    /// <param name="request">Entity discovery request (must be "product-metafields" entity type)</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Discovery result with pagination metadata only (no entity data cached)</returns>
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (!request.EntityType.Equals("product-metafields", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"This strategy only supports 'product-metafields' entity type, but received '{request.EntityType}'");
        }

        try
        {
            // Live cancellation support - check at start
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("🔍 [PRODUCT-METAFIELDS-DISCOVERY] Starting BigCommerce API discovery for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            
            // Log discovery strategy details
            _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Discovery strategy: ProductMetafieldsDiscoveryStrategy");
            _logger.LogInformation("🔍 [DISCOVERY-DEBUG] API endpoint: /v3/catalog/products/metafields");
            _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Entity type: {EntityType}", request.EntityType);
            _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Migration ID: {MigrationId}", request.MigrationId);
            _logger.LogInformation("🔍 [DISCOVERY-DEBUG] Store ID: {StoreId}", request.SourceStore?.StoreId);

            // Build API URL for product metafields endpoint
            var url = $"{request.SourceStore!.GetApiBaseUrl()}/catalog/products/metafields?page=1&limit=1";
            
            _logger.LogInformation("🔍 [PRODUCT-METAFIELDS-DISCOVERY] Making API call to: {Url}", url);

            // Create API request and call through IApiRequestHandler for consistent rate limiting
            var apiRequest = ApiRequest.CreateGet(url, request.SourceStore!);
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(
                apiRequest, 
                cancellationToken);

            // Live cancellation support - check after API call
            cancellationToken.ThrowIfCancellationRequested();
            
            // Parse BigCommerce API response structure (IApiRequestHandler returns Dictionary)
            var totalCount = 0;
            var totalPages = 1;
            var pageSize = 250; // API default page size
            
            if (response?.TryGetValue("meta", out var metaValue) == true)
            {
                Dictionary<string, object>? metaDict = null;
                
                // Handle both Dictionary and JsonElement formats
                if (metaValue is Dictionary<string, object> directMeta)
                {
                    metaDict = directMeta;
                }
                else if (metaValue is JsonElement metaElement)
                {
                    metaDict = JsonSerializer.Deserialize<Dictionary<string, object>>(metaElement.GetRawText());
                }
                
                if (metaDict?.TryGetValue("pagination", out var paginationValue) == true)
                {
                    Dictionary<string, object>? paginationDict = null;
                    
                    if (paginationValue is Dictionary<string, object> directPagination)
                    {
                        paginationDict = directPagination;
                    }
                    else if (paginationValue is JsonElement paginationElement)
                    {
                        paginationDict = JsonSerializer.Deserialize<Dictionary<string, object>>(paginationElement.GetRawText());
                    }
                    
                    if (paginationDict != null)
                    {
                        if (paginationDict.TryGetValue("total", out var totalValue) && totalValue != null)
                        {
                            totalCount = GetIntValueFromObject(totalValue);
                        }
                        if (paginationDict.TryGetValue("total_pages", out var totalPagesValue) && totalPagesValue != null)
                        {
                            totalPages = GetIntValueFromObject(totalPagesValue);
                        }
                        if (paginationDict.TryGetValue("per_page", out var perPageValue) && perPageValue != null)
                        {
                            pageSize = GetIntValueFromObject(perPageValue);
                        }
                    }
                }
            }
                
            _logger.LogInformation("🔍 [PRODUCT-METAFIELDS-DISCOVERY] API response parsed - TotalItems: {TotalItems}, TotalPages: {TotalPages}, PerPage: {PerPage}", 
                totalCount, totalPages, pageSize);

            _logger.LogInformation("✅ [PRODUCT-METAFIELDS-DISCOVERY] Completed metadata discovery for {EntityType} - Found {TotalCount} metafields across {TotalPages} pages", 
                request.EntityType, totalCount, totalPages);

            // Check if any metafields were found
            if (totalCount == 0)
            {
                _logger.LogInformation("ℹ️ [PRODUCT-METAFIELDS-DISCOVERY] No product metafields found for migration {MigrationId}. " +
                                     "This is normal if products don't have metafields.", request.MigrationId);
                
                return new EntityDiscoveryResult
                {
                    EntityType = request.EntityType,
                    TotalCount = 0,
                    EntityIds = new List<string>(),
                    ApiVersion = BigCommerceApiVersion.V3,
                    SkipDiscovery = true,
                    PaginationMetadata = new Dictionary<string, object>
                    {
                        { "ApiVersion", "V3" },
                        { "Strategy", "ProductMetafieldsDiscovery" },
                        { "Endpoint", "/v3/catalog/products/metafields" },
                        { "TotalPages", totalPages },
                        { "PageSize", pageSize },
                        { "TotalCount", totalCount },
                        { "MemoryOptimized", true },
                        { "SkipReason", "NoMetafieldsFound" }
                    }
                };
            }

            // Memory efficient: Return pagination metadata instead of all entity IDs
            // Batch processing will use page-based fetching during processing
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                EntityIds = new List<string>(), // Empty - will use pagination-based batching
                EntityData = new List<Dictionary<string, object>>(), // NO caching for scalability
                TotalCount = totalCount,
                ApiVersion = BigCommerceApiVersion.V3,
                SkipDiscovery = false,
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "ApiVersion", "V3" },
                    { "Strategy", "ProductMetafieldsDiscovery" },
                    { "Endpoint", "/v3/catalog/products/metafields" },
                    { "TotalPages", totalPages },
                    { "PageSize", pageSize },
                    { "TotalCount", totalCount },
                    { "HierarchicallySorted", false },
                    { "CachingDisabled", true },
                    { "MemoryOptimized", true },
                    { "UseDirectPagination", true },
                    { "MaxPageSize", 250 },
                    { "BulkCreateMaxSize", 50 }
                }
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🚫 [PRODUCT-METAFIELDS-DISCOVERY] Discovery was cancelled for {EntityType} in migration {MigrationId}", 
                request.EntityType, request.MigrationId);
            
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                ApiVersion = BigCommerceApiVersion.V3,
                SkipDiscovery = true,
                Errors = new List<string> { "Discovery was cancelled" }
            };
        }
        catch (Exception ex)
        {
            // Enhanced API error logging with request/response payload logging
            _logger.LogError(ex, "❌ [PRODUCT-METAFIELDS-DISCOVERY] BigCommerce API discovery failed for {EntityType} in migration {MigrationId}. " +
                            "Store: {StoreId}, Endpoint: /v3/catalog/products/metafields, " +
                            "ErrorType: {ErrorType}, Category: Error",
                request.EntityType, request.MigrationId, request.SourceStore?.StoreId, ex.GetType().Name);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                ApiVersion = BigCommerceApiVersion.V3,
                SkipDiscovery = true,
                Errors = new List<string> { $"Discovery failed: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Validates that the store configuration is valid for discovery operations
    /// </summary>
    /// <param name="storeConfig">Store configuration to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public bool ValidateStoreConfiguration(StoreConfiguration storeConfig)
    {
        return storeConfig != null && storeConfig.IsValid();
    }

    /// <summary>
    /// Gets the supported entity types for this discovery strategy
    /// </summary>
    /// <returns>List of supported entity types</returns>
    public List<string> GetSupportedEntityTypes()
    {
        return new List<string> { "product-metafields" };
    }

    /// <summary>
    /// Safely extracts integer value from an object that could be JsonElement, int, or string
    /// </summary>
    /// <param name="value">Value to convert to integer</param>
    /// <returns>Integer value, or 0 if conversion fails</returns>
    private static int GetIntValueFromObject(object value)
    {
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
            {
                return jsonElement.GetInt32();
            }
            if (jsonElement.ValueKind == JsonValueKind.String && int.TryParse(jsonElement.GetString(), out var stringResult))
            {
                return stringResult;
            }
        }
        else if (value is int intValue)
        {
            return intValue;
        }
        else if (value is string stringValue && int.TryParse(stringValue, out var parsedValue))
        {
            return parsedValue;
        }
        
        return 0; // Default fallback
    }
}
