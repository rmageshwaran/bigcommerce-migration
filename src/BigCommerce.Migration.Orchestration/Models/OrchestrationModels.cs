using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Orchestration.Models;

// EntityConfiguration moved to BigCommerce.Migration.Core.Models

/// <summary>
/// Migration statistics for tracking performance and outcomes
/// </summary>
public class MigrationStatistics
{
    /// <summary>
    /// Total migration duration
    /// </summary>
    public TimeSpan TotalDuration { get; set; }
    
    /// <summary>
    /// Total number of API calls made
    /// </summary>
    public int TotalApiCalls { get; set; }
    
    /// <summary>
    /// Total number of successful API calls
    /// </summary>
    public int SuccessfulApiCalls { get; set; }
    
    /// <summary>
    /// Total number of failed API calls
    /// </summary>
    public int FailedApiCalls { get; set; }
    
    /// <summary>
    /// Average response time in milliseconds
    /// </summary>
    public double AverageResponseTimeMs { get; set; }
    
    /// <summary>
    /// Peak API calls per minute
    /// </summary>
    public double PeakApiCallsPerMinute { get; set; }
    
    /// <summary>
    /// Total bytes transferred
    /// </summary>
    public long TotalBytesTransferred { get; set; }
    
    /// <summary>
    /// Total number of entities processed across all types
    /// </summary>
    public int TotalEntitiesProcessed { get; set; }
    
    /// <summary>
    /// Total number of entities successfully migrated
    /// </summary>
    public int TotalEntitiesSuccessful { get; set; }
    
    /// <summary>
    /// Total number of entities that failed migration
    /// </summary>
    public int TotalEntitiesFailed { get; set; }
    
    /// <summary>
    /// Total number of retry attempts
    /// </summary>
    public int TotalRetryAttempts { get; set; }
    
    /// <summary>
    /// Total number of rate limit delays
    /// </summary>
    public int TotalRateLimitDelays { get; set; }
    
    /// <summary>
    /// Total time spent waiting for rate limits
    /// </summary>
    public TimeSpan TotalRateLimitWaitTime { get; set; }
    
    /// <summary>
    /// Memory usage statistics
    /// </summary>
    public Dictionary<string, long> MemoryUsage { get; set; } = new();
    
    /// <summary>
    /// Performance counters
    /// </summary>
    public Dictionary<string, double> PerformanceCounters { get; set; } = new();
}

/// <summary>
/// Request model for the main migration orchestrator
/// Contains all information needed to start and manage a complete migration
/// </summary>
public class MigrationOrchestrationRequest
{
    /// <summary>
    /// Unique identifier for the migration
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Original migration request from the HTTP API
    /// </summary>
    public MigrationRequest OriginalRequest { get; set; } = new();
    
    /// <summary>
    /// Category tree context for channel-specific category operations
    /// </summary>
    public CategoryTreeContext CategoryTreeContext { get; set; } = new();
    
    /// <summary>
    /// Migration start time (UTC)
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Additional metadata for the migration
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    /// <summary>
    /// Validates the orchestration request
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValid()
    {
        var validationErrors = GetValidationErrors();
        return validationErrors.Count == 0;
    }
    
    /// <summary>
    /// Gets validation errors for the request
    /// </summary>
    /// <returns>List of validation error messages</returns>
    public List<string> GetValidationErrors()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(MigrationId))
            errors.Add("MigrationId is required");
            
        if (OriginalRequest?.IsValid() != true)
            errors.Add("OriginalRequest must be valid");
            
        return errors;
    }
}

/// <summary>
/// Request model for entity-specific migration sub-orchestrators
/// </summary>
public class EntityMigrationRequest
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of entity to migrate (categories, products, brands, etc.)
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Source store configuration
    /// </summary>
    public StoreConfiguration SourceStore { get; set; } = new();
    
    /// <summary>
    /// Destination store configuration
    /// </summary>
    public StoreConfiguration DestinationStore { get; set; } = new();
    
    /// <summary>
    /// Category tree context for channel-specific operations
    /// </summary>
    public CategoryTreeContext CategoryTreeContext { get; set; } = new();
    
    /// <summary>
    /// Entity-specific configuration
    /// </summary>
    public EntityConfiguration EntityConfig { get; set; } = new();
    
    /// <summary>
    /// Batch size for processing entities
    /// </summary>
    public int BatchSize { get; set; } = 10;
    
    /// <summary>
    /// Maximum number of retries for failed operations
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// Migration settings (optional)
    /// </summary>
    public MigrationSettings? Settings { get; set; }
    
    private static readonly string[] ValidEntityTypes = 
    {
        "categories", "products", "brands", "variants", "images", "modifiers"
    };
    
    /// <summary>
    /// Validates the entity migration request
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValid()
    {
        var validationErrors = GetValidationErrors();
        return validationErrors.Count == 0;
    }
    
    /// <summary>
    /// Gets validation errors for the request
    /// </summary>
    /// <returns>List of validation error messages</returns>
    public List<string> GetValidationErrors()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(MigrationId))
            errors.Add("MigrationId is required");
            
        if (string.IsNullOrWhiteSpace(EntityType))
            errors.Add("EntityType is required");
        else if (!ValidEntityTypes.Contains(EntityType.ToLowerInvariant()))
            errors.Add("EntityType must be one of: categories, products, brands, variants, images, modifiers");
            
        if (BatchSize <= 0)
            errors.Add("BatchSize must be greater than 0");
            
        if (MaxRetries < 0)
            errors.Add("MaxRetries cannot be negative");
            
        return errors;
    }
}

/// <summary>
/// Request model for batch processing activities
/// </summary>
public class BatchProcessingRequest
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of entity being processed
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Current batch number (1-based)
    /// </summary>
    public int BatchNumber { get; set; } = 1;
    
    /// <summary>
    /// Total number of batches for this entity type
    /// </summary>
    public int TotalBatches { get; set; } = 1;
    
    /// <summary>
    /// List of entity IDs to process in this batch
    /// </summary>
    public List<string> EntityIds { get; set; } = new();
    
    /// <summary>
    /// Source store configuration
    /// </summary>
    public StoreConfiguration SourceStore { get; set; } = new();
    
    /// <summary>
    /// Destination store configuration
    /// </summary>
    public StoreConfiguration DestinationStore { get; set; } = new();
    
    /// <summary>
    /// Category tree context for channel-specific operations
    /// </summary>
    public CategoryTreeContext CategoryTreeContext { get; set; } = new();
    
    /// <summary>
    /// Cached entity data from V3 discovery phase to avoid duplicate API calls
    /// For V2 APIs, this will be null and processing will use direct pagination
    /// </summary>
    public List<Dictionary<string, object>>? CachedEntityData { get; set; }
    
    /// <summary>
    /// Pagination metadata from discovery phase for efficient pagination strategies
    /// </summary>
    public Dictionary<string, object>? PaginationMetadata { get; set; }
    
    /// <summary>
    /// Indicates whether to use direct pagination instead of entity ID batching
    /// Used for efficient pagination strategies that return empty EntityIds
    /// </summary>
    public bool UseDirectPagination { get; set; }
    
    /// <summary>
    /// Additional data for passing context between activities
    /// Used for preserving error details without causing orchestration replay
    /// </summary>
    public Dictionary<string, object> AdditionalData { get; set; } = new();
    
    /// <summary>
    /// Validates the batch processing request
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValid()
    {
        var validationErrors = GetValidationErrors();
        return validationErrors.Count == 0;
    }
    
    /// <summary>
    /// Gets validation errors for the request
    /// </summary>
    /// <returns>List of validation error messages</returns>
    public List<string> GetValidationErrors()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(MigrationId))
            errors.Add("MigrationId is required");
            
        if (string.IsNullOrWhiteSpace(EntityType))
            errors.Add("EntityType is required");
            
        if (BatchNumber < 1)
            errors.Add("BatchNumber must be greater than 0");
            
        if (TotalBatches < 1)
            errors.Add("TotalBatches must be greater than 0");
            
        if (BatchNumber > TotalBatches)
            errors.Add("BatchNumber cannot be greater than TotalBatches");
            
        return errors;
    }
}

/// <summary>
/// Result model for completed migrations
/// </summary>
public class MigrationResult
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Current migration status
    /// </summary>
    public MigrationStatus Status { get; set; } = MigrationStatus.InProgress;
    
    /// <summary>
    /// Migration start time
    /// </summary>
    public DateTime StartTime { get; set; }
    
    /// <summary>
    /// Migration end time
    /// </summary>
    public DateTime EndTime { get; set; }
    
    /// <summary>
    /// Total migration duration
    /// </summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>
    /// Results by entity type
    /// </summary>
    public Dictionary<string, EntityMigrationResult> EntityResults { get; set; } = new();
    
    /// <summary>
    /// Migration-level errors
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// Migration statistics
    /// </summary>
    public MigrationStatistics Statistics { get; set; } = new();
    
    /// <summary>
    /// Calculates and updates the duration based on start and end times
    /// </summary>
    public void CalculateDuration()
    {
        Duration = EndTime - StartTime;
    }
    
    /// <summary>
    /// Gets a summary of migration statistics across all entities
    /// </summary>
    /// <returns>Migration statistics summary</returns>
    public MigrationStatisticsSummary GetStatisticsSummary()
    {
        var summary = new MigrationStatisticsSummary();
        
        foreach (var entityResult in EntityResults.Values)
        {
            summary.TotalEntities += entityResult.TotalEntities;
            summary.SuccessfulEntities += entityResult.SuccessfulEntities;
            summary.FailedEntities += entityResult.FailedEntities;
        }
        
        if (summary.TotalEntities > 0)
        {
            summary.SuccessRate = Math.Round((double)summary.SuccessfulEntities / summary.TotalEntities * 100, 2);
        }
        
        return summary;
    }
}

/// <summary>
/// Result model for entity-specific migration results
/// </summary>
public class EntityMigrationResult
{
    /// <summary>
    /// Entity type that was migrated
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the entity migration was successful
    /// </summary>
    public bool IsSuccess { get; set; }
    
    /// <summary>
    /// Error message if the migration failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Migration start time
    /// </summary>
    public DateTime StartTime { get; set; }
    
    /// <summary>
    /// Migration end time
    /// </summary>
    public DateTime? EndTime { get; set; }
    
    /// <summary>
    /// Total migration duration
    /// </summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>
    /// Total number of entities discovered
    /// </summary>
    public int TotalEntities { get; set; }
    
    /// <summary>
    /// Number of entities processed (attempted)
    /// </summary>
    public int ProcessedEntities { get; set; }
    
    /// <summary>
    /// Number of entities successfully migrated
    /// </summary>
    public int SuccessfulEntities { get; set; }
    
    /// <summary>
    /// Number of entities that failed migration
    /// </summary>
    public int FailedEntities { get; set; }
    
    /// <summary>
    /// Results from individual batch operations
    /// </summary>
    public List<BatchProcessingResult> BatchResults { get; set; } = new();
    
    /// <summary>
    /// Entity mappings (source ID -> destination ID)
    /// </summary>
    public List<EntityMapping> Mappings { get; set; } = new();
    
    /// <summary>
    /// Entity-level errors
    /// </summary>
    public List<string> Errors { get; set; } = new();
    
    /// <summary>
    /// Time taken to process this entity type (alias for Duration)
    /// </summary>
    public TimeSpan ProcessingTime { get; set; }
    
    /// <summary>
    /// Calculates the success rate for this entity type
    /// </summary>
    /// <returns>Success rate as a percentage</returns>
    public double GetSuccessRate()
    {
        if (TotalEntities == 0) return 0.0;
        return Math.Round((double)SuccessfulEntities / TotalEntities * 100, 2);
    }
    
    /// <summary>
    /// Validates the entity migration result
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValid()
    {
        var validationErrors = GetValidationErrors();
        return validationErrors.Count == 0;
    }
    
    /// <summary>
    /// Gets validation errors for the result
    /// </summary>
    /// <returns>List of validation error messages</returns>
    public List<string> GetValidationErrors()
    {
        var errors = new List<string>();
        
        if (SuccessfulEntities + FailedEntities > TotalEntities)
            errors.Add("SuccessfulEntities + FailedEntities cannot exceed TotalEntities");
            
        if (SuccessfulEntities < 0)
            errors.Add("SuccessfulEntities cannot be negative");
            
        if (FailedEntities < 0)
            errors.Add("FailedEntities cannot be negative");
            
        if (TotalEntities < 0)
            errors.Add("TotalEntities cannot be negative");
            
        return errors;
    }
}

/// <summary>
/// Summary statistics for migration results
/// </summary>
public class MigrationStatisticsSummary
{
    /// <summary>
    /// Total entities across all types
    /// </summary>
    public int TotalEntities { get; set; }
    
    /// <summary>
    /// Successful entities across all types
    /// </summary>
    public int SuccessfulEntities { get; set; }
    
    /// <summary>
    /// Failed entities across all types
    /// </summary>
    public int FailedEntities { get; set; }
    
    /// <summary>
    /// Overall success rate as percentage
    /// </summary>
    public double SuccessRate { get; set; }
}


/// <summary>
/// Request model for checking rate limits
/// </summary>
public class CheckRateLimitRequest
{
    /// <summary>
    /// Store ID to check rate limits for
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
    
    /// <summary>
    /// Entity type being processed (for context)
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
}

/// <summary>
/// Request model for updating entity progress
/// </summary>
public class UpdateEntityProgressRequest
{
    /// <summary>
    /// Migration ID
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Entity type being processed
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Current phase of processing
    /// </summary>
    public string Phase { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of entities to process
    /// </summary>
    public int TotalEntities { get; set; }
    
    /// <summary>
    /// Number of entities processed so far
    /// </summary>
    public int ProcessedEntities { get; set; }
    
    /// <summary>
    /// Number of entities successfully processed
    /// </summary>
    public int SuccessfulEntities { get; set; }
    
    /// <summary>
    /// Number of entities that failed processing
    /// </summary>
    public int FailedEntities { get; set; }
    
    /// <summary>
    /// Current batch number (optional)
    /// </summary>
    public int CurrentBatch { get; set; }
    
    /// <summary>
    /// Total number of batches (optional)
    /// </summary>
    public int TotalBatches { get; set; }
}

/// <summary>
/// Request model for starting entity processing
/// </summary>
public class StartEntityProcessingRequest
{
    /// <summary>
    /// Migration ID
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Entity type being processed
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of entities to process
    /// </summary>
    public int TotalCount { get; set; }
} 