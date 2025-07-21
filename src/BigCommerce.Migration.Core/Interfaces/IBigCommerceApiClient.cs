using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for BigCommerce API client operations
/// Now supports request-based store credentials instead of hardcoded configuration
/// Follows Interface Segregation Principle by inheriting from focused interfaces
/// </summary>
public interface IBigCommerceApiClient : ICategoryApiClient, IProductApiClient, IPaginationApiClient, IApiHealthClient
{
    // Additional methods unique to the composite interface
    
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