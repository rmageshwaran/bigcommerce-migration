using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Strategies;

/// <summary>
/// Special discovery strategy for product-components and individual component entity types
/// Product-components and its individual types (options, modifiers, images, reviews) are not real BigCommerce API endpoints
/// This strategy queries the products endpoint with include parameters to get embedded components
/// Supports both the aggregated "product-components" and individual component types
/// </summary>
public class V3ProductComponentsDiscoveryStrategy : IEntityDiscoveryStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<V3ProductComponentsDiscoveryStrategy> _logger;
    private readonly ICancellationStore _cancellationStore;

    /// <summary>
    /// Gets the BigCommerce API version this strategy supports
    /// </summary>
    public BigCommerceApiVersion SupportedApiVersion => BigCommerceApiVersion.V3;

    public V3ProductComponentsDiscoveryStrategy(
        IBigCommerceApiClient apiClient, 
        ILogger<V3ProductComponentsDiscoveryStrategy> logger,
        ICancellationStore cancellationStore)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
    }

    /// <summary>
    /// Discovers product components by querying products with include parameters
    /// Returns the count of products that have components, not the individual components
    /// The actual component extraction happens during processing
    /// </summary>
    /// <param name="request">Discovery request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Discovery result with product count as component entity count</returns>
    public async Task<EntityDiscoveryResult> DiscoverEntitiesAsync(
        EntityDiscoveryRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check both standard and blob-based cancellation
            cancellationToken.ThrowIfCancellationRequested();
            await CheckCancellationAsync(request.MigrationId);

            _logger.LogInformation("🔍 [PRODUCT-COMPONENTS-DISCOVERY] Starting component discovery by querying products with includes for migration {MigrationId}", 
                request.MigrationId);
            
            var includeParam = request.EntityConfig?.Settings?.TryGetValue("include", out var includeValue) == true ? includeValue?.ToString() : null;
            _logger.LogDebug("🔍 [PRODUCT-COMPONENTS-DISCOVERY] Using include parameter: '{Include}'", includeParam ?? "options,modifiers,images,reviews");

            // Use the configured page size for discovery
            var discoveryLimit = request.EntityConfig.PageSize;
            
            // Query products endpoint with include parameters to get products that have components
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = discoveryLimit,
                IncludeDeleted = request.EntityConfig.IncludeDeleted,
                IncludeDrafts = request.EntityConfig.IncludeDrafts,
                SortBy = "id",
                SortDirection = "asc",
                // Include components in the products query
                Include = includeParam ?? "options,modifiers,images,reviews"
            };

                    _logger.LogInformation("🔍 [PRODUCT-COMPONENTS-DISCOVERY] Querying products endpoint with Include='{Include}', Limit={Limit}", 
            paginationRequest.Include, paginationRequest.Limit);
        
        _logger.LogInformation("🎯 [PRODUCT-COMPONENTS-DISCOVERY] DEBUG: EntityType='{EntityType}', MigrationId='{MigrationId}'", 
            request.EntityType, request.MigrationId);

            // Check cancellation before making API call
            cancellationToken.ThrowIfCancellationRequested();
            await CheckCancellationAsync(request.MigrationId);

            // Fetch ALL pages to get accurate component counts by type
            var componentCounts = new Dictionary<string, int>
            {
                { "options", 0 },
                { "modifiers", 0 },
                { "images", 0 },
                { "reviews", 0 }
            };
            
            var allProducts = new List<Dictionary<string, object>>();
            var currentPage = 1;
            var totalPages = 1;
            var totalProducts = 0;

            do
            {
                // Update pagination request for current page
                paginationRequest.Page = currentPage;
                
                _logger.LogInformation("🔍 [PRODUCT-COMPONENTS-DISCOVERY] Fetching page {CurrentPage} of products with include parameters", currentPage);
                
                // Check cancellation before each API call
                cancellationToken.ThrowIfCancellationRequested();
                await CheckCancellationAsync(request.MigrationId);

                // Call products endpoint (not product-components which doesn't exist)
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    request.SourceStore,
                    "products", // Query products, not product-components
                    paginationRequest,
                    cancellationToken);
                    
                _logger.LogInformation("🔍 [PRODUCT-COMPONENTS-DISCOVERY] Page {CurrentPage} response - TotalItems: {TotalItems}, TotalPages: {TotalPages}, Data.Count: {DataCount}", 
                    currentPage, response.TotalItems, response.TotalPages, response.Data?.Count);

                // Update totals from first response
                if (currentPage == 1)
                {
                    totalPages = response.TotalPages ?? 1;
                    totalProducts = response.TotalItems ?? 0;
                }

                var products = response.Data ?? new List<Dictionary<string, object>>();
                allProducts.AddRange(products);
                
                // Count components by type in this page's products
                foreach (var product in products)
                {
                    var productId = product.TryGetValue("id", out var id) ? id.ToString() : "unknown";
                    var productComponentCounts = new Dictionary<string, int>();
                    
                    _logger.LogDebug("🔍 [PRODUCT-COMPONENTS-DISCOVERY] Analyzing product {ProductId}: {ProductKeys}", 
                        productId, string.Join(", ", product.Keys));
                    
                    // Count options (handle both JSON string and array formats)
                    var optionsCount = ParseComponentCount(product, "options");
                    componentCounts["options"] += optionsCount;
                    productComponentCounts["options"] = optionsCount;
                    
                    // Count modifiers (handle both JSON string and array formats) 
                    var modifiersCount = ParseComponentCount(product, "modifiers");
                    componentCounts["modifiers"] += modifiersCount;
                    productComponentCounts["modifiers"] = modifiersCount;
                    
                    // Count images (handle both JSON string and array formats)
                    var imagesCount = ParseComponentCount(product, "images");
                    componentCounts["images"] += imagesCount;
                    productComponentCounts["images"] = imagesCount;
                    
                    // Count reviews (handle both JSON string and array formats)
                    var reviewsCount = ParseComponentCount(product, "reviews");
                    componentCounts["reviews"] += reviewsCount;
                    productComponentCounts["reviews"] = reviewsCount;
                    
                    var totalForProduct = productComponentCounts.Values.Sum();
                    _logger.LogDebug("🔍 [PRODUCT-COMPONENTS-DISCOVERY] Product {ProductId} has {TotalComponents} components: {ComponentBreakdown}", 
                        productId, totalForProduct, string.Join(", ", productComponentCounts.Select(kv => $"{kv.Key}:{kv.Value}")));
                }
                
                // Move to next page
                currentPage++;
                
            } while (currentPage <= totalPages);

            var totalComponentsCount = componentCounts.Values.Sum();
            _logger.LogInformation("✅ [PRODUCT-COMPONENTS-DISCOVERY] Completed discovery - Found {TotalComponents} individual components across {TotalProducts} products: {ComponentBreakdown}", 
                totalComponentsCount, totalProducts, string.Join(", ", componentCounts.Select(kv => $"{kv.Key}:{kv.Value}")));

            // Determine the count to return based on the requested entity type
            var countToReturn = request.EntityType.ToLowerInvariant() switch
            {
                "product-components" => totalComponentsCount, // Return total of all components
                "options" => componentCounts["options"],
                "modifiers" => componentCounts["modifiers"],
                "images" => componentCounts["images"],
                "reviews" => componentCounts["reviews"],
                _ => totalComponentsCount // Default to total
            };
            
            _logger.LogInformation("🎯 [COMPONENT-COUNT] For entity type '{EntityType}', returning count: {Count}", 
                request.EntityType, countToReturn);

            // Return discovery result with appropriate component count
            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType, // Keep the requested entity type
                TotalCount = countToReturn, // Count specific to the requested entity type
                EntityIds = new List<string>(), // Empty - we use page-based processing
                Errors = new List<string>(),
                ApiVersion = BigCommerceApiVersion.V3,
                // Discovery phase only counts - data is fetched later during processing
                EntityData = new List<Dictionary<string, object>>(),
                // Store pagination metadata for processing coordination
                PaginationMetadata = new Dictionary<string, object>
                {
                    { "TotalPages", totalPages },
                    { "PageSize", discoveryLimit },
                    { "TotalProducts", totalProducts },
                    { "TotalComponents", totalComponentsCount },
                    { "OptionsCount", componentCounts["options"] },
                    { "ModifiersCount", componentCounts["modifiers"] },
                    { "ImagesCount", componentCounts["images"] },
                    { "ReviewsCount", componentCounts["reviews"] },
                    { "ComponentCounts", componentCounts },
                    { "ApiVersion", "V3" },
                    { "Strategy", "ProductComponentsDiscovery" },
                    { "IncludeParameter", paginationRequest.Include ?? "options,modifiers,images,reviews" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PRODUCT-COMPONENTS-DISCOVERY] Failed to discover product components for migration {MigrationId}", 
                request.MigrationId);

            return new EntityDiscoveryResult
            {
                EntityType = request.EntityType,
                TotalCount = 0,
                EntityIds = new List<string>(),
                Errors = new List<string> { $"Product components discovery failed: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Helper method to check for blob-based cancellation
    /// Uses cooperative cancellation pattern suitable for Azure Functions activities
    /// </summary>
    private async Task CheckCancellationAsync(string migrationId)
    {
        try
        {
            var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
            if (isCancelled)
            {
                var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
                _logger.LogInformation("🚫 [CANCELLATION] Product-components discovery cancelled: {Reason}", reason);
                throw new OperationCanceledException($"Migration cancelled: {reason}");
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CANCELLATION-CHECK] Failed to check cancellation flag for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't throw - continue processing if cancellation check fails
        }
    }

    /// <summary>
    /// Parses component count from product data, handling both JSON string and array formats
    /// BigCommerce API sometimes returns arrays as JSON strings
    /// </summary>
    private int ParseComponentCount(Dictionary<string, object> product, string componentType)
    {
        if (!product.TryGetValue(componentType, out var componentObj))
            return 0;

        try
        {
            // Case 1: Already an array/list
            if (componentObj is IEnumerable<object> list)
            {
                return list.Count();
            }

            // Case 2: JSON string that needs to be parsed
            if (componentObj is string jsonString)
            {
                if (string.IsNullOrWhiteSpace(jsonString) || jsonString == "[]")
                    return 0;

                // Parse JSON array and count items
                var array = JsonSerializer.Deserialize<JsonElement[]>(jsonString);
                return array?.Length ?? 0;
            }

            // Case 3: JsonElement array
            if (componentObj is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Array)
                {
                    return jsonElement.GetArrayLength();
                }
                else if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    var jsonStr = jsonElement.GetString();
                    if (string.IsNullOrWhiteSpace(jsonStr) || jsonStr == "[]")
                        return 0;
                    
                    var array = JsonSerializer.Deserialize<JsonElement[]>(jsonStr);
                    return array?.Length ?? 0;
                }
            }

            _logger.LogDebug("🔍 [PARSE-COMPONENT] Unknown {ComponentType} format: {Type} = {Value}", 
                componentType, componentObj.GetType().Name, componentObj);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PARSE-COMPONENT] Failed to parse {ComponentType} for product: {ErrorMessage}", 
                componentType, ex.Message);
            return 0;
        }
    }
}