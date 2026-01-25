using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Result model for entity discovery activities
/// Enhanced to support both V2 and V3 API optimization strategies
/// </summary>
public class EntityDiscoveryResult
{
    /// <summary>
    /// Type of entity discovered
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// List of discovered entity IDs
    /// </summary>
    public List<string> EntityIds { get; set; } = new();
    
    /// <summary>
    /// Total count of entities available
    /// </summary>
    public int TotalCount { get; set; }
    
    /// <summary>
    /// Discovery errors if any
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// Pagination metadata for coordinating paginated data retrieval
    /// Contains information about API version, total pages, etc.
    /// </summary>
    public Dictionary<string, object> PaginationMetadata { get; set; } = new();
    
    /// <summary>
    /// API version detected for this entity type
    /// </summary>
    public BigCommerceApiVersion ApiVersion { get; set; } = BigCommerceApiVersion.V3;
    
    /// <summary>
    /// For V3 APIs: Store actual entity data to avoid duplicate API calls
    /// For V2 APIs: This will be empty as we skip discovery phase
    /// </summary>
    public List<Dictionary<string, object>> EntityData { get; set; } = new();
    
    /// <summary>
    /// Indicates if discovery phase should be skipped for this entity type
    /// True for V2 APIs, False for V3 APIs
    /// </summary>
    public bool SkipDiscovery { get; set; } = false;
    
    /// <summary>
    /// For V3 APIs: Rich pagination metadata from API response
    /// </summary>
    public BigCommerceV3Pagination? V3PaginationMetadata { get; set; }
    
    /// <summary>
    /// Indicates if entity data is available (for V3 APIs)
    /// </summary>
    public bool HasEntityData => EntityData.Count > 0;
    
    /// <summary>
    /// Indicates if this is a successful discovery with data
    /// Enhanced to support memory-efficient V3 pagination strategies
    /// </summary>
    public bool IsSuccessful => Errors.Count == 0 && (HasEntityData || SkipDiscovery || (TotalCount > 0 && !HasEntityData && ApiVersion == BigCommerceApiVersion.V3));
}

/// <summary>
/// Result model for entity creation operations
/// </summary>
public class EntityCreationResult
{
    /// <summary>
    /// Whether the entity creation was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// The ID of the created entity (if successful)
    /// </summary>
    public string? CreatedEntityId { get; set; }
    
    /// <summary>
    /// Error message if creation failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Additional metadata about the creation process
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Result model for batch processing activities
/// </summary>
public class BatchProcessingResult
{
    /// <summary>
    /// Batch number processed
    /// </summary>
    public int BatchNumber { get; set; }
    
    /// <summary>
    /// Total entities processed in this batch
    /// </summary>
    public int TotalProcessed { get; set; }
    
    /// <summary>
    /// Number of successfully processed entities
    /// </summary>
    public int SuccessfulEntities { get; set; }
    
    /// <summary>
    /// Number of failed entities
    /// </summary>
    public int FailedEntities { get; set; }
    
    /// <summary>
    /// Number of entities skipped during processing (e.g., duplicates, transformations)
    /// </summary>
    public int SkippedEntities { get; set; }
    
    /// <summary>
    /// Number of entities cancelled during processing due to migration cancellation
    /// </summary>
    public int CancelledEntities { get; set; }
    
    /// <summary>
    /// Entity mappings created during processing
    /// </summary>
    public List<EntityMapping> EntityMappings { get; set; } = new();
    
    /// <summary>
    /// Processing errors
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// Processing time for this batch
    /// </summary>
    public TimeSpan ProcessingTime { get; set; }
}

/// <summary>
/// Result model for rate limiting checks
/// </summary>
public class RateLimitResult
{
    /// <summary>
    /// Whether the operation can proceed without delay
    /// </summary>
    public bool CanProceed { get; set; }
    
    /// <summary>
    /// Delay in milliseconds before next operation
    /// </summary>
    public int DelayMs { get; set; }
    
    /// <summary>
    /// Current request count in the rate limit window
    /// </summary>
    public int CurrentRequestCount { get; set; }
    
    /// <summary>
    /// Maximum requests allowed in the rate limit window
    /// </summary>
    public int RequestLimit { get; set; }
    
    /// <summary>
    /// Time until rate limit window resets
    /// </summary>
    public TimeSpan WindowResetTime { get; set; }
} 