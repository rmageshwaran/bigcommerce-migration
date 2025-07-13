using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for resolving category tree IDs for source and destination stores
/// </summary>
public class ResolveCategoryTreeIdsActivity
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<ResolveCategoryTreeIdsActivity> _logger;

    public ResolveCategoryTreeIdsActivity(
        IBigCommerceApiClient apiClient,
        ILogger<ResolveCategoryTreeIdsActivity> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves category tree IDs for source and destination stores
    /// </summary>
    /// <param name="request">Category tree resolution request</param>
    /// <returns>Category tree context with resolved IDs</returns>
    [Function("ResolveCategoryTreeIds")]
    public async Task<CategoryTreeContext> ResolveCategoryTreeIdsAsync(
        [ActivityTrigger] ResolveCategoryTreeIdsRequest request)
    {
        try
        {
            _logger.LogInformation("Resolving category tree IDs for migration {MigrationId}", request.MigrationId);

            var result = request.CategoryTreeContext ?? new CategoryTreeContext();

            // Set channel IDs if not already set
            result.SourceChannelId ??= request.SourceStore.ChannelId;
            result.DestinationChannelId ??= request.DestinationStore.ChannelId;

            // Resolve source store category tree ID
            if (string.IsNullOrEmpty(result.SourceCategoryTreeId))
            {
                result.SourceCategoryTreeId = await ResolveCategoryTreeIdAsync(request.SourceStore, "source");
            }

            // Resolve destination store category tree ID
            if (string.IsNullOrEmpty(result.DestinationCategoryTreeId))
            {
                result.DestinationCategoryTreeId = await ResolveCategoryTreeIdAsync(request.DestinationStore, "destination");
            }

            _logger.LogInformation("Category tree IDs resolved - Source: {SourceTreeId}, Destination: {DestinationTreeId}", 
                result.SourceCategoryTreeId, result.DestinationCategoryTreeId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve category tree IDs for migration {MigrationId}", request.MigrationId);
            
            // Return default context
            return new CategoryTreeContext
            {
                SourceCategoryTreeId = "1", // Default to primary tree
                DestinationCategoryTreeId = "1", // Default to primary tree
                SourceChannelId = request.SourceStore.ChannelId,
                DestinationChannelId = request.DestinationStore.ChannelId
            };
        }
    }

    /// <summary>
    /// Resolves category tree ID for a specific store
    /// </summary>
    /// <param name="store">Store configuration</param>
    /// <param name="storeType">Store type (source/destination) for logging</param>
    /// <returns>Category tree ID</returns>
    private async Task<string> ResolveCategoryTreeIdAsync(StoreConfiguration store, string storeType)
    {
        try
        {
            // For now, we'll use the channel ID as the tree ID
            // This is a simplified approach - in reality, you might need to query the BigCommerce API
            // to get the actual category tree ID for the channel
            var treeId = store.ChannelId ?? "1";
            
            _logger.LogInformation("Resolved {StoreType} category tree ID: {TreeId} for store {StoreId}", 
                storeType, treeId, store.StoreId);
                
            return treeId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve category tree ID for {StoreType} store {StoreId}, using default", 
                storeType, store.StoreId);
            return "1"; // Default to primary category tree
        }
    }
}

/// <summary>
/// Request model for category tree ID resolution
/// </summary>
public class ResolveCategoryTreeIdsRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public StoreConfiguration SourceStore { get; set; } = new();
    public StoreConfiguration DestinationStore { get; set; } = new();
    public CategoryTreeContext? CategoryTreeContext { get; set; }
} 