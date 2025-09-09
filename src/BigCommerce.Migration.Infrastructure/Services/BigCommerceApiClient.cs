using System.Text.Json;
using System.Text.Json.Serialization;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// BigCommerce API client implementation using delegation pattern
/// Delegates HTTP concerns to IApiRequestHandler following Single Responsibility Principle
/// Focuses solely on business logic and API endpoint construction
/// </summary>
public class BigCommerceApiClient : IBigCommerceApiClient
{
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly ILogger<BigCommerceApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the BigCommerceApiClient with delegation pattern
    /// </summary>
    /// <param name="apiRequestHandler">API request handler for HTTP concerns</param>
    /// <param name="logger">Logger instance for service operations</param>
    public BigCommerceApiClient(
        IApiRequestHandler apiRequestHandler,
        ILogger<BigCommerceApiClient> logger)
    {
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets category trees for a specific store and channel using correct BigCommerce API syntax
    /// </summary>
    public async Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        // This method is now a pass-through to the multi-channel version.
        // It's kept for backward compatibility with any components that haven't been updated yet.
        // Note: The ChannelId is no longer on StoreConfiguration, so this method's logic needs to be considered.
        // For now, we will assume it fetches all trees if no channel is specified.
        return await GetCategoryTreesAsync(storeConfig, new List<string>(), cancellationToken);
    }

    /// <summary>
    /// Gets category trees for a specific store and a list of channels using correct BigCommerce API syntax
    /// </summary>
    public async Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(StoreConfiguration storeConfig, List<string> channelIds, CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/trees";
        if (channelIds.Any())
        {
            url += $"?channel_id:in={string.Join(",", channelIds)}";
        }

        try
        {
            var request = ApiRequest.CreateGet(url, storeConfig);
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var trees = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(dataElement.GetRawText()) ?? new List<Dictionary<string, object>>();
                
                _logger.LogDebug("Retrieved {TreeCount} category trees for store {StoreId}, channels [{Channels}]", 
                    trees.Count, storeConfig.StoreId, string.Join(",", channelIds));
                
                return trees;
            }

            return new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get category trees for store {StoreId}, channels [{Channels}]", 
                storeConfig.StoreId, string.Join(",", channelIds));
            throw;
        }
    }

    /// <summary>
    /// Gets categories for a specific category tree
    /// </summary>
    public async Task<List<Dictionary<string, object>>> GetCategoriesAsync(StoreConfiguration storeConfig, string categoryTreeId, CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/trees/{categoryTreeId}/categories";

        try
        {
            var request = ApiRequest.CreateGet(url, storeConfig);
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var categories = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(dataElement.GetRawText()) ?? new List<Dictionary<string, object>>();
                return categories;
            }

            return new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get categories for store {StoreId}, tree {TreeId}", 
                storeConfig.StoreId, categoryTreeId);
            throw;
        }
    }

    /// <summary>
    /// Creates categories using the correct BigCommerce API endpoint
    /// </summary>
    public async Task<List<Dictionary<string, object>>> CreateCategoriesAsync(StoreConfiguration storeConfig
        , List<Dictionary<string, object>> categories
        , CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        // Use the correct BigCommerce V3 Category Trees API endpoint for creating categories
        // BigCommerce V3 Category Trees API: POST /catalog/trees/categories (per official documentation)
        // Reference: https://developer.bigcommerce.com/docs/rest-catalog/category-trees/categories#create-categories
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/trees/categories";
        
        _logger.LogDebug("Creating {Count} categories for store {StoreId}", 
            categories.Count, storeConfig.StoreId);

        // Serialize categories to JSON for the API call
        var jsonContent = JsonSerializer.Serialize(categories);
        _logger.LogDebug("Category creation payload: {JsonPayload}", jsonContent);
        
        // Log category details at debug level if needed
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            for (int i = 0; i < categories.Count; i++)
            {
                var category = categories[i];
                var name = category.GetValueOrDefault("name");
                var treeId = category.GetValueOrDefault("tree_id");
                var hasUrl = category.ContainsKey("url");
                var hasTreeId = category.ContainsKey("tree_id");
                
                _logger.LogDebug("Category[{Index}] - name: {Name}, tree_id: {TreeId}, hasUrl: {HasUrl}, hasTreeId: {HasTreeId}",
                    i, name, treeId, hasUrl, hasTreeId);
            }
        }

        try
        {
            var request = ApiRequest.CreatePost(url, jsonContent, storeConfig);
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            
            // 🔍 DEBUG: Log the complete API response structure
            if (response != null)
            {
                var responseJson = JsonSerializer.Serialize(response);
                _logger.LogInformation("🔍 [API-RESPONSE-DEBUG] Complete BigCommerce API response: {ResponseJson}", responseJson);
                _logger.LogInformation("🔍 [API-RESPONSE-DEBUG] Response keys: {Keys}", string.Join(", ", response.Keys));
            }
            else
            {
                _logger.LogWarning("🔍 [API-RESPONSE-DEBUG] Response is null!");
            }
            
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var createdCategories = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(dataElement.GetRawText()) ?? new List<Dictionary<string, object>>();
                _logger.LogInformation("🔍 [API-RESPONSE-DEBUG] Successfully parsed {Count} categories from 'data' field", createdCategories.Count);
                return createdCategories;
            }

            _logger.LogWarning("🔍 [API-RESPONSE-DEBUG] No 'data' field found in response - returning empty list!");
            return new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create categories for store {StoreId}", 
                storeConfig.StoreId);
            throw;
        }
    }

    /// <summary>
    /// Gets products for a specific store and channel with pagination
    /// </summary>
    public async Task<List<Dictionary<string, object>>> GetProductsAsync(StoreConfiguration storeConfig, int page = 1, int limit = 50, string? include = null, CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/products?page={page}&limit={limit}";
        
        // Add include parameter if provided
        if (!string.IsNullOrEmpty(include))
        {
            url += $"&include={include}";
        }

        try
        {
            var request = ApiRequest.CreateGet(url, storeConfig);
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            
            // 🔍 STAGE 1 DEBUG: Log raw API response to trace options data
            if (include?.Contains("options") == true)
            {
                _logger.LogInformation("🔍 [STAGE-1-API] Raw API Response for products with options: {RawResponse}", 
                    JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
            }
            
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var rawDataText = dataElement.GetRawText();
                
                // 🔍 STAGE 1 DEBUG: Log raw data text before deserialization
                if (include?.Contains("options") == true)
                {
                    _logger.LogInformation("🔍 [STAGE-1-API] Raw data text: {RawDataText}", rawDataText);
                }
                
                var products = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(rawDataText) ?? new List<Dictionary<string, object>>();
                
                // 🔍 STAGE 1 DEBUG: Log deserialized products to see if options include IDs
                if (include?.Contains("options") == true && products.Any())
                {
                    var firstProduct = products.First();
                    if (firstProduct.TryGetValue("options", out var optionsValue))
                    {
                        _logger.LogInformation("🔍 [STAGE-1-API] First product options after deserialization: {OptionsData}", 
                            JsonSerializer.Serialize(optionsValue, new JsonSerializerOptions { WriteIndented = true }));
                    }
                }
                
                return products;
            }

            return new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get products for store {StoreId}", 
                storeConfig.StoreId);
            throw;
        }
    }

    // NOTE: CreateProductsAsync batch method removed - replaced with individual processing
    // in ProductCreationStrategy.CreateSingleProductAsync for better error isolation

    /// <summary>
    /// Checks if the API client can communicate with BigCommerce for a specific store
    /// </summary>
    public async Task<bool> IsHealthyAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        try
        {
            // Use v2 API for store information endpoint
            var url = $"{storeConfig.GetApiBaseUrl("v2")}/store";
            var request = ApiRequest.CreateGet(url, storeConfig);
            await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for store {StoreId}", storeConfig.StoreId);
            return false;
        }
    }

    /// <summary>
    /// Gets product variants for a specific product
    /// </summary>
    public async Task<List<ProductVariantSummary>> GetProductVariantsAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken)
    {
        ValidateStoreConfiguration(storeConfig);

        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/products/{productId}/variants";

        try
        {
            var request = ApiRequest.CreateGet(url, storeConfig);
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var variants = new List<ProductVariantSummary>();
                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var variantElement in dataElement.EnumerateArray())
                    {
                        if (variantElement.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
                        {
                            var variant = new ProductVariantSummary
                            {
                                Id = id,
                                ProductId = productId
                            };
                            variants.Add(variant);
                        }
                    }
                }
                
                return variants;
            }

            return new List<ProductVariantSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get product variants for store {StoreId}, product {ProductId}", storeConfig.StoreId, productId);
            throw;
        }
    }

    /// <summary>
    /// Gets product images for a specific product
    /// </summary>
    public async Task<List<ProductImageSummary>> GetProductImagesAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken)
    {
        ValidateStoreConfiguration(storeConfig);

        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/products/{productId}/images";

        try
        {
            var request = ApiRequest.CreateGet(url, storeConfig);
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var images = new List<ProductImageSummary>();
                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var imageElement in dataElement.EnumerateArray())
                    {
                        if (imageElement.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
                        {
                            var image = new ProductImageSummary
                            {
                                Id = id,
                                ProductId = productId
                            };
                            images.Add(image);
                        }
                    }
                }
                
                return images;
            }

            return new List<ProductImageSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get product images for store {StoreId}, product {ProductId}", storeConfig.StoreId, productId);
            throw;
        }
    }

    /// <summary>
    /// Gets product modifiers for a specific product
    /// </summary>
    public async Task<List<ProductModifierSummary>> GetProductModifiersAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken)
    {
        ValidateStoreConfiguration(storeConfig);

        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/products/{productId}/modifiers";

        try
        {
            var request = ApiRequest.CreateGet(url, storeConfig);
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var modifiers = new List<ProductModifierSummary>();
                if (dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var modifierElement in dataElement.EnumerateArray())
                    {
                        if (modifierElement.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
                        {
                            var modifier = new ProductModifierSummary
                            {
                                Id = id,
                                ProductId = productId
                            };
                            modifiers.Add(modifier);
                        }
                    }
                }
                
                return modifiers;
            }

            return new List<ProductModifierSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get product modifiers for store {StoreId}, product {ProductId}", storeConfig.StoreId, productId);
            throw;
        }
    }

    /// <summary>
    /// Detects the BigCommerce API version for a store
    /// </summary>
    public async Task<BigCommerceApiVersion> DetectApiVersionAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken)
    {
        ValidateStoreConfiguration(storeConfig);
        
        try
        {
            // For this implementation, we'll assume V3 API since we're using modern endpoints
            // In a real implementation, you would test endpoints to determine version
            await Task.CompletedTask; // Prevent async warning
            return BigCommerceApiVersion.V3;
        }
        catch
        {
            // Default to V3 for newer stores
            return BigCommerceApiVersion.V3;
        }
    }
    
    /// <summary>
    /// Gets a paginated response for any entity type with automatic API version handling
    /// </summary>
    public async Task<BigCommercePaginatedResponse<Dictionary<string, object>>> GetPaginatedEntitiesAsync(
        StoreConfiguration storeConfig, 
        string entityType, 
        BigCommercePaginationRequest paginationRequest, 
        CancellationToken cancellationToken)
    {
        ValidateStoreConfiguration(storeConfig);
        
        var apiVersion = await DetectApiVersionAsync(storeConfig, cancellationToken);
        
        try
        {
            var url = BuildEntityUrl(storeConfig, entityType, paginationRequest);
            
            // ✅ DEBUG: Log the exact URL being called during discovery
            _logger.LogInformation("🔍 DEBUG: GetPaginatedEntitiesAsync calling URL: {Url} for {EntityType} with CategoryTreeId: {CategoryTreeId}", 
                url, entityType, paginationRequest.CategoryTreeId ?? "NULL");
            
            var request = ApiRequest.CreateGet(url, storeConfig);
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
    
            return ParsePaginatedResponse(response, apiVersion, paginationRequest, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get paginated entities for store {StoreId}, entity type {EntityType}", 
                storeConfig.StoreId, entityType);
            throw;
        }
    }
    
    /// <summary>
    /// Gets a specific page of products with pagination metadata
    /// </summary>
    public async Task<BigCommercePaginatedResponse<ProductSummary>> GetProductPageAsync(
        StoreConfiguration storeConfig,
        BigCommercePaginationRequest paginationRequest,
        CancellationToken cancellationToken)
    {
        var response = await GetPaginatedEntitiesAsync(storeConfig, "products", paginationRequest, cancellationToken);
        
        // Convert to ProductSummary objects
        var products = response.Data.Select(ConvertToProductSummary).ToList();
        
        return new BigCommercePaginatedResponse<ProductSummary>
        {
            Data = products,
            ApiVersion = response.ApiVersion,
            CurrentPage = response.CurrentPage,
            PerPage = response.PerPage,
            TotalItems = response.TotalItems,
            TotalPages = response.TotalPages,
            HasNextPage = response.HasNextPage,
            IsLastPage = response.IsLastPage,
            Meta = response.Meta,
            Request = response.Request,
            ResponseTimestamp = response.ResponseTimestamp,
            ResponseTimeMs = response.ResponseTimeMs
        };
    }
    
    /// <summary>
    /// Gets a specific page of categories with pagination metadata
    /// </summary>
    public async Task<BigCommercePaginatedResponse<CategorySummary>> GetCategoryPageAsync(
        StoreConfiguration storeConfig,
        BigCommercePaginationRequest paginationRequest,
        CancellationToken cancellationToken)
    {
        var response = await GetPaginatedEntitiesAsync(storeConfig, "categories", paginationRequest, cancellationToken);
        
        // Convert to CategorySummary objects
        var categories = response.Data.Select(ConvertToCategorySummary).ToList();
        
        return new BigCommercePaginatedResponse<CategorySummary>
        {
            Data = categories,
            ApiVersion = response.ApiVersion,
            CurrentPage = response.CurrentPage,
            PerPage = response.PerPage,
            TotalItems = response.TotalItems,
            TotalPages = response.TotalPages,
            HasNextPage = response.HasNextPage,
            IsLastPage = response.IsLastPage,
            Meta = response.Meta,
            Request = response.Request,
            ResponseTimestamp = response.ResponseTimestamp,
            ResponseTimeMs = response.ResponseTimeMs
        };
    }
    
    /// <summary>
    /// Gets a specific page of brands with pagination metadata
    /// </summary>
    public async Task<BigCommercePaginatedResponse<BrandSummary>> GetBrandPageAsync(
        StoreConfiguration storeConfig,
        BigCommercePaginationRequest paginationRequest,
        CancellationToken cancellationToken)
    {
        var response = await GetPaginatedEntitiesAsync(storeConfig, "brands", paginationRequest, cancellationToken);
        
        // Convert to BrandSummary objects
        var brands = response.Data.Select(ConvertToBrandSummary).ToList();
        
        return new BigCommercePaginatedResponse<BrandSummary>
        {
            Data = brands,
            ApiVersion = response.ApiVersion,
            CurrentPage = response.CurrentPage,
            PerPage = response.PerPage,
            TotalItems = response.TotalItems,
            TotalPages = response.TotalPages,
            HasNextPage = response.HasNextPage,
            IsLastPage = response.IsLastPage,
            Meta = response.Meta,
            Request = response.Request,
            ResponseTimestamp = response.ResponseTimestamp,
            ResponseTimeMs = response.ResponseTimeMs
        };
    }
    
    /// <summary>
    /// Gets all entities of a specific type using pagination (streaming approach)
    /// </summary>
    public async IAsyncEnumerable<BigCommercePaginatedResponse<Dictionary<string, object>>> GetAllEntitiesPaginatedAsync(
        StoreConfiguration storeConfig,
        string entityType,
        BigCommercePaginationRequest paginationRequest,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var currentPage = paginationRequest.Page;
        var maxPages = paginationRequest.MaxPages ?? int.MaxValue;
        
        while (currentPage <= maxPages)
        {
            var pageRequest = new BigCommercePaginationRequest
            {
                Page = currentPage,
                Limit = paginationRequest.Limit,
                IncludeDeleted = paginationRequest.IncludeDeleted,
                IncludeDrafts = paginationRequest.IncludeDrafts,
                SortBy = paginationRequest.SortBy,
                SortDirection = paginationRequest.SortDirection,
                ChannelId = paginationRequest.ChannelId,
                CategoryTreeId = paginationRequest.CategoryTreeId,
                AdditionalParams = paginationRequest.AdditionalParams
            };
            
            var response = await GetPaginatedEntitiesAsync(storeConfig, entityType, pageRequest, cancellationToken);
            
            yield return response;
            
            // Stop if this is the last page or no more data
            if (response.IsLastPage || !response.HasNextPage || response.Data.Count == 0)
                break;
                
            currentPage++;
        }
    }
    
    /// <summary>
    /// Estimates the total count of entities for V2 API stores
    /// </summary>
    public async Task<int> EstimateEntityCountAsync(
        StoreConfiguration storeConfig,
        string entityType,
        BigCommercePaginationRequest paginationRequest,
        CancellationToken cancellationToken)
    {
        var apiVersion = await DetectApiVersionAsync(storeConfig, cancellationToken);
        
        if (apiVersion == BigCommerceApiVersion.V3)
        {
            // For V3, get first page to get accurate total
            var firstPage = await GetPaginatedEntitiesAsync(storeConfig, entityType, paginationRequest, cancellationToken);
            return firstPage.TotalItems ?? 0;
        }
        
        // For V2, estimate by sampling pages
        var v2FirstPage = await GetPaginatedEntitiesAsync(storeConfig, entityType, paginationRequest, cancellationToken);
        if (v2FirstPage.Data.Count < paginationRequest.Limit)
        {
            // Found the last page on first try
            return v2FirstPage.Data.Count;
        }
        
        // Conservative estimate
        return v2FirstPage.Data.Count * 100; // Rough estimate
    }

    private static void ValidateStoreConfiguration(StoreConfiguration storeConfig)
    {
        if (storeConfig == null)
            throw new ArgumentNullException(nameof(storeConfig));

        if (!storeConfig.IsValid())
            throw new ArgumentException("Invalid store configuration", nameof(storeConfig));
    }

    // Helper methods for pagination
    
    /// <summary>
    /// Builds entity URL based on entity type and pagination request
    /// </summary>
    private string BuildEntityUrl(StoreConfiguration storeConfig, string entityType, BigCommercePaginationRequest request)
    {
        _logger.LogInformation("🔗 [BUILD-URL-DEBUG] Building URL for EntityType='{EntityType}', Include='{Include}', Page={Page}, Limit={Limit}", 
            entityType, request.Include, request.Page, request.Limit);
            
        var baseUrl = storeConfig.GetApiBaseUrl();
        var endpoint = entityType.ToLowerInvariant() switch
        {
            "products" => "catalog/products",
            "categories" => "catalog/trees/categories", 
            "brands" => "catalog/brands",
            "variants" => "catalog/variants",              // Phase 3: Map to variants endpoint
            
            // 🚀 ENHANCED PRODUCT MIGRATION: Phase-specific entity type mappings
            "product-components" => "catalog/products",    // Phase 2: Fetch products with includes
            "product-related" => "catalog/products",       // Phase 4: Fetch products for relationship updates
            "product-metafields" => "catalog/products/metafields",    // Phase 7: Fetch product metafields using dedicated API  
            "product-channels" => "catalog/products",      // Phase 6: Fetch products for channel assignments
            
            _ => throw new ArgumentException($"Unsupported entity type: {entityType}")
        };
        
        _logger.LogInformation("🔗 [BUILD-URL-DEBUG] EntityType '{EntityType}' mapped to endpoint '{Endpoint}'", entityType, endpoint);
        
        var queryParams = new List<string>
        {
            $"page={request.Page}",
            $"limit={request.Limit}"
        };
        
        // Add entity-specific parameters based on BigCommerce API documentation
        switch (entityType.ToLowerInvariant())
        {
            case "products":
                // ✅ ENHANCED PRODUCTS: Add include parameter for additional product data WITHOUT channel_id filtering
                if (!string.IsNullOrEmpty(request.Include))
                {
                    queryParams.Add($"include={request.Include}");
                }
                break;
                
            case "product-components":    // 🔧 FIX: Phase 2 - NO channel_id to get all products
            case "product-related":       // 🔧 FIX: Phase 4 - NO channel_id for relationship updates  
            case "product-metafields":    // 🔧 FIX: Phase 5 - NO channel_id for metafield updates
            case "product-channels":      // 🔧 FIX: Phase 6 - NO channel_id for channel assignment updates
                // ✅ ENHANCED PRODUCTS: Add include parameter WITHOUT channel_id filtering
                if (!string.IsNullOrEmpty(request.Include))
                {
                    queryParams.Add($"include={request.Include}");
                    _logger.LogInformation("🔗 [BUILD-URL-DEBUG] Added include parameter for {EntityType}: include={Include}", entityType, request.Include);
                }
                else
                {
                    _logger.LogWarning("🔗 [BUILD-URL-DEBUG] ⚠️ No include parameter for {EntityType} - this may result in 0 components!", entityType);
                }
                break;
                
            case "categories":
                // Categories API uses tree_id:in= parameter for category tree filtering
                if (!string.IsNullOrEmpty(request.CategoryTreeId))
                {
                    queryParams.Add($"tree_id:in={request.CategoryTreeId}");
                }
                break;
                
            case "brands":
                // Brands API doesn't support channel_id or tree_id parameters
                break;
                
            case "variants":              // ✅ FIXED: Phase 3 parameter handling
                // Variants API doesn't require additional parameters
                break;
        }
        
        // Add any additional parameters from the request
        if (request.AdditionalParams != null && request.AdditionalParams.Any())
        {
            foreach (var param in request.AdditionalParams)
            {
                queryParams.Add($"{param.Key}={param.Value}");
            }
        }
        
        var finalUrl = $"{baseUrl}/{endpoint}?{string.Join("&", queryParams)}";
        
        _logger.LogInformation("🔗 [BUILD-URL-DEBUG] ✅ Final URL constructed: {FinalUrl}", finalUrl);
        
        return finalUrl;
    }
    
    /// <summary>
    /// Parses paginated response based on API version
    /// </summary>
    private BigCommercePaginatedResponse<Dictionary<string, object>> ParsePaginatedResponse(
        Dictionary<string, object>? result, 
        BigCommerceApiVersion apiVersion, 
        BigCommercePaginationRequest request,
        long elapsedMilliseconds)
    {
        _logger.LogInformation("📊 [PARSE-RESPONSE-DEBUG] Parsing API response - HasResult: {HasResult}, ApiVersion: {ApiVersion}", 
            result != null, apiVersion);
            
        var response = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            ApiVersion = apiVersion,
            Request = request,
            ResponseTimestamp = DateTime.UtcNow,
            ResponseTimeMs = (int)elapsedMilliseconds,
            Data = new List<Dictionary<string, object>>()
        };
        
        if (result?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
        {
            response.Data = ParseDataArray(dataElement);
            _logger.LogInformation("📊 [PARSE-RESPONSE-DEBUG] Parsed data array - Count: {DataCount}", response.Data.Count);
        }
        else
        {
            _logger.LogWarning("📊 [PARSE-RESPONSE-DEBUG] ⚠️ No 'data' field found in response or data is not JsonElement!");
        }
        
        if (result?.TryGetValue("meta", out var metaValue) == true && metaValue is JsonElement metaElement)
        {
            var meta = ParseV3Meta(metaElement);
            response.Meta = meta;
            response.CurrentPage = meta.Pagination.CurrentPage;
            response.PerPage = meta.Pagination.PerPage;
            response.TotalItems = meta.Pagination.Total;
            response.TotalPages = meta.Pagination.TotalPages;
            response.HasNextPage = meta.Pagination.CurrentPage < meta.Pagination.TotalPages;
            response.IsLastPage = meta.Pagination.CurrentPage >= meta.Pagination.TotalPages;
            
            _logger.LogInformation("📊 [PARSE-RESPONSE-DEBUG] Parsed pagination metadata - Total: {Total}, TotalPages: {TotalPages}, CurrentPage: {CurrentPage}, PerPage: {PerPage}", 
                meta.Pagination.Total, meta.Pagination.TotalPages, meta.Pagination.CurrentPage, meta.Pagination.PerPage);
        }
        else
        {
            // No meta information (V2 or simplified response)
            response.CurrentPage = request.Page;
            response.PerPage = request.Limit;
            response.HasNextPage = response.Data.Count >= request.Limit;
            response.IsLastPage = response.Data.Count < request.Limit;
        }
        
        return response;
    }
    
    /// <summary>
    /// Parses data array from JSON response
    /// </summary>
    private List<Dictionary<string, object>> ParseDataArray(JsonElement dataElement)
    {
        var data = new List<Dictionary<string, object>>();
        
        if (dataElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataElement.EnumerateArray())
            {
                data.Add(ParseJsonElement(item));
            }
        }
        
        return data;
    }
    
    /// <summary>
    /// Parses V3 meta information
    /// </summary>
    private BigCommerceV3Meta ParseV3Meta(JsonElement metaElement)
    {
        var meta = new BigCommerceV3Meta();
        
        if (metaElement.TryGetProperty("pagination", out var paginationElement))
        {
            var pagination = new BigCommerceV3Pagination();
            
            if (paginationElement.TryGetProperty("total", out var totalElement))
                pagination.Total = totalElement.GetInt32();
                
            if (paginationElement.TryGetProperty("count", out var countElement))
                pagination.Count = countElement.GetInt32();
                
            if (paginationElement.TryGetProperty("per_page", out var perPageElement))
                pagination.PerPage = perPageElement.GetInt32();
                
            if (paginationElement.TryGetProperty("current_page", out var currentPageElement))
                pagination.CurrentPage = currentPageElement.GetInt32();
                
            if (paginationElement.TryGetProperty("total_pages", out var totalPagesElement))
                pagination.TotalPages = totalPagesElement.GetInt32();
                
            meta.Pagination = pagination;
        }
        
        return meta;
    }
    
    /// <summary>
    /// Parses a JSON element into a dictionary
    /// </summary>
    private Dictionary<string, object> ParseJsonElement(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.TryGetInt32(out var intValue) ? intValue : 
                                      property.Value.TryGetDouble(out var doubleValue) ? doubleValue : 
                                      property.Value.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => (object?)null!,
                _ => property.Value.GetRawText()
            };
        }
        
        return dict;
    }
    
    /// <summary>
    /// Converts dictionary to ProductSummary
    /// </summary>
    private ProductSummary ConvertToProductSummary(Dictionary<string, object> data)
    {
        return new ProductSummary
        {
            Id = data.TryGetValue("id", out var id) ? JsonElementHelper.GetIntegerValue(id) : 0,
            Name = data.TryGetValue("name", out var name) ? name.ToString() ?? string.Empty : string.Empty,
            Sku = data.TryGetValue("sku", out var sku) ? sku.ToString() ?? string.Empty : string.Empty,
            Status = data.TryGetValue("status", out var status) ? status.ToString() ?? string.Empty : string.Empty,
            IsDeleted = data.TryGetValue("is_deleted", out var deleted) && JsonElementHelper.GetBooleanValue(deleted)
        };
    }
    
    /// <summary>
    /// Converts dictionary to CategorySummary
    /// </summary>
    private CategorySummary ConvertToCategorySummary(Dictionary<string, object> data)
    {
        return new CategorySummary
        {
            Id = data.TryGetValue("id", out var id) ? JsonElementHelper.GetIntegerValue(id) : 0,
            Name = data.TryGetValue("name", out var name) ? name.ToString() ?? string.Empty : string.Empty,
            ParentId = data.TryGetValue("parent_id", out var parentId) ? JsonElementHelper.GetIntegerValue(parentId) : 0
        };
    }
    
    /// <summary>
    /// Converts dictionary to BrandSummary
    /// </summary>
    private BrandSummary ConvertToBrandSummary(Dictionary<string, object> data)
    {
        return new BrandSummary
        {
            Id = data.TryGetValue("id", out var id) ? JsonElementHelper.GetIntegerValue(id) : 0,
            Name = data.TryGetValue("name", out var name) ? name.ToString() ?? string.Empty : string.Empty
        };
    }


} 
