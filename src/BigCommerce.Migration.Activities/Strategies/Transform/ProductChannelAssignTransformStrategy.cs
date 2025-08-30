using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Strategies.Transform;

/// <summary>
/// Transform strategy for product-channel-assign phase - Phase 5 of product migration
/// 
/// Handles transformation of EntityMapping data with ChannelsData into channel assignment payloads
/// with channel IDs mapped from source to destination using ChannelMapping configuration.
/// 
/// Key Features:
/// - Maps source channel IDs to destination IDs using ChannelMapping from migration request
/// - Deduplicates channel assignments per product (same destination channel from multiple sources)
/// - Validates channel mappings and handles partial/missing mappings appropriately
/// - Creates bulk channel assignment payloads for BigCommerce API
/// - Supports continue-on-error policy with proper status classification
/// 
/// Business Logic:
/// - Input: EntityMapping with ChannelsData from Phase 1 + ChannelMapping config
/// - Processing: Map source channel IDs → destination channel IDs via ChannelMapping
/// - Output: Channel assignment payload { "product_id": destId, "channel_assignments": [...] }
/// - Deduplication: Remove duplicate destination channels per product
/// - Status Logic: Failed (no mappings), Skipped (partial mappings), Success (full processing)
/// </summary>
public class ProductChannelAssignTransformStrategy : IEntityTransformStrategy
{
    #region Private Fields

    private readonly ILogger<ProductChannelAssignTransformStrategy> _logger;

    #endregion

    #region Properties

    /// <summary>
    /// Entity type handled by this strategy
    /// </summary>
    public string EntityType => "product-channel-assign";

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of ProductChannelAssignTransformStrategy
    /// </summary>
    /// <param name="logger">Logger for the strategy</param>
    public ProductChannelAssignTransformStrategy(ILogger<ProductChannelAssignTransformStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Transforms EntityMapping data with ChannelsData into channel assignment payloads
    /// Maps source channel IDs to destination channel IDs using ChannelMapping configuration
    /// </summary>
    /// <param name="entity">EntityMapping data containing SourceId, DestinationId, ChannelsData</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="sourceStore">Source store configuration (not used in this phase)</param>
    /// <param name="destinationStore">Destination store configuration (not used in this phase)</param>
    /// <param name="categoryTreeContext">Category tree context (not used in this phase)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transformed channel assignment payload or null for failed/skipped products</returns>
    public async Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> entity,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            _logger.LogWarning("⚠️ [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Null entity provided");
            return new Dictionary<string, object>();
        }

        // Extract source and destination product IDs from EntityMapping
        if (!entity.TryGetValue("SourceId", out var sourceProductIdObj) || sourceProductIdObj == null ||
            !entity.TryGetValue("DestinationId", out var destinationProductIdObj) || destinationProductIdObj == null)
        {
            _logger.LogWarning("⚠️ [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Missing SourceId or DestinationId in entity for migration {MigrationId}", migrationId);
            return null;
        }

        var sourceProductId = sourceProductIdObj.ToString()!;
        var destinationProductId = destinationProductIdObj.ToString()!;

        // Check for ChannelsData field
        if (!entity.TryGetValue("ChannelsData", out var channelsDataObj) || channelsDataObj == null)
        {
            _logger.LogDebug("📋 [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Product {SourceProductId} has no ChannelsData, skipping (migration: {MigrationId})", 
                sourceProductId, migrationId);
            
            // Return skipped result - will be counted as skipped
            return new Dictionary<string, object>
            {
                ["id"] = destinationProductId,
                ["source_product_id"] = sourceProductId,
                ["status"] = "skipped_no_channels",
                ["channel_assignments"] = new List<Dictionary<string, object>>()
            };
        }

        var channelsDataJson = channelsDataObj.ToString();
        if (string.IsNullOrEmpty(channelsDataJson))
        {
            _logger.LogDebug("📋 [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Product {SourceProductId} has empty ChannelsData, skipping (migration: {MigrationId})", 
                sourceProductId, migrationId);
            
            // Return skipped result - will be counted as skipped
            return new Dictionary<string, object>
            {
                ["id"] = destinationProductId,
                ["source_product_id"] = sourceProductId,
                ["status"] = "skipped_empty_channels",
                ["channel_assignments"] = new List<Dictionary<string, object>>()
            };
        }

        // Get ChannelMapping configuration from entity data (injected by the service)
        var channelMappingConfig = ExtractChannelMappingFromEntity(entity);
        
        // ✅ DEBUG: Log channel mapping extraction results
        _logger.LogDebug("🔗 [TRANSFORM-DEBUG] Extracted {MappingCount} channel mappings for product {SourceProductId}: [{Mappings}]", 
            channelMappingConfig?.Count ?? 0, sourceProductId,
            channelMappingConfig?.Any() == true ? string.Join(", ", channelMappingConfig.Select(m => $"{m.SourceChannel}→{m.DestinationChannel}")) : "none");
        
        if (channelMappingConfig == null || !channelMappingConfig.Any())
        {
            _logger.LogError("❌ [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] No ChannelMapping configuration found in entity data for product {SourceProductId} (migration: {MigrationId})", 
                sourceProductId, migrationId);
            
            // Return failed result - no channel mapping configuration
            return new Dictionary<string, object>
            {
                ["id"] = destinationProductId,
                ["source_product_id"] = sourceProductId,
                ["status"] = "failed_no_channel_config",
                ["channel_assignments"] = new List<Dictionary<string, object>>()
            };
        }

        try
        {
            // Parse source channel IDs from ChannelsData using robust string manipulation
            var sourceChannelIds = ParseChannelsData(channelsDataJson, sourceProductId);
            
            // ✅ DEBUG: Log ChannelsData parsing results  
            _logger.LogDebug("📊 [TRANSFORM-DEBUG] Parsed ChannelsData for product {SourceProductId}: Raw={ChannelsData}, Parsed=[{ParsedChannels}]", 
                sourceProductId, channelsDataJson, string.Join(", ", sourceChannelIds));
            
            if (!sourceChannelIds.Any())
            {
                _logger.LogDebug("📋 [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Product {SourceProductId} has no valid channel IDs in ChannelsData, skipping (migration: {MigrationId})", 
                    sourceProductId, migrationId);
                
                return new Dictionary<string, object>
                {
                    ["id"] = destinationProductId,
                    ["source_product_id"] = sourceProductId,
                    ["status"] = "skipped_no_valid_channels",
                    ["channel_assignments"] = new List<Dictionary<string, object>>()
                };
            }

            // Map source channel IDs to destination channel IDs
            var channelAssignments = new List<Dictionary<string, object>>();
            var mappedChannels = new List<string>();
            var unmappedChannels = new List<string>();

            // ✅ DEBUG: Log channel mapping process for each channel
            _logger.LogDebug("🔗 [TRANSFORM-DEBUG] Starting channel mapping for product {SourceProductId}: {SourceChannelCount} source channels to map", 
                sourceProductId, sourceChannelIds.Count);
            
            foreach (var sourceChannelId in sourceChannelIds)
            {
                var mapping = channelMappingConfig.FirstOrDefault(cm => 
                    cm.SourceChannel.Equals(sourceChannelId, StringComparison.OrdinalIgnoreCase));
                
                if (mapping != null)
                {
                    channelAssignments.Add(new Dictionary<string, object>
                    {
                        ["product_id"] = int.Parse(destinationProductId),
                        ["channel_id"] = int.Parse(mapping.DestinationChannel)
                    });
                    mappedChannels.Add(sourceChannelId);
                    
                    // ✅ DEBUG: Log successful mapping
                    _logger.LogDebug("✅ [TRANSFORM-DEBUG] Mapped channel for product {SourceProductId}: {SourceChannel} → {DestinationChannel}", 
                        sourceProductId, sourceChannelId, mapping.DestinationChannel);
                }
                else
                {
                    unmappedChannels.Add(sourceChannelId);
                    
                    // ✅ DEBUG: Log unmapped channel
                    _logger.LogDebug("⚠️ [TRANSFORM-DEBUG] No mapping found for product {SourceProductId} channel {SourceChannel}", 
                        sourceProductId, sourceChannelId);
                }
            }
            
            // ✅ DEBUG: Log mapping summary
            _logger.LogInformation("📊 [TRANSFORM-DEBUG] Channel mapping summary for product {SourceProductId}: {MappedCount} mapped, {UnmappedCount} unmapped, {TotalAssignments} final assignments", 
                sourceProductId, mappedChannels.Count, unmappedChannels.Count, channelAssignments.Count);

            // Deduplicate channel assignments (same destination channel from multiple sources)
            var uniqueAssignments = channelAssignments
                .GroupBy(a => new { ProductId = a["product_id"], ChannelId = a["channel_id"] })
                .Select(g => g.First())
                .ToList();

            if (channelAssignments.Count != uniqueAssignments.Count)
            {
                _logger.LogInformation("🔄 [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Deduplicated {Original} → {Unique} channel assignments for product {SourceProductId} (migration: {MigrationId})", 
                    channelAssignments.Count, uniqueAssignments.Count, sourceProductId, migrationId);
            }

            // Determine status based on mapping results
            string status;
            if (!uniqueAssignments.Any())
            {
                // No mappings found - mark as FAILED
                status = "failed_no_mappings";
                _logger.LogError("❌ [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] No channel mappings found for product {SourceProductId} with channels [{Channels}] (migration: {MigrationId})", 
                    sourceProductId, string.Join(", ", sourceChannelIds), migrationId);
            }
            else if (unmappedChannels.Any())
            {
                // Partial mappings - mark as SKIPPED with warnings but process available mappings
                status = "skipped_partial_mappings";
                _logger.LogWarning("⚠️ [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Partial channel mappings for product {SourceProductId}: " +
                    "Mapped [{Mapped}], Unmapped [{Unmapped}] (migration: {MigrationId})", 
                    sourceProductId, string.Join(", ", mappedChannels), string.Join(", ", unmappedChannels), migrationId);
            }
            else
            {
                // All channels mapped successfully
                status = "success";
                _logger.LogDebug("✅ [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Successfully mapped {AssignmentCount} channel assignments for product {SourceProductId} (migration: {MigrationId})", 
                    uniqueAssignments.Count, sourceProductId, migrationId);
            }

            return new Dictionary<string, object>
            {
                ["id"] = destinationProductId,
                ["source_product_id"] = sourceProductId,
                ["status"] = status,
                ["channel_assignments"] = uniqueAssignments,
                ["mapped_channels"] = mappedChannels,
                ["unmapped_channels"] = unmappedChannels
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Failed to transform product {SourceProductId} (migration: {MigrationId}): {ErrorMessage}",
                sourceProductId, migrationId, ex.Message);

            return new Dictionary<string, object>
            {
                ["id"] = destinationProductId,
                ["source_product_id"] = sourceProductId,
                ["status"] = "failed_transform_error",
                ["channel_assignments"] = new List<Dictionary<string, object>>(),
                ["error_message"] = ex.Message
            };
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Extracts ChannelMapping configuration from the entity data
    /// </summary>
    /// <param name="entity">Entity data containing injected channel mapping</param>
    /// <returns>List of channel mappings or empty list if not found</returns>
    private List<ChannelMapping> ExtractChannelMappingFromEntity(Dictionary<string, object> entity)
    {
        try
        {
            // Check if ChannelMapping is injected in the entity data
            if (entity.TryGetValue("_channel_mapping", out var channelMappingObj) && channelMappingObj != null)
            {
                var channelMappingJson = channelMappingObj.ToString();
                if (!string.IsNullOrEmpty(channelMappingJson))
                {
                    return JsonSerializer.Deserialize<List<ChannelMapping>>(channelMappingJson) ?? new List<ChannelMapping>();
                }
            }

            return new List<ChannelMapping>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Failed to extract ChannelMapping from entity: {ErrorMessage}", ex.Message);
            return new List<ChannelMapping>();
        }
    }

    /// <summary>
    /// Parses ChannelsData JSON field to extract source channel IDs
    /// </summary>
    /// <param name="channelsDataJson">String containing channel data in various formats</param>
    /// <param name="sourceProductId">Source product ID for logging context</param>
    /// <returns>List of source channel IDs</returns>
    private List<string> ParseChannelsData(string channelsDataJson, string sourceProductId)
    {
        try
        {
            _logger.LogDebug("🔍 [PARSE] Raw ChannelsData: '{Data}' for SourceProductId={SourceProductId}", 
                channelsDataJson, sourceProductId);

            if (string.IsNullOrWhiteSpace(channelsDataJson))
            {
                _logger.LogDebug("🔍 [PARSE] Empty ChannelsData for SourceProductId={SourceProductId}", sourceProductId);
                return new List<string>();
            }

            // 🔧 ROBUST STRING MANIPULATION: Clean up the string and extract comma-separated values
            string cleanedData = channelsDataJson
                .Trim()                     // Remove leading/trailing whitespace
                .Trim('"')                  // Remove outer quotes: "\"[1,2,3]\"" → "[1,2,3]"
                .Trim()                     // Remove any remaining whitespace
                .Trim('[', ']')             // Remove array brackets: "[1,2,3]" → "1,2,3"
                .Trim();                    // Final cleanup

            _logger.LogDebug("🧹 [PARSE] Cleaned data: '{CleanedData}' for SourceProductId={SourceProductId}", 
                cleanedData, sourceProductId);

            // Handle empty result after cleaning
            if (string.IsNullOrWhiteSpace(cleanedData))
            {
                _logger.LogDebug("🔍 [PARSE] No data after cleaning for SourceProductId={SourceProductId}", sourceProductId);
                return new List<string>();
            }

            // Split by comma and clean each value
            var result = cleanedData
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim().Trim('"'))  // Remove quotes from individual IDs
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            _logger.LogDebug("✅ [PARSE] Successfully parsed {Count} channel IDs for SourceProductId={SourceProductId}: [{IDs}]",
                result.Count, sourceProductId, string.Join(", ", result));
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PRODUCT-CHANNEL-ASSIGN-TRANSFORM] Failed to parse ChannelsData: '{Data}' for SourceProductId={SourceProductId}", 
                channelsDataJson, sourceProductId);
            return new List<string>();
        }
    }

    #endregion
}
