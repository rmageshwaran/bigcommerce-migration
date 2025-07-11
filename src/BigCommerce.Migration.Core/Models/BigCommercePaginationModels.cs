namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// BigCommerce API version enumeration
/// </summary>
public enum BigCommerceApiVersion
{
    /// <summary>V2 API - Legacy version without meta pagination details</summary>
    V2,
    /// <summary>V3 API - Current version with meta pagination details</summary>
    V3
}

/// <summary>
/// Unified pagination request model that works with both V2 and V3 APIs
/// </summary>
public class BigCommercePaginationRequest
{
    /// <summary>
    /// Page number (1-based)
    /// </summary>
    public int Page { get; set; } = 1;
    
    /// <summary>
    /// Number of items per page
    /// </summary>
    public int Limit { get; set; } = 50;
    
    /// <summary>
    /// Maximum number of pages to fetch (for V2 safety)
    /// </summary>
    public int? MaxPages { get; set; }
    
    /// <summary>
    /// Whether to include deleted entities
    /// </summary>
    public bool IncludeDeleted { get; set; } = false;
    
    /// <summary>
    /// Whether to include draft entities
    /// </summary>
    public bool IncludeDrafts { get; set; } = false;
    
    /// <summary>
    /// Sort field for consistent ordering
    /// </summary>
    public string SortBy { get; set; } = "id";
    
    /// <summary>
    /// Sort direction (asc/desc)
    /// </summary>
    public string SortDirection { get; set; } = "asc";
    
    /// <summary>
    /// Channel ID for channel-specific queries
    /// </summary>
    public int? ChannelId { get; set; }
    
    /// <summary>
    /// Category tree ID for category queries
    /// </summary>
    public string? CategoryTreeId { get; set; }
    
    /// <summary>
    /// Additional query parameters
    /// </summary>
    public Dictionary<string, string> AdditionalParams { get; set; } = new();
}

/// <summary>
/// V3 API meta pagination information
/// </summary>
public class BigCommerceV3Meta
{
    /// <summary>Pagination details from V3 API response</summary>
    public BigCommerceV3Pagination Pagination { get; set; } = new();
}

/// <summary>
/// V3 API pagination details
/// </summary>
public class BigCommerceV3Pagination
{
    /// <summary>Total number of items</summary>
    public int Total { get; set; }
    
    /// <summary>Number of items returned in current response</summary>
    public int Count { get; set; }
    
    /// <summary>Number of items per page</summary>
    public int PerPage { get; set; }
    
    /// <summary>Current page number</summary>
    public int CurrentPage { get; set; }
    
    /// <summary>Total number of pages</summary>
    public int TotalPages { get; set; }
    
    /// <summary>Links for navigation</summary>
    public BigCommerceV3Links Links { get; set; } = new();
}

/// <summary>
/// V3 API navigation links
/// </summary>
public class BigCommerceV3Links
{
    /// <summary>URL for previous page</summary>
    public string? Previous { get; set; }
    
    /// <summary>URL for current page</summary>
    public string? Current { get; set; }
    
    /// <summary>URL for next page</summary>
    public string? Next { get; set; }
}

/// <summary>
/// Unified paginated response model for both V2 and V3 APIs
/// </summary>
/// <typeparam name="T">Type of data being paginated</typeparam>
public class BigCommercePaginatedResponse<T>
{
    /// <summary>
    /// Data items for this page
    /// </summary>
    public List<T> Data { get; set; } = new();
    
    /// <summary>
    /// API version used for this response
    /// </summary>
    public BigCommerceApiVersion ApiVersion { get; set; }
    
    /// <summary>
    /// Current page number
    /// </summary>
    public int CurrentPage { get; set; }
    
    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PerPage { get; set; }
    
    /// <summary>
    /// Total number of items (V3 only, estimated for V2)
    /// </summary>
    public int? TotalItems { get; set; }
    
    /// <summary>
    /// Total number of pages (V3 only, estimated for V2)
    /// </summary>
    public int? TotalPages { get; set; }
    
    /// <summary>
    /// Whether there are more pages available
    /// </summary>
    public bool HasNextPage { get; set; }
    
    /// <summary>
    /// Whether this is the last page
    /// </summary>
    public bool IsLastPage { get; set; }
    
    /// <summary>
    /// V3 meta information (null for V2)
    /// </summary>
    public BigCommerceV3Meta? Meta { get; set; }
    
    /// <summary>
    /// Request parameters used for this page
    /// </summary>
    public BigCommercePaginationRequest Request { get; set; } = new();
    
    /// <summary>
    /// Response timestamp
    /// </summary>
    public DateTime ResponseTimestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Request duration in milliseconds
    /// </summary>
    public int ResponseTimeMs { get; set; }
}

/// <summary>
/// Pagination strategy configuration for different entity types
/// </summary>
public class BigCommercePaginationStrategy
{
    /// <summary>
    /// Entity type this strategy applies to
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Preferred page size for this entity type
    /// </summary>
    public int PreferredPageSize { get; set; } = 50;
    
    /// <summary>
    /// Maximum page size allowed for this entity type
    /// </summary>
    public int MaxPageSize { get; set; } = 250;
    
    /// <summary>
    /// Whether to use parallel page fetching
    /// </summary>
    public bool AllowParallelFetching { get; set; } = false;
    
    /// <summary>
    /// Maximum number of parallel requests
    /// </summary>
    public int MaxParallelRequests { get; set; } = 3;
    
    /// <summary>
    /// Delay between page requests (milliseconds)
    /// </summary>
    public int DelayBetweenPages { get; set; } = 100;
    
    /// <summary>
    /// Maximum number of pages to fetch (V2 safety limit)
    /// </summary>
    public int? MaxPagesLimit { get; set; } = 1000;
    
    /// <summary>
    /// Whether to estimate totals for V2 API
    /// </summary>
    public bool EstimateTotalsForV2 { get; set; } = true;
}

/// <summary>
/// Configuration for BigCommerce API pagination behavior
/// </summary>
public class BigCommercePaginationConfig
{
    /// <summary>
    /// Default pagination strategies by entity type
    /// </summary>
    public Dictionary<string, BigCommercePaginationStrategy> Strategies { get; set; } = new()
    {
        ["products"] = new BigCommercePaginationStrategy
        {
            EntityType = "products",
            PreferredPageSize = 50,
            MaxPageSize = 250,
            AllowParallelFetching = true,
            MaxParallelRequests = 3,
            DelayBetweenPages = 100,
            MaxPagesLimit = 2000, // Up to 500K products
            EstimateTotalsForV2 = true
        },
        ["categories"] = new BigCommercePaginationStrategy
        {
            EntityType = "categories",
            PreferredPageSize = 100,
            MaxPageSize = 250,
            AllowParallelFetching = true,
            MaxParallelRequests = 2,
            DelayBetweenPages = 50,
            MaxPagesLimit = 100, // Up to 25K categories
            EstimateTotalsForV2 = true
        },
        ["brands"] = new BigCommercePaginationStrategy
        {
            EntityType = "brands",
            PreferredPageSize = 100,
            MaxPageSize = 250,
            AllowParallelFetching = false,
            MaxParallelRequests = 1,
            DelayBetweenPages = 50,
            MaxPagesLimit = 50, // Up to 12.5K brands
            EstimateTotalsForV2 = true
        },
        ["variants"] = new BigCommercePaginationStrategy
        {
            EntityType = "variants",
            PreferredPageSize = 25,
            MaxPageSize = 100,
            AllowParallelFetching = false,
            MaxParallelRequests = 1,
            DelayBetweenPages = 150,
            MaxPagesLimit = 5000, // Up to 500K variants
            EstimateTotalsForV2 = false
        },
        ["images"] = new BigCommercePaginationStrategy
        {
            EntityType = "images",
            PreferredPageSize = 25,
            MaxPageSize = 100,
            AllowParallelFetching = false,
            MaxParallelRequests = 1,
            DelayBetweenPages = 150,
            MaxPagesLimit = 10000, // Up to 1M images
            EstimateTotalsForV2 = false
        },
        ["modifiers"] = new BigCommercePaginationStrategy
        {
            EntityType = "modifiers",
            PreferredPageSize = 50,
            MaxPageSize = 100,
            AllowParallelFetching = false,
            MaxParallelRequests = 1,
            DelayBetweenPages = 100,
            MaxPagesLimit = 1000, // Up to 100K modifiers
            EstimateTotalsForV2 = false
        }
    };
    
    /// <summary>
    /// Global rate limiting configuration
    /// </summary>
    public int GlobalRateLimitPerSecond { get; set; } = 12;
    
    /// <summary>
    /// Default timeout for API requests
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Whether to automatically detect API version
    /// </summary>
    public bool AutoDetectApiVersion { get; set; } = true;
    
    /// <summary>
    /// Fallback API version if detection fails
    /// </summary>
    public BigCommerceApiVersion FallbackApiVersion { get; set; } = BigCommerceApiVersion.V3;
}

/// <summary>
/// Request model for fetching a specific page of entities
/// </summary>
public class EntityPageRequest
{
    /// <summary>
    /// Migration ID for tracking
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of entity to fetch
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Source store configuration
    /// </summary>
    public StoreConfiguration SourceStore { get; set; } = new();
    
    /// <summary>
    /// Pagination request parameters
    /// </summary>
    public BigCommercePaginationRequest PaginationRequest { get; set; } = new();
    
    /// <summary>
    /// Entity configuration for filtering
    /// </summary>
    public EntityConfiguration EntityConfig { get; set; } = new();
    
    /// <summary>
    /// Whether to validate the response
    /// </summary>
    public bool ValidateResponse { get; set; } = true;
    
    /// <summary>
    /// Maximum retry attempts for this page
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// Result model for entity page fetching
/// </summary>
public class EntityPageResult
{
    /// <summary>
    /// Entity type that was fetched
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Page number that was fetched
    /// </summary>
    public int PageNumber { get; set; }
    
    /// <summary>
    /// Raw entities data
    /// </summary>
    public List<Dictionary<string, object>> Entities { get; set; } = new();
    
    /// <summary>
    /// Entity IDs extracted from the data
    /// </summary>
    public List<string> EntityIds { get; set; } = new();
    
    /// <summary>
    /// Pagination information
    /// </summary>
    public BigCommercePaginatedResponse<Dictionary<string, object>> PaginationInfo { get; set; } = new();
    
    /// <summary>
    /// Any errors encountered during fetching
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// Response timestamp
    /// </summary>
    public DateTime ResponseTimestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// API call duration
    /// </summary>
    public int ResponseTimeMs { get; set; }
    
    /// <summary>
    /// Whether this page was successful
    /// </summary>
    public bool IsSuccessful => Errors.Count == 0;
} 