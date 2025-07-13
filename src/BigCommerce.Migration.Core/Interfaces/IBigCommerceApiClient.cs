using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for BigCommerce API client operations
/// Now supports request-based store credentials instead of hardcoded configuration
/// </summary>
public interface IBigCommerceApiClient
{
    /// <summary>
    /// Gets category trees for a specific store and channel
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of category trees</returns>
    Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets categories from a specific category tree
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="categoryTreeId">Category tree identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of categories</returns>
    Task<List<Dictionary<string, object>>> GetCategoriesAsync(StoreConfiguration storeConfig, string categoryTreeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates categories in a specific category tree
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="categoryTreeId">Category tree identifier</param>
    /// <param name="categories">Categories to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created categories</returns>
    Task<List<Dictionary<string, object>>> CreateCategoriesAsync(StoreConfiguration storeConfig, string categoryTreeId, List<Dictionary<string, object>> categories, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets products from a specific store and channel with pagination
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="page">Page number for pagination</param>
    /// <param name="limit">Number of products per page</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of products</returns>
    Task<List<Dictionary<string, object>>> GetProductsAsync(StoreConfiguration storeConfig, int page = 1, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates products in a specific store and channel
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="products">Products to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created products</returns>
    Task<List<Dictionary<string, object>>> CreateProductsAsync(StoreConfiguration storeConfig, List<Dictionary<string, object>> products, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the BigCommerce API is healthy for a specific store
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if API is healthy, false otherwise</returns>
    Task<bool> IsHealthyAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken = default);

    // Additional methods for activity functions
    
    // Legacy GetCategoriesAsync and GetBrandsAsync methods removed - use GetCategoryPageAsync and GetBrandPageAsync instead
    // Legacy GetProductsAsync with ProductQueryOptions removed - use GetProductPageAsync instead

    /// <summary>
    /// Gets product variants for a specific product
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="productId">Product ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of product variant summaries</returns>
    Task<List<ProductVariantSummary>> GetProductVariantsAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets product images for a specific product
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="productId">Product ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of product image summaries</returns>
    Task<List<ProductImageSummary>> GetProductImagesAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets product modifiers for a specific product
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="productId">Product ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of product modifier summaries</returns>
    Task<List<ProductModifierSummary>> GetProductModifiersAsync(StoreConfiguration storeConfig, int productId, CancellationToken cancellationToken);

    // Paginated API methods
    
    /// <summary>
    /// Detects the BigCommerce API version for a store
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>API version (V2 or V3)</returns>
    Task<BigCommerceApiVersion> DetectApiVersionAsync(StoreConfiguration storeConfig, CancellationToken cancellationToken);
    
    /// <summary>
    /// Gets a paginated response for any entity type with automatic API version handling
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="entityType">Type of entity to fetch</param>
    /// <param name="paginationRequest">Pagination parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated response with data and meta information</returns>
    Task<BigCommercePaginatedResponse<Dictionary<string, object>>> GetPaginatedEntitiesAsync(
        StoreConfiguration storeConfig, 
        string entityType, 
        BigCommercePaginationRequest paginationRequest, 
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Gets a specific page of products with pagination metadata
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="paginationRequest">Pagination parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated products response</returns>
    Task<BigCommercePaginatedResponse<ProductSummary>> GetProductPageAsync(
        StoreConfiguration storeConfig,
        BigCommercePaginationRequest paginationRequest,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Gets a specific page of categories with pagination metadata
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="paginationRequest">Pagination parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated categories response</returns>
    Task<BigCommercePaginatedResponse<CategorySummary>> GetCategoryPageAsync(
        StoreConfiguration storeConfig,
        BigCommercePaginationRequest paginationRequest,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Gets a specific page of brands with pagination metadata
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="paginationRequest">Pagination parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated brands response</returns>
    Task<BigCommercePaginatedResponse<BrandSummary>> GetBrandPageAsync(
        StoreConfiguration storeConfig,
        BigCommercePaginationRequest paginationRequest,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Gets all entities of a specific type using pagination (streaming approach)
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="entityType">Type of entity to fetch</param>
    /// <param name="paginationRequest">Base pagination parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of entity pages</returns>
    IAsyncEnumerable<BigCommercePaginatedResponse<Dictionary<string, object>>> GetAllEntitiesPaginatedAsync(
        StoreConfiguration storeConfig,
        string entityType,
        BigCommercePaginationRequest paginationRequest,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Estimates the total count of entities for V2 API stores
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="entityType">Type of entity to count</param>
    /// <param name="paginationRequest">Base pagination parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Estimated total count</returns>
    Task<int> EstimateEntityCountAsync(
        StoreConfiguration storeConfig,
        string entityType,
        BigCommercePaginationRequest paginationRequest,
        CancellationToken cancellationToken);
}

/// <summary>
/// Summary models for API responses
/// </summary>
public class ProductSummary
{
    /// <summary>Product ID</summary>
    public int Id { get; set; }
    /// <summary>Product name</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Product SKU</summary>
    public string Sku { get; set; } = string.Empty;
    /// <summary>Product status</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Whether product is deleted</summary>
    public bool IsDeleted { get; set; }
}

/// <summary>Category summary information</summary>
public class CategorySummary
{
    /// <summary>Category ID</summary>
    public int Id { get; set; }
    /// <summary>Category name</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Parent category ID</summary>
    public int ParentId { get; set; }
}

/// <summary>Brand summary information</summary>
public class BrandSummary
{
    /// <summary>Brand ID</summary>
    public int Id { get; set; }
    /// <summary>Brand name</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>Product variant summary information</summary>
public class ProductVariantSummary
{
    /// <summary>Variant ID</summary>
    public int Id { get; set; }
    /// <summary>Product ID</summary>
    public int ProductId { get; set; }
}

/// <summary>Product image summary information</summary>
public class ProductImageSummary
{
    /// <summary>Image ID</summary>
    public int Id { get; set; }
    /// <summary>Product ID</summary>
    public int ProductId { get; set; }
}

/// <summary>Product modifier summary information</summary>
public class ProductModifierSummary
{
    /// <summary>Modifier ID</summary>
    public int Id { get; set; }
    /// <summary>Product ID</summary>
    public int ProductId { get; set; }
}

// Legacy CategoryQueryOptions, BrandQueryOptions, and ProductQueryOptions classes removed - use BigCommercePaginationRequest instead

/// <summary>
/// BigCommerce API exception
/// </summary>
public class BigCommerceApiException : Exception
{
    /// <summary>Initialize exception with message</summary>
    public BigCommerceApiException(string message) : base(message) { }
    /// <summary>Initialize exception with message and inner exception</summary>
    public BigCommerceApiException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Request model for entity discovery activities
/// </summary>
public class EntityDiscoveryRequest
{
    /// <summary>
    /// Migration ID for tracking
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of entity to discover
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Source store configuration
    /// </summary>
    public StoreConfiguration SourceStore { get; set; } = new();
    
    /// <summary>
    /// Entity-specific configuration
    /// </summary>
    public EntityConfiguration EntityConfig { get; set; } = new();
    
    /// <summary>
    /// Category tree context for category-related operations
    /// </summary>
    public CategoryTreeContext? CategoryTreeContext { get; set; }
} 