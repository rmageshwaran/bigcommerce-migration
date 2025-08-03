using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using System.ComponentModel.DataAnnotations;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for analyzing category hierarchy structure
/// Uses ChunkedHierarchicalDiscoveryStrategy for memory-safe hierarchy analysis
/// </summary>
public class AnalyzeHierarchyActivity
{
    private readonly IChunkedHierarchicalDiscoveryStrategy _discoveryStrategy;
    private readonly ILogger<AnalyzeHierarchyActivity> _logger;

    public AnalyzeHierarchyActivity(
        IChunkedHierarchicalDiscoveryStrategy discoveryStrategy, 
        ILogger<AnalyzeHierarchyActivity> logger)
    {
        _discoveryStrategy = discoveryStrategy ?? throw new ArgumentNullException(nameof(discoveryStrategy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyzes the category hierarchy structure using chunked processing
    /// </summary>
    /// <param name="request">Hierarchy analysis request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hierarchy metadata with structure analysis</returns>
    [Function("AnalyzeHierarchyActivity")]
    public async Task<HierarchyMetadata> AnalyzeHierarchyAsync(
        [ActivityTrigger] HierarchyAnalysisRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("🔍 [HIERARCHY-ANALYSIS] Starting hierarchy analysis for migration {MigrationId}", 
                request.MigrationId);

            // Validate request
            var validationErrors = ValidateRequest(request);
            if (validationErrors.Any())
            {
                var errorMessage = string.Join("; ", validationErrors);
                _logger.LogError("❌ [HIERARCHY-ANALYSIS] Validation failed for migration {MigrationId}: {Errors}", 
                    request.MigrationId, errorMessage);
                
                return new HierarchyMetadata
                {
                    TotalCategories = 0,
                    MaxDepth = 0,
                    LevelCounts = new Dictionary<int, int>(),
                    EstimatedProcessingTimeMinutes = 0
                };
            }

            // Create EntityDiscoveryRequest for the strategy
            var discoveryRequest = new EntityDiscoveryRequest
            {
                MigrationId = request.MigrationId,
                EntityType = "categories",
                SourceStore = request.SourceStore,
                CategoryTreeContext = request.CategoryTreeContext ?? new CategoryTreeContext
                {
                    SourceCategoryTreeId = "1", // Default fallback if not provided
                    DestinationCategoryTreeId = "1"
                }
            };

            // Use the chunked discovery strategy to analyze hierarchy
            var hierarchyMetadata = await _discoveryStrategy.AnalyzeHierarchyAsync(
                discoveryRequest, 
                request.Config, 
                cancellationToken);

            _logger.LogInformation("✅ [HIERARCHY-ANALYSIS] Completed hierarchy analysis for migration {MigrationId}: " +
                                 "{TotalCategories} categories, {MaxDepth} max depth, {EstimatedTime:F2} minutes estimated", 
                request.MigrationId, 
                hierarchyMetadata.TotalCategories, 
                hierarchyMetadata.MaxDepth, 
                hierarchyMetadata.EstimatedProcessingTimeMinutes);

            return hierarchyMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [HIERARCHY-ANALYSIS] Error analyzing hierarchy for migration {MigrationId}", 
                request.MigrationId);
            
            // Return empty metadata on error to allow migration to continue
            return new HierarchyMetadata
            {
                TotalCategories = 0,
                MaxDepth = 0,
                LevelCounts = new Dictionary<int, int>(),
                EstimatedProcessingTimeMinutes = 0
            };
        }
    }

    /// <summary>
    /// Validates the hierarchy analysis request
    /// </summary>
    private static List<string> ValidateRequest(HierarchyAnalysisRequest request)
    {
        var errors = new List<string>();

        if (request == null)
        {
            errors.Add("Request cannot be null");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.MigrationId))
            errors.Add("MigrationId is required");

        if (request.SourceStore == null)
            errors.Add("SourceStore is required");

        if (request.Config == null)
            errors.Add("ChunkedHierarchyConfig is required");

        return errors;
    }
}

/// <summary>
/// Request model for hierarchy analysis activity
/// </summary>
public class HierarchyAnalysisRequest
{
    [Required]
    public string MigrationId { get; set; } = string.Empty;

    [Required]
    public StoreConfiguration SourceStore { get; set; } = new();

    [Required]
    public ChunkedHierarchyConfiguration Config { get; set; } = new();

    /// <summary>
    /// Category tree context containing source and destination tree IDs
    /// </summary>
    public CategoryTreeContext? CategoryTreeContext { get; set; }
}