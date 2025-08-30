using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Strategies.Transform;

/// <summary>
/// Transform strategy for product-related phase - Phase 3 of product migration
/// 
/// Handles transformation of EntityMapping data into BigCommerce product update payloads
/// with related product IDs mapped from source to destination.
/// 
/// Key Features:
/// - Maps source related product IDs to destination IDs using EntityMappings
/// - Handles special business case "[-1]" as valid related product
/// - Skips products with null/empty RelatedProductsData (counted as processed)
/// - Creates minimal update payloads with only related_products field
/// - Supports BigCommerce Batch API format (array of product updates)
/// 
/// Business Logic:
/// - Input: EntityMapping with RelatedProductsData from Phase 1
/// - Processing: Map source IDs → destination IDs via EntityMappings lookup
/// - Output: Product update payload { "id": destId, "related_products": [mapped_ids] }
/// - Special Case: "[-1]" → "[-1]" (preserved as-is)
/// - Skip Case: null/empty RelatedProductsData → skip product (count as processed)
/// </summary>
public class ProductRelatedTransformStrategy : IEntityTransformStrategy
{
    #region Private Fields

    private readonly ILogger<ProductRelatedTransformStrategy> _logger;
    private readonly IMigrationStorageService _migrationStorageService;

    #endregion

    #region Properties

    /// <summary>
    /// Entity type handled by this strategy
    /// </summary>
    public string EntityType => "product-related";

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of ProductRelatedTransformStrategy
    /// </summary>
    /// <param name="logger">Logger for the strategy</param>
    /// <param name="migrationStorageService">Service for direct EntityMapping lookups</param>
    public ProductRelatedTransformStrategy(
        ILogger<ProductRelatedTransformStrategy> logger,
        IMigrationStorageService migrationStorageService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Transforms EntityMapping data into product update payload for BigCommerce Batch API
    /// 
    /// Note: For product-related phase, the input 'entity' is actually EntityMapping data,
    /// not a raw BigCommerce entity. This is a special case transformation.
    /// </summary>
    /// <param name="entity">EntityMapping data containing SourceId, DestinationId, RelatedProductsData</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="sourceStore">Source store configuration (not used in this phase)</param>
    /// <param name="destinationStore">Destination store configuration (not used in this phase)</param>
    /// <param name="categoryTreeContext">Category tree context (not used in this phase)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Product update payload for BigCommerce Batch API, or null if should be skipped</returns>
    public async Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> entity,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        // 🚫 CANCELLATION: Check both CancellationToken and cooperative cancellation
        cancellationToken.ThrowIfCancellationRequested();

        // 🔍 TRACE: Log entry to confirm method is called
        _logger.LogInformation("🚀 [PRODUCT-RELATED-TRANSFORM] METHOD CALLED for migration {MigrationId}", migrationId);

        try
        {
            // ✅ FIXED: Handle entities already marked as skipped by fetch phase
            var status = entity.GetValueOrDefault("status")?.ToString();
            if (status == "skipped")
            {
                var skippedSourceId = entity.GetValueOrDefault("SourceId")?.ToString();
                var skippedDestinationId = entity.GetValueOrDefault("DestinationId")?.ToString();
                var reason = entity.GetValueOrDefault("reason")?.ToString();
                
                _logger.LogDebug("🚫 [TRANSFORM] Entity already marked as skipped by fetch phase: SourceId={SourceId}, DestinationId={DestinationId}, Reason={Reason}", 
                    skippedSourceId, skippedDestinationId, reason);
                
                // Pass through the skip marker
                return new Dictionary<string, object>
                {
                    ["_skip"] = true,
                    ["_skipReason"] = reason ?? "fetch_phase_skip",
                    ["id"] = skippedDestinationId ?? "",
                    ["DestinationId"] = skippedDestinationId ?? "",
                    ["SourceId"] = skippedSourceId ?? ""
                };
            }

            // Extract EntityMapping data from the input
            var sourceId = entity.GetValueOrDefault("SourceId")?.ToString();
            var destinationId = entity.GetValueOrDefault("DestinationId")?.ToString();
            var relatedProductsData = entity.GetValueOrDefault("RelatedProductsData")?.ToString();

            if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(destinationId))
            {
                _logger.LogWarning(
                    "🔄 [TRANSFORM] Invalid EntityMapping data - missing SourceId or DestinationId: SourceId={SourceId}, DestinationId={DestinationId}",
                    sourceId, destinationId);
                
                // Return null to indicate this should be skipped
                return null!;
            }

            _logger.LogDebug(
                "🔍 [TRANSFORM-DEBUG] ENTRY: Processing product-related transform: SourceId={SourceId}, DestinationId={DestinationId}, RelatedProductsData='{RelatedProductsData}', IsNullOrWhiteSpace={IsNullOrWhiteSpace}",
                sourceId, destinationId, relatedProductsData, string.IsNullOrWhiteSpace(relatedProductsData));

            // Handle skip case: null or empty RelatedProductsData  
            // Note: Empty arrays "[]" are handled naturally by ParseRelatedProductIds → empty list → later skip
            if (string.IsNullOrWhiteSpace(relatedProductsData))
            {
                _logger.LogDebug(
                    "🚫 [TRANSFORM-DEBUG] SKIPPING: product with null/empty RelatedProductsData: '{Data}', DestinationId={DestinationId}",
                    relatedProductsData, destinationId);
                
                // Return special marker to indicate skip (but count as processed)
                return new Dictionary<string, object>
                {
                    ["_skip"] = true,
                    ["_skipReason"] = "null_or_empty_related_products_data",
                    ["id"] = destinationId,
                    ["DestinationId"] = destinationId,
                    ["SourceId"] = sourceId,
                    ["status"] = "skipped"
                };
            }

            _logger.LogDebug(
                "✅ [TRANSFORM-DEBUG] CONTINUING: RelatedProductsData is not null/empty, proceeding to parse: DestinationId={DestinationId}",
                destinationId);

            // Parse RelatedProductsData (JSON array of source product IDs)
            // 🔧 CLEAN SOLUTION: Unified parsing that handles both strings and numbers
            var sourceRelatedProductIds = ParseRelatedProductIds(relatedProductsData, destinationId);
            if (sourceRelatedProductIds == null)
            {
                // Parsing failed - return null to skip this entity
                return null!;
            }

            // Transform source IDs to destination IDs (returns List<object> with integers)
            var destinationRelatedProductIds = await MapRelatedProductIdsAsync(
                sourceRelatedProductIds, migrationId, cancellationToken);

            _logger.LogInformation(
                "🔄 [ID-MAPPING-DEBUG] Mapped related products: SourceCount={SourceCount}, DestinationCount={DestinationCount}, DestinationId={DestinationId}, SourceIds=[{SourceIds}], DestinationIds=[{DestinationIds}]",
                sourceRelatedProductIds.Count, destinationRelatedProductIds.Count, destinationId, 
                string.Join(", ", sourceRelatedProductIds), string.Join(", ", destinationRelatedProductIds));

            // Create product update payload for BigCommerce Batch API
            var updatePayload = new Dictionary<string, object>
            {
                ["id"] = int.Parse(destinationId), // Ensure product ID is also numeric
                ["DestinationId"] = destinationId, // ✅ FIX: Add DestinationId for creation strategy
                ["SourceId"] = sourceId, // ✅ FIX: Add SourceId for logging/tracking
                ["related_products"] = destinationRelatedProductIds
            };

            _logger.LogDebug(
                "✅ [TRANSFORM] Created product update payload: DestinationId={DestinationId}, RelatedProductsCount={Count}, RelatedProducts=[{RelatedProducts}]",
                destinationId, destinationRelatedProductIds.Count, string.Join(", ", destinationRelatedProductIds));

            return updatePayload;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("❌ [TRANSFORM] Product-related transformation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "❌ [TRANSFORM] Error transforming product-related data: MigrationId={MigrationId}",
                migrationId);
            throw;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Maps source related product IDs to destination IDs using EntityMappings
    /// Handles special business case "[-1]" by preserving it as-is
    /// </summary>
    /// <param name="sourceRelatedProductIds">List of source product IDs</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of mapped destination product IDs</returns>
    private async Task<List<object>> MapRelatedProductIdsAsync(
        List<string> sourceRelatedProductIds,
        string migrationId,
        CancellationToken cancellationToken)
    {
        var destinationIds = new List<object>();

        foreach (var sourceId in sourceRelatedProductIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Special business case: "[-1]" is preserved as-is
            if (sourceId == "-1")
            {
                destinationIds.Add(-1);
                _logger.LogInformation("✅ [TRANSFORM] Preserved special related product ID: [-1] → [-1]");
                continue;
            }

            // Look up destination ID for this source ID
            var destinationId = await FindDestinationIdAsync(sourceId, migrationId, cancellationToken);
            
            if (!string.IsNullOrEmpty(destinationId))
            {
                // 🔧 CRITICAL: BigCommerce API requires numeric product IDs only
                if (int.TryParse(destinationId, out var intDestinationId))
                {
                    destinationIds.Add(intDestinationId);
                    _logger.LogTrace("🔄 [TRANSFORM] Mapped related product: {SourceId} → {DestinationId} (numeric)", sourceId, intDestinationId);
                }
                else
                {
                    _logger.LogWarning("🚨 [TRANSFORM] Invalid destination ID - not numeric: {SourceId} → '{DestinationId}' - skipping",
                        sourceId, destinationId);
                    // Skip non-numeric destination IDs as BigCommerce API requires integers
                }
            }
            else
            {
                _logger.LogWarning(
                    "🚨 [ID-MAPPING-MISSING] Related product mapping not found - skipping: SourceId={SourceId}, MigrationId={MigrationId}",
                    sourceId, migrationId);
                // Skip missing mappings (don't add to list)
            }
        }

        _logger.LogInformation(
            "📊 [ID-MAPPING-SUMMARY] Migration {MigrationId}: SourceIds={SourceCount}, MappedIds={MappedCount}, SourceList=[{SourceList}], MappedList=[{MappedList}]",
            migrationId, sourceRelatedProductIds.Count, destinationIds.Count,
            string.Join(", ", sourceRelatedProductIds), string.Join(", ", destinationIds));

        return destinationIds;
    }

    /// <summary>
    /// Finds destination ID for a source product ID using direct EntityMapping lookup
    /// 
    /// ✅ PERFORMANCE OPTIMIZATION: Uses direct lookup by migrationId, entityType, and sourceId
    /// Instead of inefficient pagination through all product mappings
    /// </summary>
    /// <param name="sourceId">Source product ID</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Destination product ID, or null if not found</returns>
    private async Task<string?> FindDestinationIdAsync(
        string sourceId,
        string migrationId,
        CancellationToken cancellationToken)
    {
        try
        {
            // ✅ PERFORMANCE OPTIMIZATION: Direct lookup by migrationId, entityType, and sourceId
            // This is O(1) instead of O(n) where n = total number of products
            var mapping = await _migrationStorageService.GetEntityMappingAsync(
                migrationId, "products", sourceId);

            if (mapping != null)
            {
                _logger.LogTrace("🔄 [TRANSFORM] Found destination ID: {SourceId} → {DestinationId}", 
                    sourceId, mapping.DestinationId);
                return mapping.DestinationId;
            }

            _logger.LogWarning("🚨 [ENTITY-MAPPING-LOOKUP] No mapping found for source ID: {SourceId} in migration {MigrationId}", sourceId, migrationId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "❌ [TRANSFORM] Error finding destination ID for source: SourceId={SourceId}, MigrationId={MigrationId}",
                sourceId, migrationId);
            return null;
        }
    }

    /// <summary>
    /// 🔧 SIMPLE SOLUTION: Parse RelatedProductsData using simple string manipulation
    /// Extracts numeric IDs from various formats: "[1,2,3]", "\"[1,2,3]\"", "1,2,3", "[-1]", etc.
    /// </summary>
    /// <param name="relatedProductsData">String containing related product IDs in various formats</param>
    /// <param name="destinationId">Destination product ID for logging context</param>
    /// <returns>List of source product IDs as strings, or null if parsing fails</returns>
    private List<string>? ParseRelatedProductIds(string relatedProductsData, string destinationId)
    {
        try
        {
            _logger.LogDebug("🔍 [PARSE] Raw RelatedProductsData: '{Data}' for DestinationId={DestinationId}", 
                relatedProductsData, destinationId);

            if (string.IsNullOrWhiteSpace(relatedProductsData))
            {
                _logger.LogDebug("🔍 [PARSE] Empty RelatedProductsData for DestinationId={DestinationId}", destinationId);
                return new List<string>();
            }

            // 🔧 SIMPLE APPROACH: Clean up the string and extract comma-separated values
            string cleanedData = relatedProductsData
                .Trim()                     // Remove leading/trailing whitespace
                .Trim('"')                  // Remove outer quotes: "\"[1,2,3]\"" → "[1,2,3]"
                .Trim()                     // Remove any remaining whitespace
                .Trim('[', ']')             // Remove array brackets: "[1,2,3]" → "1,2,3"
                .Trim();                    // Final cleanup

            _logger.LogDebug("🧹 [PARSE] Cleaned data: '{CleanedData}' for DestinationId={DestinationId}", 
                cleanedData, destinationId);

            // Handle empty result after cleaning
            if (string.IsNullOrWhiteSpace(cleanedData))
            {
                _logger.LogDebug("🔍 [PARSE] No data after cleaning for DestinationId={DestinationId}", destinationId);
                return new List<string>();
            }

            // Split by comma and clean each value
            var result = cleanedData
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim().Trim('"'))  // Remove quotes from individual IDs
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            _logger.LogDebug("✅ [PARSE] Successfully parsed {Count} related product IDs for DestinationId={DestinationId}: [{IDs}]",
                result.Count, destinationId, string.Join(", ", result));
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PARSE] Unexpected error parsing RelatedProductsData: '{Data}' for DestinationId={DestinationId}", 
                relatedProductsData, destinationId);
            return null; // Signal parsing failure
        }
    }



    #endregion
}
