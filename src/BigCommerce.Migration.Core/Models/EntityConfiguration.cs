namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Entity-specific configuration for migration processing
/// </summary>
public class EntityConfiguration
{
    /// <summary>
    /// Entity type this configuration applies to
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether to include soft-deleted entities
    /// </summary>
    public bool IncludeDeleted { get; set; } = false;
    
    /// <summary>
    /// Whether to include draft entities
    /// </summary>
    public bool IncludeDrafts { get; set; } = false;
    
    /// <summary>
    /// Whether to include entity images
    /// </summary>
    public bool IncludeImages { get; set; } = true;
    
    /// <summary>
    /// Whether to include entity metadata
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;
    
    /// <summary>
    /// Whether to create mappings for this entity type
    /// </summary>
    public bool CreateMappings { get; set; } = true;
    
    /// <summary>
    /// Chunk size for orchestrator-level chunking (how many entities per chunk)
    /// </summary>
    public int ChunkSize { get; set; } = 50;

    /// <summary>
    /// Fetch batch size for API calls (limit parameter for pagination)
    /// </summary>
    public int FetchBatchSize { get; set; } = 250;

    /// <summary>
    /// Page size for initial batch creation (how many entities per page)
    /// </summary>
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// Number of entities per sub-batch (within each page)
    /// </summary>
    public int SubBatchSize { get; set; } = 5;

    /// <summary>
    /// Maximum number of entities to process concurrently within a sub-batch
    /// </summary>
    public int MaxConcurrency { get; set; } = 5;

    /// <summary>
    /// Whether sub-batches should be processed sequentially (true) or in parallel (false)
    /// </summary>
    public bool ProcessSubBatchesSequentially { get; set; } = true;
    
    /// <summary>
    /// Custom field mappings
    /// </summary>
    public Dictionary<string, string> FieldMappings { get; set; } = new();
    
    /// <summary>
    /// Entity-specific batch size override
    /// </summary>
    public int? BatchSizeOverride { get; set; }
    
    /// <summary>
    /// Entity-specific retry count override
    /// </summary>
    public int? MaxRetriesOverride { get; set; }
    
    /// <summary>
    /// Entity-specific rate limit override
    /// </summary>
    public int? RateLimitOverride { get; set; }
    
    /// <summary>
    /// Additional entity-specific settings
    /// </summary>
    public Dictionary<string, object> Settings { get; set; } = new();
} 