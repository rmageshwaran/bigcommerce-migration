using System.Net;
using System.Text;
using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// BigCommerce API client implementation with request-based store credentials
/// Supports multi-tenant architecture where credentials come from migration requests
/// </summary>
public class BigCommerceApiClient : IBigCommerceApiClient
{
    private readonly BigCommerceConfiguration _globalConfig;
    private readonly HttpClient _httpClient;
    private readonly IOpenSearchService _openSearchService;
    private readonly ILogger<BigCommerceApiClient> _logger;
    private readonly SemaphoreSlim _rateLimitSemaphore;

    /// <summary>
    /// Initializes a new instance of the BigCommerceApiClient
    /// </summary>
    /// <param name="globalConfig">BigCommerce global configuration for API access</param>
    /// <param name="httpClient">HTTP client for making requests</param>
    /// <param name="openSearchService">OpenSearch service for logging and metrics</param>
    /// <param name="logger">Logger instance for service operations</param>
    public BigCommerceApiClient(
        BigCommerceConfiguration globalConfig,
        HttpClient httpClient,
        IOpenSearchService openSearchService,
        ILogger<BigCommerceApiClient> logger)
    {
        _globalConfig = globalConfig ?? throw new ArgumentNullException(nameof(globalConfig));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!_globalConfig.IsValid())
        {
            throw new ArgumentException("Invalid BigCommerce global configuration", nameof(globalConfig));
        }

        // Initialize rate limiting semaphore
        _rateLimitSemaphore = new SemaphoreSlim(_globalConfig.RateLimitRequestsPerSecond, _globalConfig.RateLimitRequestsPerSecond);
    }

    /// <summary>
    /// Gets category trees for a specific store and channel using correct BigCommerce API syntax
    /// </summary>
    public async Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        // Use correct BigCommerce API syntax: channel_id:in for filtering category trees by channel
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/trees?channel_id:in={storeConfig.ChannelId}";

        try
        {
            var response = await MakeApiRequestAsync(storeConfig, url, HttpMethod.Get, cancellationToken);
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            
            if (result?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var trees = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(dataElement.GetRawText()) ?? new List<Dictionary<string, object>>();
                
                _logger.LogDebug("Retrieved {TreeCount} category trees for store {StoreId}, channel {ChannelId}", 
                    trees.Count, storeConfig.StoreId, storeConfig.ChannelId);
                
                await LogPerformanceMetrics("GetCategoryTrees", stopwatch.Elapsed, storeConfig.StoreId!, storeConfig.ChannelId!);
                return trees;
            }

            return new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get category trees for store {StoreId}, channel {ChannelId}", 
                storeConfig.StoreId, storeConfig.ChannelId);
            throw;
        }
    }

    /// <summary>
    /// Gets categories for a specific category tree
    /// </summary>
    public async Task<List<Dictionary<string, object>>> GetCategoriesAsync(StoreConfiguration storeConfig, string categoryTreeId, CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/trees/{categoryTreeId}/categories";

        try
        {
            var response = await MakeApiRequestAsync(storeConfig, url, HttpMethod.Get, cancellationToken);
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            
            if (result?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var categories = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(dataElement.GetRawText()) ?? new List<Dictionary<string, object>>();
                
                await LogPerformanceMetrics("GetCategories", stopwatch.Elapsed, storeConfig.StoreId!, storeConfig.ChannelId!);
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
    /// Creates categories in a specific category tree
    /// </summary>
    public async Task<List<Dictionary<string, object>>> CreateCategoriesAsync(StoreConfiguration storeConfig, string categoryTreeId, List<Dictionary<string, object>> categories, CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/trees/{categoryTreeId}/categories";
        var jsonContent = JsonSerializer.Serialize(categories);

        try
        {
            var response = await MakeApiRequestAsync(storeConfig, url, HttpMethod.Post, cancellationToken, jsonContent);
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            
            if (result?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var createdCategories = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(dataElement.GetRawText()) ?? new List<Dictionary<string, object>>();
                
                await LogPerformanceMetrics("CreateCategories", stopwatch.Elapsed, storeConfig.StoreId!, storeConfig.ChannelId!);
                return createdCategories;
            }

            return new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create categories for store {StoreId}, tree {TreeId}", 
                storeConfig.StoreId, categoryTreeId);
            throw;
        }
    }

    /// <summary>
    /// Gets products for a specific store and channel with pagination
    /// </summary>
    public async Task<List<Dictionary<string, object>>> GetProductsAsync(StoreConfiguration storeConfig, int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/products?page={page}&limit={limit}&channel_id={storeConfig.ChannelId}";

        try
        {
            var response = await MakeApiRequestAsync(storeConfig, url, HttpMethod.Get, cancellationToken);
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            
            if (result?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var products = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(dataElement.GetRawText()) ?? new List<Dictionary<string, object>>();
                
                await LogPerformanceMetrics("GetProducts", stopwatch.Elapsed, storeConfig.StoreId!, storeConfig.ChannelId!);
                return products;
            }

            return new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get products for store {StoreId}, channel {ChannelId}", 
                storeConfig.StoreId, storeConfig.ChannelId);
            throw;
        }
    }

    /// <summary>
    /// Creates products in a specific store and channel
    /// </summary>
    public async Task<List<Dictionary<string, object>>> CreateProductsAsync(StoreConfiguration storeConfig, List<Dictionary<string, object>> products, CancellationToken cancellationToken = default)
    {
        ValidateStoreConfiguration(storeConfig);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/products?channel_id={storeConfig.ChannelId}";
        var jsonContent = JsonSerializer.Serialize(products);

        try
        {
            var response = await MakeApiRequestAsync(storeConfig, url, HttpMethod.Post, cancellationToken, jsonContent);
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            
            if (result?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                var createdProducts = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(dataElement.GetRawText()) ?? new List<Dictionary<string, object>>();
                
                await LogPerformanceMetrics("CreateProducts", stopwatch.Elapsed, storeConfig.StoreId!, storeConfig.ChannelId!);
                return createdProducts;
            }

            return new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create products for store {StoreId}, channel {ChannelId}", 
                storeConfig.StoreId, storeConfig.ChannelId);
            throw;
        }
    }

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
            await MakeApiRequestAsync(storeConfig, url, HttpMethod.Get, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for store {StoreId}", storeConfig.StoreId);
            return false;
        }
    }

    // Duplicate IsHealthyAsync method removed

    private static void ValidateStoreConfiguration(StoreConfiguration storeConfig)
    {
        if (storeConfig == null)
            throw new ArgumentNullException(nameof(storeConfig));

        if (!storeConfig.IsValid())
            throw new ArgumentException("Invalid store configuration", nameof(storeConfig));
    }

    private async Task<string> MakeApiRequestAsync(StoreConfiguration storeConfig, string url, HttpMethod method, CancellationToken cancellationToken, string? content = null)
    {
        // Apply rate limiting
        await _rateLimitSemaphore.WaitAsync(cancellationToken);

        try
        {
            using var request = new HttpRequestMessage(method, url);
            
            // Add authentication headers from store configuration
            var authHeaders = storeConfig.GetAuthHeaders();
            foreach (var header in authHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Add global default headers
            var defaultHeaders = _globalConfig.GetDefaultHeaders();
            foreach (var header in defaultHeaders)
            {
                if (!request.Headers.Contains(header.Key))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (!string.IsNullOrEmpty(content))
            {
                request.Content = new StringContent(content, Encoding.UTF8, "application/json");
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("API request failed: {StatusCode}, Content: {Content}", response.StatusCode, errorContent);

            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new UnauthorizedAccessException($"Authentication failed for store {storeConfig.StoreId}"),
                HttpStatusCode.Forbidden => new UnauthorizedAccessException($"Access forbidden for store {storeConfig.StoreId}"),
                HttpStatusCode.TooManyRequests => new HttpRequestException($"Rate limit exceeded for store {storeConfig.StoreId}"),
                _ => new HttpRequestException($"API request failed with status {response.StatusCode}: {errorContent}")
            };
        }
        finally
        {
            _rateLimitSemaphore.Release();
        }
    }

    private async Task LogPerformanceMetrics(string operation, TimeSpan duration, string storeId, string channelId)
    {
        try
        {
            var metrics = new Dictionary<string, object>
            {
                ["operation"] = operation,
                ["duration_ms"] = duration.TotalMilliseconds,
                ["store_id"] = storeId,
                ["channel_id"] = channelId,
                ["timestamp"] = DateTime.UtcNow
            };

            await _openSearchService.LogPerformanceMetricsAsync("bigcommerce_api", duration, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log performance metrics for operation {Operation}", operation);
        }
    }

    // Additional methods for activity functions

    // Legacy GetCategoriesAsync and GetBrandsAsync methods removed - use GetCategoryPageAsync and GetBrandPageAsync instead
    // Legacy GetProductsAsync with ProductQueryOptions removed - use GetProductPageAsync instead

    /// <summary>
    /// Gets product variants for a specific product
    /// </summary>
    public async Task<List<ProductVariantSummary>> GetProductVariantsAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken)
    {
        ValidateStoreConfiguration(storeConfig);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/products/{productId}/variants";

        try
        {
            var response = await MakeApiRequestAsync(storeConfig, url, HttpMethod.Get, cancellationToken);
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            
            if (result?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
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
                
                await LogPerformanceMetrics("GetProductVariants", stopwatch.Elapsed, storeConfig.StoreId!, storeConfig.ChannelId!);
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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/products/{productId}/images";

        try
        {
            var response = await MakeApiRequestAsync(storeConfig, url, HttpMethod.Get, cancellationToken);
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            
            if (result?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
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
                
                await LogPerformanceMetrics("GetProductImages", stopwatch.Elapsed, storeConfig.StoreId!, storeConfig.ChannelId!);
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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var url = $"{storeConfig.GetApiBaseUrl()}/catalog/products/{productId}/modifiers";

        try
        {
            var response = await MakeApiRequestAsync(storeConfig, url, HttpMethod.Get, cancellationToken);
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            
            if (result?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
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
                
                await LogPerformanceMetrics("GetProductModifiers", stopwatch.Elapsed, storeConfig.StoreId!, storeConfig.ChannelId!);
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

    // Pagination methods implementation
    
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
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var apiVersion = await DetectApiVersionAsync(storeConfig, cancellationToken);
        
        try
        {
            var url = BuildEntityUrl(storeConfig, entityType, paginationRequest);
            var response = await MakeApiRequestAsync(storeConfig, url, HttpMethod.Get, cancellationToken);
            var result = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            
            return ParsePaginatedResponse(result, apiVersion, paginationRequest, stopwatch.ElapsedMilliseconds);
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

    // Helper methods for pagination
    
    /// <summary>
    /// Builds entity URL based on entity type and pagination request
    /// </summary>
    private string BuildEntityUrl(StoreConfiguration storeConfig, string entityType, BigCommercePaginationRequest request)
    {
        var baseUrl = storeConfig.GetApiBaseUrl();
        var endpoint = entityType.ToLowerInvariant() switch
        {
            "products" => "catalog/products",
            "categories" => "catalog/categories",
            "brands" => "catalog/brands",
            _ => throw new ArgumentException($"Unsupported entity type: {entityType}")
        };
        
        var queryParams = new List<string>
        {
            $"page={request.Page}",
            $"limit={request.Limit}"
        };
        
        if (storeConfig.ChannelId != null)
            queryParams.Add($"channel_id={storeConfig.ChannelId}");
        
        return $"{baseUrl}/{endpoint}?{string.Join("&", queryParams)}";
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
                JsonValueKind.Number => property.Value.GetInt32(),
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
            Id = data.TryGetValue("id", out var id) ? Convert.ToInt32(id) : 0,
            Name = data.TryGetValue("name", out var name) ? name.ToString() ?? string.Empty : string.Empty,
            Sku = data.TryGetValue("sku", out var sku) ? sku.ToString() ?? string.Empty : string.Empty,
            Status = data.TryGetValue("status", out var status) ? status.ToString() ?? string.Empty : string.Empty,
            IsDeleted = data.TryGetValue("is_deleted", out var deleted) && Convert.ToBoolean(deleted)
        };
    }
    
    /// <summary>
    /// Converts dictionary to CategorySummary
    /// </summary>
    private CategorySummary ConvertToCategorySummary(Dictionary<string, object> data)
    {
        return new CategorySummary
        {
            Id = data.TryGetValue("id", out var id) ? Convert.ToInt32(id) : 0,
            Name = data.TryGetValue("name", out var name) ? name.ToString() ?? string.Empty : string.Empty,
            ParentId = data.TryGetValue("parent_id", out var parentId) ? Convert.ToInt32(parentId) : 0
        };
    }
    
    /// <summary>
    /// Converts dictionary to BrandSummary
    /// </summary>
    private BrandSummary ConvertToBrandSummary(Dictionary<string, object> data)
    {
        return new BrandSummary
        {
            Id = data.TryGetValue("id", out var id) ? Convert.ToInt32(id) : 0,
            Name = data.TryGetValue("name", out var name) ? name.ToString() ?? string.Empty : string.Empty
        };
    }
} 