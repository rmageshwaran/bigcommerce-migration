using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;

namespace BigCommerce.Migration.Activities.Activities;

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
            _logger.LogInformation("🌳 [RESOLVE-TREE] ⭐ STARTING: Resolving category tree IDs for migration {MigrationId}", request.MigrationId);

            var channelMapping = request.MigrationRequest.ChannelMapping;
            if (channelMapping is null || !channelMapping.Any())
            {
                _logger.LogWarning("No channel mappings provided for migration {MigrationId}. Category migration will be skipped.", request.MigrationId);
                return new CategoryTreeContext();
            }

            var sourceChannelIds = channelMapping.Select(m => m.SourceChannel).Distinct().ToList();
            var destinationChannelIds = channelMapping.Select(m => m.DestinationChannel).Distinct().ToList();

            var sourceTrees = await GetTreesForChannels(request.MigrationRequest.SourceStore, sourceChannelIds, "source");
            var destinationTrees = await GetTreesForChannels(request.MigrationRequest.DestinationStore, destinationChannelIds, "destination");

            var result = request.CategoryTreeContext ?? new CategoryTreeContext();
            foreach (var mapping in channelMapping)
            {
                if (sourceTrees.TryGetValue(mapping.SourceChannel, out var sourceTreeId) &&
                    destinationTrees.TryGetValue(mapping.DestinationChannel, out var destinationTreeId))
                {
                    result.CategoryTreeIdMapping[sourceTreeId] = destinationTreeId;
                }
                else
                {
                    _logger.LogWarning("Could not resolve category tree for channel mapping: Source {SourceChannel} -> Destination {DestinationChannel}",
                        mapping.SourceChannel, mapping.DestinationChannel);
                }
            }

            _logger.LogInformation("Category tree IDs resolved successfully for migration {MigrationId}", request.MigrationId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve category tree IDs for migration {MigrationId}", request.MigrationId);
            throw; // Let the orchestrator handle the failure properly
        }
    }

    private async Task<Dictionary<string, string>> GetTreesForChannels(StoreConfiguration store, List<string> channelIds, string storeType)
    {
        var trees = await _apiClient.GetCategoryTreesAsync(store, channelIds, CancellationToken.None);
        var channelTreeMapping = new Dictionary<string, string>();

        foreach (var tree in trees)
        {
            if (tree.TryGetValue("id", out var treeIdObj) && treeIdObj != null &&
                tree.TryGetValue("channels", out var channelsObj) && channelsObj is System.Text.Json.JsonElement channelsElement && channelsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var treeId = treeIdObj.ToString();
                foreach (var channelElement in channelsElement.EnumerateArray())
                {
                    if (channelElement.TryGetInt32(out var channelId))
                    {
                        channelTreeMapping[channelId.ToString()] = treeId;
                    }
                }
            }
        }
        return channelTreeMapping;
    }
}

/// <summary>
/// Request model for category tree ID resolution
/// </summary>
public class ResolveCategoryTreeIdsRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public MigrationRequest MigrationRequest { get; set; } = new();
    public CategoryTreeContext? CategoryTreeContext { get; set; }
} 