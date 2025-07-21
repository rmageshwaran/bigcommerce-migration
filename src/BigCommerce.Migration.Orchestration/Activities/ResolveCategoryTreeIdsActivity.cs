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
                try
                {
                    result.SourceCategoryTreeId = await ResolveCategoryTreeIdAsync(request.SourceStore, "source");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to resolve source category tree ID for migration {MigrationId} - this will cause category migration to fail", request.MigrationId);
                    throw new InvalidOperationException($"Cannot resolve source category tree ID for store {request.SourceStore.StoreId}: {ex.Message}", ex);
                }
            }

            // Resolve destination store category tree ID
            if (string.IsNullOrEmpty(result.DestinationCategoryTreeId))
            {
                try
                {
                    result.DestinationCategoryTreeId = await ResolveCategoryTreeIdAsync(request.DestinationStore, "destination");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to resolve destination category tree ID for migration {MigrationId} - this will cause category migration to fail", request.MigrationId);
                    throw new InvalidOperationException($"Cannot resolve destination category tree ID for store {request.DestinationStore.StoreId}: {ex.Message}", ex);
                }
            }

            _logger.LogInformation("Category tree IDs resolved successfully - Source: {SourceTreeId}, Destination: {DestinationTreeId} for migration {MigrationId}", 
                result.SourceCategoryTreeId, result.DestinationCategoryTreeId, request.MigrationId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve category tree IDs for migration {MigrationId}", request.MigrationId);
            throw; // Let the orchestrator handle the failure properly
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
            _logger.LogInformation("Fetching actual category trees from BigCommerce API for {StoreType} store {StoreId}", 
                storeType, store.StoreId);

            // Call the BigCommerce API to get actual category trees
            var categoryTrees = await _apiClient.GetCategoryTreesAsync(store, CancellationToken.None);
            
            if (categoryTrees?.Any() == true)
            {
                // Look for a tree assigned to this specific channel
                var channelId = store.ChannelId;
                var matchingTree = FindTreeForChannel(categoryTrees, channelId);
                
                if (!string.IsNullOrEmpty(matchingTree))
                {
                    _logger.LogInformation("Found category tree {TreeId} for {StoreType} store {StoreId}, channel {ChannelId}", 
                        matchingTree, storeType, store.StoreId, channelId);
                    return matchingTree;
                }
                
                // Fallback: Use the first available tree
                var firstTree = categoryTrees.FirstOrDefault();
                if (firstTree?.TryGetValue("id", out var firstTreeId) == true)
                {
                    var treeId = firstTreeId.ToString()!;
                    _logger.LogWarning("No specific tree found for channel {ChannelId}, using first available tree {TreeId} for {StoreType} store {StoreId}", 
                        channelId, treeId, storeType, store.StoreId);
                    return treeId;
                }
            }
            
            _logger.LogError("No category trees found for {StoreType} store {StoreId}", storeType, store.StoreId);
            throw new InvalidOperationException($"No category trees available in {storeType} store {store.StoreId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve category tree ID for {StoreType} store {StoreId}", storeType, store.StoreId);
            throw; // Don't fall back to tree ID "1" - let the caller handle this properly
        }
    }
    
    /// <summary>
    /// Finds the category tree ID for a specific channel from the API response
    /// </summary>
    /// <param name="trees">List of category trees from BigCommerce API</param>
    /// <param name="channelId">Channel ID to search for</param>
    /// <returns>Category tree ID or null if not found</returns>
    private string? FindTreeForChannel(List<Dictionary<string, object>> trees, string? channelId)
    {
        if (string.IsNullOrEmpty(channelId) || !trees.Any())
        {
            return null;
        }

        try
        {
            var targetChannelId = int.Parse(channelId);
            
            foreach (var tree in trees)
            {
                if (!tree.TryGetValue("channels", out var channelsValue) || 
                    channelsValue is not System.Text.Json.JsonElement channelsElement ||
                    channelsElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    continue;
                }

                // Check if this tree is assigned to the target channel
                foreach (var channelElement in channelsElement.EnumerateArray())
                {
                    var isMatch = false;
                    
                    // Handle array of numbers format: [1, 2, 3]
                    if (channelElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        isMatch = channelElement.TryGetInt32(out var channelIdInt) && channelIdInt == targetChannelId;
                    }
                    // Handle array of objects format: [{"channel_id": 1}]
                    else if (channelElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                             channelElement.TryGetProperty("channel_id", out var channelIdElement))
                    {
                        if (channelIdElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                        {
                            isMatch = channelIdElement.TryGetInt32(out var channelIdInt) && channelIdInt == targetChannelId;
                        }
                        else if (channelIdElement.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            isMatch = channelIdElement.GetString() == targetChannelId.ToString();
                        }
                    }
                    
                    if (isMatch && tree.TryGetValue("id", out var treeIdValue))
                    {
                        return treeIdValue.ToString();
                    }
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing category trees for channel {ChannelId}", channelId);
            return null;
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