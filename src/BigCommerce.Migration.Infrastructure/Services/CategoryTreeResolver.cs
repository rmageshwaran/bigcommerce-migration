using System.Diagnostics;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Service for resolving category tree IDs for BigCommerce multi-storefront migrations
/// Uses request-based store credentials from MigrationRequest
/// </summary>
public class CategoryTreeResolver : ICategoryTreeResolver
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly IOpenSearchService _openSearchService;
    private readonly ILogger<CategoryTreeResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the CategoryTreeResolver
    /// </summary>
    /// <param name="apiClient">BigCommerce API client for tree retrieval</param>
    /// <param name="openSearchService">OpenSearch service for performance logging</param>
    /// <param name="logger">Logger instance for service operations</param>
    public CategoryTreeResolver(
        IBigCommerceApiClient apiClient,
        IOpenSearchService openSearchService,
        ILogger<CategoryTreeResolver> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves category tree IDs for source and destination channels from a migration request
    /// </summary>
    /// <param name="migrationRequest">Migration request containing source and destination store configurations</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>CategoryTreeContext with resolved tree IDs, or null if resolution failed</returns>
    public async Task<CategoryTreeContext?> ResolveCategoryTreeAsync(MigrationRequest migrationRequest, CancellationToken cancellationToken = default)
    {
        if (migrationRequest?.SourceStore == null || migrationRequest.DestinationStore == null)
        {
            _logger.LogWarning("Migration request is null or missing store configurations");
            return null;
        }

        if (!migrationRequest.IsValid())
        {
            _logger.LogWarning("Migration request validation failed");
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var sourceStore = migrationRequest.SourceStore;
        var destinationStore = migrationRequest.DestinationStore;

        try
        {
            _logger.LogInformation("Resolving category trees for migration: {MigrationSummary}", migrationRequest.GetSummary());

            // Get category trees for source store/channel
            var sourceTrees = await _apiClient.GetCategoryTreesAsync(sourceStore, cancellationToken);
            var sourceTreeId = FindTreeForChannel(sourceTrees, sourceStore.ChannelId!);

            if (string.IsNullOrEmpty(sourceTreeId))
            {
                _logger.LogWarning("No category tree found for source store {StoreId}, channel {ChannelId}", 
                    sourceStore.StoreId, sourceStore.ChannelId);
                return null;
            }

            // Get category trees for destination store/channel
            var destinationTrees = await _apiClient.GetCategoryTreesAsync(destinationStore, cancellationToken);
            var destinationTreeId = FindTreeForChannel(destinationTrees, destinationStore.ChannelId!);

            if (string.IsNullOrEmpty(destinationTreeId))
            {
                _logger.LogWarning("No category tree found for destination store {StoreId}, channel {ChannelId}", 
                    destinationStore.StoreId, destinationStore.ChannelId);
                return null;
            }

            stopwatch.Stop();

            var context = new CategoryTreeContext
            {
                SourceChannelId = sourceStore.ChannelId,
                DestinationChannelId = destinationStore.ChannelId,
                SourceCategoryTreeId = sourceTreeId,
                DestinationCategoryTreeId = destinationTreeId
            };

            await LogPerformanceMetrics("ResolveCategoryTree", stopwatch.Elapsed, migrationRequest);

            _logger.LogInformation("Successfully resolved category trees: Source[{SourceChannel}]={SourceTree}, Destination[{DestChannel}]={DestTree}", 
                sourceStore.ChannelId, sourceTreeId, destinationStore.ChannelId, destinationTreeId);

            return context;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Category tree resolution was cancelled for migration: {MigrationSummary}", migrationRequest.GetSummary());
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve category trees for migration: {MigrationSummary}", migrationRequest.GetSummary());
            return null;
        }
    }

    /// <summary>
    /// Finds the category tree ID for a specific channel from the API response (optimized)
    /// Handles both array of numbers [1,2,3] and array of objects [{"channel_id":1}] formats
    /// </summary>
    /// <param name="trees">List of category trees from BigCommerce API</param>
    /// <param name="channelId">Channel ID to search for</param>
    /// <returns>Category tree ID or null if not found</returns>
    private string? FindTreeForChannel(List<Dictionary<string, object>> trees, string channelId)
    {
        try
        {
            var targetChannelId = int.Parse(channelId);
            
            // First pass: Look for exact channel matches using LINQ with early exit
            var matchingTree = trees.FirstOrDefault(tree => IsTreeAssignedToChannel(tree, targetChannelId));
            if (matchingTree != null && matchingTree.TryGetValue("id", out var treeIdValue))
            {
                var treeId = treeIdValue.ToString();
                _logger.LogDebug("Found matching category tree {TreeId} for channel {ChannelId}", 
                    treeId, channelId);
                return treeId;
            }

            // Fallback: Look for default tree
            var defaultTree = trees.FirstOrDefault(tree => IsDefaultTree(tree));
            if (defaultTree != null && defaultTree.TryGetValue("id", out var defaultTreeIdValue))
            {
                var treeId = defaultTreeIdValue.ToString();
                _logger.LogDebug("Using default category tree {TreeId} for channel {ChannelId}", 
                    treeId, channelId);
                return treeId;
            }

            _logger.LogWarning("No category tree found for channel {ChannelId} in {TreeCount} available trees", 
                channelId, trees.Count);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing category trees for channel {ChannelId}", channelId);
            return null;
        }
    }

    /// <summary>
    /// Checks if a category tree is assigned to a specific channel (optimized helper)
    /// </summary>
    private static bool IsTreeAssignedToChannel(Dictionary<string, object> tree, int targetChannelId)
    {
        if (!tree.TryGetValue("channels", out var channelsValue) || 
            channelsValue is not System.Text.Json.JsonElement channelsElement ||
            channelsElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        // Use LINQ Any() for efficient short-circuit evaluation
        return channelsElement.EnumerateArray().Any(channelElement =>
        {
            // Handle array of numbers format: [1, 2, 3]
            if (channelElement.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                return channelElement.TryGetInt32(out var channelIdInt) && channelIdInt == targetChannelId;
            }
            
            // Handle array of objects format: [{"channel_id": 1}]
            if (channelElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                channelElement.TryGetProperty("channel_id", out var channelIdElement))
            {
                if (channelIdElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    return channelIdElement.TryGetInt32(out var channelIdInt) && channelIdInt == targetChannelId;
                }
                if (channelIdElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return channelIdElement.GetString() == targetChannelId.ToString();
                }
            }
            
            return false;
        });
    }

    /// <summary>
    /// Checks if a category tree is marked as default (optimized helper)
    /// </summary>
    private static bool IsDefaultTree(Dictionary<string, object> tree)
    {
        return tree.TryGetValue("is_default", out var isDefaultValue) && 
               isDefaultValue is System.Text.Json.JsonElement defaultElement &&
               defaultElement.ValueKind == System.Text.Json.JsonValueKind.True;
    }

    /// <summary>
    /// Logs performance metrics for category tree resolution
    /// </summary>
    private async Task LogPerformanceMetrics(string operation, TimeSpan duration, MigrationRequest migrationRequest)
    {
        try
        {
            var metrics = new Dictionary<string, object>
            {
                ["operation"] = operation,
                ["duration_ms"] = duration.TotalMilliseconds,
                ["source_store_id"] = migrationRequest.SourceStore!.StoreId!,
                ["source_channel_id"] = migrationRequest.SourceStore.ChannelId!,
                ["destination_store_id"] = migrationRequest.DestinationStore!.StoreId!,
                ["destination_channel_id"] = migrationRequest.DestinationStore.ChannelId!,
                ["timestamp"] = DateTime.UtcNow
            };

            await _openSearchService.LogPerformanceMetricsAsync("category_tree_resolver", duration, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log performance metrics for operation {Operation}", operation);
        }
    }
} 