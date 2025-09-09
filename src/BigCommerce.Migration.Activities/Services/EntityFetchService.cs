using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Implementation of IEntityFetchService for fetching entities from source stores
/// Refactored to use Strategy Pattern for Open/Closed Principle compliance
/// No longer violates OCP - new entity types can be added without modifying this class
/// </summary>
public class EntityFetchService : IEntityFetchService
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly IEntityFetchStrategyFactory _strategyFactory;
    private readonly ISubBatchConfigurationService _configService;
    private readonly IEntityMappingsPaginationService _entityMappingsPaginationService;
    private readonly ILogger<EntityFetchService> _logger;
    
    // All configuration values are now loaded dynamically from SubBatchConfigurationService

    public EntityFetchService(
        IBigCommerceApiClient apiClient, 
        IEntityFetchStrategyFactory strategyFactory,
        ISubBatchConfigurationService configService,
        IEntityMappingsPaginationService entityMappingsPaginationService,
        ILogger<EntityFetchService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _entityMappingsPaginationService = entityMappingsPaginationService ?? throw new ArgumentNullException(nameof(entityMappingsPaginationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<Dictionary<string, object>>> FetchEntitiesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        _logger.LogError("📥📥📥 [FETCH-SERVICE-DEBUG] ===== FETCH REQUEST RECEIVED =====");
        _logger.LogError("📥 [FETCH-SERVICE-DEBUG] EntityType={EntityType}, MigrationId={MigrationId}, BatchNumber={BatchNumber}", 
            request.EntityType, request.MigrationId, request.BatchNumber);
        _logger.LogError("📥 [FETCH-SERVICE-DEBUG] EntityIds.Count={EntityIdsCount}, UseDirectPagination={UseDirectPagination}", 
            request.EntityIds?.Count ?? 0, request.UseDirectPagination);
        _logger.LogError("📥 [FETCH-SERVICE-DEBUG] PaginationMetadata count: {PaginationMetadataCount}", 
            request.PaginationMetadata?.Count ?? 0);
            
        if (request.PaginationMetadata != null && request.PaginationMetadata.Any())
        {
            foreach (var kvp in request.PaginationMetadata)
            {
                _logger.LogError("📥 [FETCH-SERVICE-DEBUG] PaginationMetadata[{Key}]={Value}", kvp.Key, kvp.Value);
            }
        }
        
        var config = _configService.GetConfiguration(request.EntityType);
        _logger.LogError("🔧 [FETCH-SERVICE-DEBUG] Configuration retrieved: FetchBatchSize={FetchBatchSize}, ChunkSize={ChunkSize}, PageSize={PageSize}", 
            config.FetchBatchSize, config.ChunkSize, config.PageSize);

        _logger.LogInformation("Fetching entities of type {EntityType} for batch {BatchNumber} in migration {MigrationId}", 
            request.EntityType, request.BatchNumber, request.MigrationId);

        // ✅ ENHANCED DEBUG: Log comprehensive decision-making process for debugging
        _logger.LogInformation("🔍 [FETCH-DECISION] EntityType={EntityType}, UseDirectPagination={UseDirectPagination}, " +
                              "HasEntityIds={HasEntityIds}, EntityIds.Count={EntityIdsCount}, " +
                              "CachedDataCount={CachedDataCount}, BatchNumber={BatchNumber}, MigrationId={MigrationId}",
            request.EntityType, request.UseDirectPagination, request.EntityIds?.Any() ?? false, 
            request.EntityIds?.Count ?? 0, request.CachedEntityData?.Count ?? 0, 
            request.BatchNumber, request.MigrationId);

        try
        {
            // 🚨 CRITICAL BRAND ROUTING: Brands MUST use direct pagination to prevent duplicates
            if (request.EntityType.Equals("brands", StringComparison.OrdinalIgnoreCase))
            {
                if (!request.UseDirectPagination)
                {
                    _logger.LogError("🚨 [FETCH-ROUTING] ❌ CRITICAL ERROR: Brands configured with UseDirectPagination=false! " +
                                   "This will cause duplicates. Forcing direct pagination. BatchNumber={BatchNumber}, MigrationId={MigrationId}", 
                        request.BatchNumber, request.MigrationId);
                }
                
                _logger.LogInformation("🏪 [FETCH-ROUTING] ✅ BRANDS: Using direct pagination for batch {BatchNumber} " +
                                      "in migration {MigrationId} (API Page={ApiPage}, ExpectedLimit=50)", 
                    request.BatchNumber, request.MigrationId, request.BatchNumber + 1);
                
                return await FetchEntitiesWithDirectPaginationAsync(request, cancellationToken);
            }
            
            // 🔧 PRODUCT-RELATED ROUTING: Fetch from EntityMappings table instead of BigCommerce API
            if (request.EntityType.Equals("product-related", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("📊 [FETCH-ROUTING] ✅ PRODUCT-RELATED: Using EntityMappings pagination for batch {BatchNumber} " +
                                      "in migration {MigrationId} (EntityMappings query instead of BigCommerce API)", 
                    request.BatchNumber, request.MigrationId);
                
                return await FetchEntitiesFromEntityMappingsAsync(request, cancellationToken);
            }
            
            // 🖼️ PRODUCT-IMAGES ROUTING: Fetch from EntityMappings table like product-related
            if (request.EntityType.Equals("product-images", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🖼️ [FETCH-ROUTING] ✅ PRODUCT-IMAGES: Using EntityMappings pagination for batch {BatchNumber} " +
                                      "in migration {MigrationId} (EntityMappings query instead of BigCommerce API)", 
                    request.BatchNumber, request.MigrationId);
                
                return await FetchEntitiesFromEntityMappingsAsync(request, cancellationToken);
            }
            
            // 🔗 PRODUCT-CHANNEL-ASSIGN ROUTING: Fetch from EntityMappings table like product-related
            if (request.EntityType.Equals("product-channel-assign", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🔗 [FETCH-ROUTING] ✅ PRODUCT-CHANNEL-ASSIGN: Using EntityMappings pagination for batch {BatchNumber} " +
                                      "in migration {MigrationId} (EntityMappings query instead of BigCommerce API)", 
                    request.BatchNumber, request.MigrationId);
                
                return await FetchEntitiesFromEntityMappingsAsync(request, cancellationToken);
            }
            
            // 🚨 CRITICAL VARIANT ROUTING: Variants MUST use direct pagination like products
            if (request.EntityType.Equals("variants", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("🎯 [FETCH-ROUTING] ✅ VARIANTS: Using direct pagination for batch {BatchNumber} " +
                                      "in migration {MigrationId} (API Page={ApiPage}) - aligned with product workflow", 
                    request.BatchNumber, request.MigrationId, request.BatchNumber + 1);
                
                return await FetchEntitiesWithDirectPaginationAsync(request, cancellationToken);
            }
            
            // Handle direct pagination for other entities with efficient strategies
            if (request.UseDirectPagination && (!request.EntityIds.Any() || request.EntityIds.First().StartsWith("page-")))
            {
                _logger.LogInformation("🎯 [FETCH-ROUTING] Using direct pagination for {EntityType} batch {BatchNumber} in migration {MigrationId}", 
                    request.EntityType, request.BatchNumber, request.MigrationId);
                
                return await FetchEntitiesWithDirectPaginationAsync(request, cancellationToken);
            }
            
            // Handle cached entity data (hierarchical strategies like categories)
            if (request.CachedEntityData != null && request.CachedEntityData.Any())
            {
                _logger.LogInformation("✅ Using cached entity data for {EntityType} batch {BatchNumber} in migration {MigrationId}", 
                    request.EntityType, request.BatchNumber, request.MigrationId);
                
                return GetCachedEntitiesForBatch(request);
            }

            // 🎯 STRATEGY PATTERN: Delegate to appropriate strategy for entity ID-based fetching
            // This eliminates OCP violation - new entity types can be added without modifying this code
            _logger.LogInformation("🚨 [FETCH-ROUTING] Using fetch strategy for {EntityType} batch {BatchNumber} in migration {MigrationId} - UseDirectPagination={UseDirectPagination}, EntityIds.Count={EntityIdsCount}", 
                request.EntityType, request.BatchNumber, request.MigrationId, request.UseDirectPagination, request.EntityIds?.Count ?? 0);
            
            var strategy = _strategyFactory.GetStrategy(request.EntityType);
            
            var result = await strategy.FetchEntitiesAsync(
                request.EntityIds,
                request.MigrationId,
                request.SourceStore,
                request.CachedEntityData,
                request.CategoryTreeContext,
                cancellationToken);

            _logger.LogInformation("Successfully fetched {Count} entities of type {EntityType} for batch {BatchNumber} in migration {MigrationId}", 
                result.Count, request.EntityType, request.BatchNumber, request.MigrationId);

            return result;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Unsupported entity type {EntityType} for batch {BatchNumber} in migration {MigrationId}", 
                request.EntityType, request.BatchNumber, request.MigrationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch entities of type {EntityType} for batch {BatchNumber} in migration {MigrationId}", 
                request.EntityType, request.BatchNumber, request.MigrationId);
            throw;
        }
    }

    public async Task<List<Dictionary<string, object>>> FetchCategoriesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 CACHED DATA HANDLING: Check for cached data first (common for category tree scenarios)
        if (request.CachedEntityData != null && request.CachedEntityData.Count > 0)
        {
            _logger.LogDebug("Using cached category data for migration {MigrationId}, count: {Count}", 
                request.MigrationId, request.CachedEntityData.Count);
            return request.CachedEntityData;
        }

        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchCategoriesAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        return await FetchEntitiesAsync(request, cancellationToken);
    }

    public async Task<List<Dictionary<string, object>>> FetchProductsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchProductsAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        try
        {
            return await FetchEntitiesAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            var message = "Product fetch failed";
            _logger.LogError(ex, "{Message} for migration {MigrationId}", message, request.MigrationId);
            throw new InvalidOperationException(message, ex);
        }
    }

    public async Task<List<Dictionary<string, object>>> FetchBrandsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchBrandsAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        return await FetchEntitiesAsync(request, cancellationToken);
    }

    public async Task<List<Dictionary<string, object>>> FetchVariantsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchVariantsAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        return await FetchEntitiesAsync(request, cancellationToken);
    }

    public async Task<List<Dictionary<string, object>>> FetchImagesAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchImagesAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        return await FetchEntitiesAsync(request, cancellationToken);
    }

    public async Task<List<Dictionary<string, object>>> FetchModifiersAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        // 🎯 REFACTORED: Delegate to strategy pattern instead of duplicating logic
        // This method is kept for backward compatibility with existing tests and interface
        _logger.LogDebug("FetchModifiersAsync called - delegating to strategy pattern for migration {MigrationId}", 
            request.MigrationId);
        
        return await FetchEntitiesAsync(request, cancellationToken);
    }

    #region Private Helper Methods

    /// <summary>
    /// Fetches entities using direct pagination (batch number = page number)
    /// Used for efficient pagination strategies that don't cache entity IDs
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchEntitiesWithDirectPaginationAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        _logger.LogError("🚀🚀🚀 [DIRECT-PAGINATION-DEBUG] ===== DIRECT PAGINATION METHOD CALLED =====");
        _logger.LogError("🚀 [DIRECT-PAGINATION-DEBUG] EntityType={EntityType}, MigrationId={MigrationId}, BatchNumber={BatchNumber}", 
            request.EntityType, request.MigrationId, request.BatchNumber);
        
        // Get configuration again for this method scope
        var methodConfig = _configService.GetConfiguration(request.EntityType);
        _logger.LogError("🚀 [DIRECT-PAGINATION-DEBUG] Config in this method: FetchBatchSize={FetchBatchSize}, ChunkSize={ChunkSize}, PageSize={PageSize}", 
            methodConfig.FetchBatchSize, methodConfig.ChunkSize, methodConfig.PageSize);
            
        // 🚨 CRITICAL FIX: BigCommerce API pages are 1-based, but our batches are 0-based
        // Batch 0 should map to Page 1, Batch 1 to Page 2, etc.
        var pageNumber = request.BatchNumber + 1; // Convert 0-based batch to 1-based page
        
        _logger.LogError("🚨🚨🚨 [PAGINATION-DEBUG] DIRECT PAGINATION: BatchNumber={BatchNumber} → pageNumber={PageNumber} for {EntityType} in migration {MigrationId}", 
            request.BatchNumber, pageNumber, request.EntityType, request.MigrationId);
        
        _logger.LogInformation("🔍 [FETCH-PAGINATION] CONVERSION: Batch={BatchNumber} → API Page={ApiPage} for {EntityType} in migration {MigrationId}", 
            request.BatchNumber, pageNumber, request.EntityType, request.MigrationId);
        
        // 🎯 CHUNKED ORCHESTRATION FIX: Use chunk size from request, not hardcoded 50
        // For chunked orchestration, we need to fetch the full chunk size (up to 500 entities)
        var requestedChunkSize = request.EntityIds?.Count ?? 0;
        var isChunkedRequest = requestedChunkSize == 0 && request.PaginationMetadata?.ContainsKey("TotalCount") == true;
        
        _logger.LogInformation("🚨 [CHUNK-DEBUG] RequestedChunkSize={RequestedChunkSize}, IsChunkedRequest={IsChunkedRequest}, HasPaginationMetadata={HasMetadata} for {EntityType} batch {BatchNumber}",
            requestedChunkSize, isChunkedRequest, request.PaginationMetadata != null, request.EntityType, request.BatchNumber);
        
        // 🚨 DEBUG: Log all pagination metadata to diagnose chunk duplication
        if (request.PaginationMetadata != null)
        {
            foreach (var kvp in request.PaginationMetadata)
            {
                _logger.LogDebug("🔍 [FETCH-METADATA] {Key}={Value} for {EntityType} batch {BatchNumber}", 
                    kvp.Key, kvp.Value, request.EntityType, request.BatchNumber);
            }
        }
        else
        {
            _logger.LogDebug("🔍 [FETCH-METADATA] No PaginationMetadata for {EntityType} batch {BatchNumber}", 
                request.EntityType, request.BatchNumber);
        }
        
        // 🚨 FIXED: Use configuration-driven batch size
        var config = _configService.GetConfiguration(request.EntityType);
        var batchSize = config.FetchBatchSize; // ✅ NOW CONFIGURABLE!
        
        _logger.LogInformation("🔍 [FETCH-BATCHSIZE] Configuration-driven: BatchSize={BatchSize} for {EntityType} (from config.FetchBatchSize)", 
            batchSize, request.EntityType);
        
        if (isChunkedRequest && request.PaginationMetadata?.TryGetValue("TotalCount", out var totalCountObj) == true)
        {
            // Handle JsonElement conversion safely
            int totalCount = 50; // Default fallback
            if (totalCountObj is System.Text.Json.JsonElement jsonElement)
            {
                totalCount = jsonElement.TryGetInt32(out var intValue) ? intValue : 50;
            }
            else if (totalCountObj is int directInt)
            {
                totalCount = directInt;
            }
            else if (int.TryParse(totalCountObj?.ToString(), out var parsedInt))
            {
                totalCount = parsedInt;
            }
            
            batchSize = Math.Min(500, totalCount); // Chunked: use total count (max 500)
        }
        
        // 🚨 CHUNK FIX: Calculate correct page offset for chunked requests
        var startIndex = 0;
        var chunkSize = batchSize;
        
        if (isChunkedRequest && request.PaginationMetadata != null)
        {
            // Extract chunk-specific metadata
            if (request.PaginationMetadata.TryGetValue("StartIndex", out var startIndexObj))
            {
                if (startIndexObj is System.Text.Json.JsonElement startJsonElement)
                {
                    startIndex = startJsonElement.TryGetInt32(out var startValue) ? startValue : 0;
                }
                else if (startIndexObj is int directStartInt)
                {
                    startIndex = directStartInt;
                }
                else if (int.TryParse(startIndexObj?.ToString(), out var parsedStartInt))
                {
                    startIndex = parsedStartInt;
                }
                
                _logger.LogInformation("🚨 [STARTINDEX-DEBUG] Extracted StartIndex={StartIndex} from PaginationMetadata for {EntityType} batch {BatchNumber}",
                    startIndex, request.EntityType, request.BatchNumber);
            }
            else
            {
                _logger.LogWarning("🚨 [STARTINDEX-DEBUG] NO StartIndex found in PaginationMetadata for {EntityType} batch {BatchNumber} - defaulting to 0",
                    request.EntityType, request.BatchNumber);
            }
            
            if (request.PaginationMetadata.TryGetValue("ChunkSize", out var chunkSizeObj))
            {
                if (chunkSizeObj is System.Text.Json.JsonElement chunkJsonElement)
                {
                    chunkSize = chunkJsonElement.TryGetInt32(out var chunkValue) ? chunkValue : batchSize;
                }
                else if (chunkSizeObj is int directChunkInt)
                {
                    chunkSize = directChunkInt;
                }
                else if (int.TryParse(chunkSizeObj?.ToString(), out var parsedChunkInt))
                {
                    chunkSize = parsedChunkInt;
                }
            }
            
            // Calculate the correct page for this chunk (using consistent fetch batch size)
            var calculatedPage = (startIndex / methodConfig.FetchBatchSize) + 1; // Using methodConfig.FetchBatchSize for consistency (already 1-based)
            pageNumber = calculatedPage;
            // 🚨 CRITICAL FIX: Always use consistent limit regardless of chunk size
            // BigCommerce API pagination results change when limit changes, causing overlaps
            batchSize = methodConfig.FetchBatchSize; // ✅ FIXED: Use configurable consistent fetch batch size
            
            _logger.LogInformation("🔍 [FETCH-CHUNKED] Page calculation: StartIndex={StartIndex} ÷ {FetchBatchSize} + 1 = Page={CalculatedPage}, ChunkSize={ChunkSize}", 
                startIndex, methodConfig.FetchBatchSize, calculatedPage, chunkSize);
        }
        
        _logger.LogDebug("🔧 [FETCH] Using page size {PageSize} for {EntityType} (ChunkedRequest: {IsChunked}, StartIndex: {StartIndex}, ChunkSize: {ChunkSize})", 
            batchSize, request.EntityType, isChunkedRequest, startIndex, chunkSize);
        
        _logger.LogInformation("🔧 [FETCH-FINAL] Page={PageNumber}, Limit={Limit}, BatchNumber={BatchNumber} for {EntityType} in migration {MigrationId}", 
            pageNumber, batchSize, request.BatchNumber, request.EntityType, request.MigrationId);
        
        _logger.LogInformation("🚀 [FETCH-API-CALL] Fetching page {PageNumber} for {EntityType} using direct pagination (limit={Limit}) in migration {MigrationId}", 
            pageNumber, request.EntityType, batchSize, request.MigrationId);

        try
        {
            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = pageNumber,
                Limit = batchSize,
                SortBy = "id",
                SortDirection = "asc"
            };

            // Add entity-specific parameters
            
            // ✅ COMPONENT HANDLING: Individual component types fetch products with includes
            string actualEntityType = request.EntityType;
            var componentTypes = new[] {"options", "modifiers", "reviews" };
            if (componentTypes.Contains(request.EntityType.ToLowerInvariant()))
            {
                _logger.LogInformation("🔗 [COMPONENT-FETCH] Individual component '{ComponentType}' - fetching products with includes", 
                    request.EntityType);
                
                actualEntityType = "products"; // Fetch products instead of the component type
                paginationRequest.Include = "options,modifiers,reviews"; // Include all components
                paginationRequest.Limit = 10; // BigCommerce limitation with includes
                
                _logger.LogDebug("🔗 [COMPONENT-FETCH] Modified fetch: EntityType=products, Include={Include}, Limit={Limit}", 
                    paginationRequest.Include, paginationRequest.Limit);
            }
            else
            {
                // ✅ ENHANCED PRODUCTS: Add include parameter from configuration for additional entity data
                if (!string.IsNullOrEmpty(config.Include))
                {
                    paginationRequest.Include = config.Include;
                    _logger.LogDebug("🔗 [ENHANCED-FETCH] Using include parameter from config for {EntityType}: {Include}", 
                        request.EntityType, config.Include);
                }
            }

            var response = await _apiClient.GetPaginatedEntitiesAsync(
                request.SourceStore,
                actualEntityType,
                paginationRequest,
                cancellationToken);

            var entities = response.Data ?? new List<Dictionary<string, object>>();
            
            _logger.LogInformation("✅ [FETCH-RESPONSE] API returned {Count} entities from page {PageNumber} (requested limit={Limit}) for {EntityType} in migration {MigrationId}", 
                entities.Count, pageNumber, batchSize, request.EntityType, request.MigrationId);
            
                            // 🚨 DEBUG: Log ALL brand IDs and names with chunk/batch tracking
                if (request.EntityType.Equals("brands", StringComparison.OrdinalIgnoreCase) && entities.Count > 0)
                {
                    _logger.LogInformation("📦 [CHUNK-FETCH] === FETCHED BRANDS FROM CHUNK {BatchNumber} (PAGE {PageNumber}) ===", 
                        request.BatchNumber, pageNumber);
                    
                    for (int i = 0; i < entities.Count; i++)
                    {
                        var entity = entities[i];
                        var id = entity.TryGetValue("id", out var idVal) ? idVal?.ToString() : "unknown";
                        var name = entity.TryGetValue("name", out var nameVal) ? nameVal?.ToString() : "unknown";
                        
                        _logger.LogInformation("📦 [CHUNK-FETCH] Brand {Index}/{Total}: ID={BrandId}, Name='{BrandName}' (Chunk={ChunkNumber}, Page={PageNumber})", 
                            i + 1, entities.Count, id, name, request.BatchNumber, pageNumber);
                    }
                    
                    _logger.LogInformation("📦 [CHUNK-FETCH] === END CHUNK {BatchNumber} - FETCHED {Count} BRANDS ===", 
                        request.BatchNumber, entities.Count);
                }
            
                            // Add original entity ID tracking for error reporting and chunk tracking
                foreach (var entity in entities)
                {
                    var entityId = entity.TryGetValue("id", out var id) ? id.ToString() : null;
                    entity["_original_entity_id"] = entityId;
                    entity["_chunk_number"] = request.BatchNumber; // Track which chunk this entity came from
                    entity["_api_page"] = pageNumber; // Track which API page this entity came from
                    entity["_migration_id"] = request.MigrationId; // Track migration ID for debugging
                }
            
            // 🚨 DEDUPLICATION: Remove duplicate brand names from same batch to prevent 409 conflicts
            if (request.EntityType.Equals("brands", StringComparison.OrdinalIgnoreCase))
            {
                var originalCount = entities.Count;
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var deduplicatedEntities = new List<Dictionary<string, object>>();
                
                foreach (var entity in entities)
                {
                    var brandName = entity.TryGetValue("name", out var name) ? name?.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(brandName) && seenNames.Add(brandName))
                    {
                        deduplicatedEntities.Add(entity);
                    }
                    else if (!string.IsNullOrWhiteSpace(brandName))
                    {
                        _logger.LogWarning("🔍 [FETCH-DEDUP] Removing duplicate brand '{BrandName}' from page {PageNumber} (keeping first occurrence)", 
                            brandName, pageNumber);
                    }
                }
                
                if (deduplicatedEntities.Count != originalCount)
                {
                    _logger.LogInformation("🔍 [FETCH-DEDUP] Deduplicated brands: {OriginalCount} → {DeduplicatedCount} (removed {RemovedCount} within-page duplicates)", 
                        originalCount, deduplicatedEntities.Count, originalCount - deduplicatedEntities.Count);
                    entities = deduplicatedEntities;
                }
            }

            // Log first and last entity IDs for debugging page boundaries
            if (entities.Count > 0)
            {
                var firstId = entities.First().TryGetValue("id", out var firstIdVal) ? firstIdVal.ToString() : "unknown";
                var lastId = entities.Last().TryGetValue("id", out var lastIdVal) ? lastIdVal.ToString() : "unknown";
                _logger.LogInformation("🔍 [FETCH-BOUNDARIES] Page {PageNumber}: First ID={FirstId}, Last ID={LastId}, Count={Count}", 
                    pageNumber, firstId, lastId, entities.Count);
            }

            // 🚨 CRITICAL FIX: For chunked requests, limit results to expected chunk size
            // We always fetch with limit=50 for consistency, but chunk might expect fewer
            if (isChunkedRequest && chunkSize < entities.Count)
            {
                _logger.LogInformation("🔧 [FETCH-CHUNK-LIMIT] Limiting results from {FetchedCount} to {ExpectedChunkSize} for pagination consistency", 
                    entities.Count, chunkSize);
                entities = entities.Take(chunkSize).ToList();
            }

            _logger.LogInformation("✅ [FETCH-COMPLETE] Direct pagination fetched {Count} {EntityType} entities from page {PageNumber} in migration {MigrationId}", 
                entities.Count, request.EntityType, pageNumber, request.MigrationId);

            return entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch {EntityType} entities using direct pagination (page {PageNumber}) in migration {MigrationId}", 
                request.EntityType, pageNumber, request.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Gets cached entities for the current batch from pre-loaded entity data
    /// Used for hierarchical strategies that cache all entity data during discovery
    /// </summary>
    private List<Dictionary<string, object>> GetCachedEntitiesForBatch(BatchProcessingRequest request)
    {
        if (request.CachedEntityData == null || !request.CachedEntityData.Any())
        {
            _logger.LogWarning("Cached entity data is null or empty for {EntityType} batch {BatchNumber} in migration {MigrationId}", 
                request.EntityType, request.BatchNumber, request.MigrationId);
            return new List<Dictionary<string, object>>();
        }

        // ✅ FIX: For hierarchical entities like categories, preserve the hierarchical order from cached data
        // Instead of filtering by EntityIds (which may not be hierarchically sorted), 
        // use the cached data order which IS hierarchically sorted by V3HierarchicalStrategy
        if (request.EntityType.Equals("categories", StringComparison.OrdinalIgnoreCase))
        {
            // Filter cached data to get only entities for this batch, preserving hierarchical order
            var batchEntities = request.CachedEntityData
                .Where(entity =>
                {
                    var entityId = entity.TryGetValue("category_id", out var id) ? id.ToString() : null;
                    return entityId != null && request.EntityIds.Contains(entityId);
                })
                .ToList(); // Preserves the hierarchical order from cached data
            
            _logger.LogInformation("🔍 DEBUG: Retrieved {Count} cached {EntityType} entities in hierarchical order for batch {BatchNumber} in migration {MigrationId}", 
                batchEntities.Count, request.EntityType, request.BatchNumber, request.MigrationId);
            
            // ✅ DEBUG: Log the hierarchical processing order
            foreach (var entity in batchEntities)
            {
                var categoryId = entity.TryGetValue("category_id", out var id) ? id.ToString() : "UNKNOWN";
                var categoryName = entity.TryGetValue("name", out var name) ? name.ToString() : "UNKNOWN";
                var parentId = entity.TryGetValue("parent_id", out var parent) ? parent.ToString() : "UNKNOWN";
                _logger.LogInformation("🔍 DEBUG: - Hierarchical order: Category {CategoryId} ({CategoryName}), Parent: {ParentId}", 
                    categoryId, categoryName, parentId);
            }

            return batchEntities;
        }
        
        // For non-hierarchical entities, use the original logic
        var standardBatchEntities = request.CachedEntityData
            .Where(entity =>
            {
                var entityId = entity.TryGetValue("id", out var id) ? id.ToString() : null;
                return entityId != null && request.EntityIds.Contains(entityId);
            })
            .ToList();

        _logger.LogDebug("Retrieved {Count} cached {EntityType} entities for batch {BatchNumber} in migration {MigrationId}", 
            standardBatchEntities.Count, request.EntityType, request.BatchNumber, request.MigrationId);

        return standardBatchEntities;
    }

    /// <summary>
    /// Calculates the depth of a category in the hierarchy (root = 0, first level = 1, etc.)
    /// </summary>
    private int CalculateCategoryDepth(Dictionary<string, object> category, Dictionary<string, Dictionary<string, object>> categoryLookup, HashSet<string> visited)
    {
        var categoryId = category.TryGetValue("id", out var id) ? id.ToString()! : string.Empty;
        
        // Prevent infinite loops from circular references
        if (visited.Contains(categoryId))
        {
            _logger.LogWarning("Circular reference detected for category {CategoryId}", categoryId);
            return 0; // Treat as root category
        }
        
        visited.Add(categoryId);

        // Check if this is a root category
        if (!category.TryGetValue("parent_id", out var parentId) || 
            parentId == null || 
            parentId.ToString() == "0" || 
            string.IsNullOrEmpty(parentId.ToString()))
        {
            return 0; // Root category
        }

        var parentIdString = parentId.ToString()!;
        
        // Check if parent exists in our category set
        if (!categoryLookup.TryGetValue(parentIdString, out var parentCategory))
        {
            _logger.LogDebug("Parent category {ParentId} not found for category {CategoryId}, treating as root", 
                parentIdString, categoryId);
            return 0; // Treat as root if parent not found
        }

        // Recursive depth calculation
        return 1 + CalculateCategoryDepth(parentCategory, categoryLookup, visited);
    }

    /// <summary>
    /// 🆕 RANGE-BASED FETCH: Fetches entities from EntityMappings table using efficient RowNumber range queries
    /// Eliminates memory-intensive streaming by using direct range queries based on batch number
    /// 
    /// DESIGN: Uses RowNumber-based range queries for true parallel processing
    /// - Batch 0: RowNumbers 1-250 (direct range query)  
    /// - Batch 1: RowNumbers 251-500 (direct range query)
    /// - Batch 2: RowNumbers 501-750 (direct range query)
    /// 
    /// PERFORMANCE: Each batch only reads its specific range - no streaming, no memory accumulation
    /// MEMORY EFFICIENCY: Constant memory usage regardless of total dataset size
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchEntitiesFromEntityMappingsAsync(
        BatchProcessingRequest request, 
        CancellationToken cancellationToken)
    {
        try
        {
            // 🚫 CANCELLATION: Check at start of fetch operation
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("🔍 [RANGE-FETCH] Starting RowNumber range query for {EntityType} batch {BatchNumber} in migration {MigrationId}", 
                request.EntityType, request.BatchNumber, request.MigrationId);
                
            // ✅ DEBUG: Log ChannelMapping availability for product-channel-assign
            if (request.EntityType.Equals("product-channel-assign", StringComparison.OrdinalIgnoreCase))
            {
                var hasChannelMapping = request.AdditionalData?.ContainsKey("ChannelMapping") == true;
                _logger.LogInformation("🔗 [FETCH-DEBUG] ChannelMapping available in AdditionalData: {HasMapping} for {EntityType} batch {BatchNumber}", 
                    hasChannelMapping, request.EntityType, request.BatchNumber);
                    
                if (hasChannelMapping)
                {
                    var channelMappingJson = request.AdditionalData["ChannelMapping"]?.ToString();
                    _logger.LogInformation("🔗 [FETCH-DEBUG] ChannelMapping JSON: {ChannelMappingJson}", channelMappingJson);
                }
            }

            // Get configuration for batch size
            var config = _configService.GetConfiguration(request.EntityType);
            var pageSize = config.FetchBatchSize; // Use configured batch size
            
            // 🆕 CALCULATE ROWNUMBER RANGE: Direct mapping from batch number to RowNumber range
            var startRowNumber = (request.BatchNumber * pageSize) + 1;
            var endRowNumber = startRowNumber + pageSize - 1;
            
            _logger.LogInformation("🔍 [RANGE-FETCH] RowNumber range calculation: Batch {BatchNumber} → RowNumbers {StartRow}-{EndRow} (PageSize={PageSize})", 
                request.BatchNumber, startRowNumber, endRowNumber, pageSize);

            // ✅ CORRECT APPROACH: No data field filtering at fetch level
            // Transform phase will handle filtering and return appropriate skip statuses
            _logger.LogInformation("🔍 [RANGE-FETCH] Retrieving ALL EntityMappings in range - transform phase will handle filtering", 
                request.EntityType);
                
            // ✅ DEBUG: Log detailed range query parameters
            _logger.LogInformation("📊 [FETCH-DEBUG] Range query details: MigrationId={MigrationId}, EntityType={EntityType}, StartRow={StartRow}, EndRow={EndRow}, NoFilter=AllEntities", 
                request.MigrationId, request.EntityType, startRowNumber, endRowNumber);

            // 🚀 EFFICIENT RANGE QUERY: Single direct query - NO STREAMING, NO FILTERING
            var mappings = await _entityMappingsPaginationService.GetEntityMappingsByRowNumberRangeAsync(
                request.MigrationId,
                "products", // All these entity types query products EntityMappings
                startRowNumber,
                endRowNumber,
                null, // ✅ NO FILTER - let transform phase handle filtering
                cancellationToken);

            _logger.LogInformation("✅ [RANGE-FETCH] Range query completed: Retrieved {Count} EntityMappings for RowNumbers {StartRow}-{EndRow}", 
                mappings.Count, startRowNumber, endRowNumber);

            // Convert EntityMappings to Dictionary format expected by transform pipeline
            var entities = new List<Dictionary<string, object>>();
            var processableEntities = 0;
            var skippedEntities = 0;
            
            foreach (var mapping in mappings)
            {
                // Create base entity dictionary
                var entity = new Dictionary<string, object>
                {
                    ["SourceId"] = mapping.SourceId ?? "",
                    ["DestinationId"] = mapping.DestinationId ?? "",
                    ["EntityType"] = mapping.EntityType ?? "",
                    ["MigrationId"] = mapping.MigrationId ?? ""
                };

                // Add entity-type specific data and determine processing status
                // ✅ CORRECT APPROACH: Populate ALL data fields, let transform phase handle filtering
                switch (request.EntityType.ToLowerInvariant())
                {
                    case "product-related":
                        entity["RelatedProductsData"] = mapping.RelatedProductsData ?? "";
                        processableEntities++; // Count all entities, transform will handle skipping
                        break;

                    case "product-channel-assign":
                        entity["ChannelsData"] = mapping.ChannelsData ?? "";
                        
                        // ✅ ALWAYS inject ChannelMapping for product-channel-assign entities
                        var channelMappingJson = "[]"; // Default to empty
                        if (request.AdditionalData?.TryGetValue("ChannelMapping", out var channelMappingObj) == true)
                        {
                            channelMappingJson = channelMappingObj?.ToString() ?? "[]";
                            _logger.LogDebug("🔗 [CHANNEL-MAPPING] Injected ChannelMapping configuration for product {SourceId}: ChannelsData='{ChannelsData}', Mapping={Mapping}", 
                                mapping.SourceId, mapping.ChannelsData ?? "NULL", channelMappingJson);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ [CHANNEL-MAPPING] No ChannelMapping found in AdditionalData for migration {MigrationId}, using empty mapping", 
                                request.MigrationId);
                        }
                        
                        entity["_channel_mapping"] = channelMappingJson;
                        processableEntities++; // Count all entities, transform will handle skipping based on ChannelsData
                        break;

                    case "product-images":
                        // For product-images, we don't store image data in EntityMappings
                        // The ProductImagesCreationStrategy will fetch images dynamically from source store
                        // We just need to ensure the product mapping exists (SourceId/DestinationId)
                        if (!string.IsNullOrEmpty(mapping.SourceId) && !string.IsNullOrEmpty(mapping.DestinationId))
                        {
                            processableEntities++;
                            _logger.LogDebug("✅ [RANGE-FETCH] Product {SourceId} → {DestinationId} is processable for product-images (migration: {MigrationId})",
                                mapping.SourceId, mapping.DestinationId, request.MigrationId);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ [RANGE-FETCH] Skipping product with missing SourceId or DestinationId for product-images (migration: {MigrationId})",
                                request.MigrationId);
                        }
                        break;

                    default:
                        _logger.LogWarning("⚠️ [RANGE-FETCH] Unknown entity type for EntityMappings fetch: {EntityType}", request.EntityType);
                        processableEntities++;
                        break;
                }

                entities.Add(entity);
            }

            _logger.LogInformation("✅ [RANGE-FETCH] Successfully converted {TotalCount} EntityMappings: {ProcessableCount} entities sent to transform (filtering handled by transform phase) for {EntityType} batch {BatchNumber}", 
                entities.Count, processableEntities, request.EntityType, request.BatchNumber);
                
            // ✅ DEBUG: Log sample EntityMapping data for debugging
            if (entities.Any())
            {
                var sampleEntity = entities.First();
                var sampleKeys = string.Join(", ", sampleEntity.Keys);
                _logger.LogDebug("📊 [FETCH-DEBUG] Sample entity keys: [{SampleKeys}]", sampleKeys);
                
                if (request.EntityType.Equals("product-channel-assign", StringComparison.OrdinalIgnoreCase))
                {
                    var sampleChannelsData = sampleEntity.TryGetValue("ChannelsData", out var cd) ? cd?.ToString() : "NULL";
                    var sampleChannelMapping = sampleEntity.TryGetValue("_channel_mapping", out var cm) ? cm?.ToString() : "NULL"; 
                    _logger.LogInformation("📊 [FETCH-DEBUG] Sample product-channel-assign: ChannelsData='{ChannelsData}', ChannelMapping='{ChannelMapping}'", 
                        sampleChannelsData, sampleChannelMapping);
                }
            }

            return entities;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🚫 [ENTITY-MAPPINGS-FETCH-CANCEL] EntityMappings fetch cancelled for batch {BatchNumber} in migration {MigrationId}", 
                request.BatchNumber, request.MigrationId);
            throw; // Re-throw to propagate cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [ENTITY-MAPPINGS-FETCH-ERROR] Failed to fetch entities from EntityMappings for product-related batch {BatchNumber} in migration {MigrationId}", 
                request.BatchNumber, request.MigrationId);
            
            // Return empty list to allow pipeline to continue
            return new List<Dictionary<string, object>>();
        }
    }

    #endregion
} 
