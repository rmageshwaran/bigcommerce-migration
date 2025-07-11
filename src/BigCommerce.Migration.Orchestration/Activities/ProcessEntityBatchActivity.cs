using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using System.Diagnostics;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for processing entity batches with rate limiting and error handling
/// Implements user requirements: Continue on failures, log all errors at sub-component level, 12 req/sec rate limiting
/// </summary>
public class ProcessEntityBatchActivity
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<ProcessEntityBatchActivity> _logger;
    private readonly IRateLimitService _rateLimitService;
    private readonly IOpenSearchService _openSearchService;
    private readonly IMigrationStorageService _migrationStorageService;

    // Supported entity types for validation
    private static readonly string[] SupportedEntityTypes = new[]
    {
        "categories", "products", "brands", "variants", "images", "modifiers"
    };

    public ProcessEntityBatchActivity(
        IBigCommerceApiClient apiClient,
        ILogger<ProcessEntityBatchActivity> logger,
        IRateLimitService rateLimitService,
        IOpenSearchService openSearchService,
        IMigrationStorageService migrationStorageService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
    }

    /// <summary>
    /// Processes a batch of entities from source to destination store
    /// </summary>
    /// <param name="request">Batch processing request</param>
    /// <returns>Batch processing result</returns>
    [Function("ProcessEntityBatch")]
    public async Task<BatchProcessingResult> ProcessEntityBatchAsync([ActivityTrigger] BatchProcessingRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        var result = new BatchProcessingResult
        {
            BatchNumber = request.BatchNumber,
            TotalProcessed = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            EntityMappings = new List<EntityMapping>(),
            Errors = new List<string>()
        };

        try
        {
            _logger.LogInformation("Starting batch processing for {EntityType} batch {BatchNumber}/{TotalBatches} in migration {MigrationId}",
                request.EntityType, request.BatchNumber, request.TotalBatches, request.MigrationId);

            // Step 1: Validate request
            var validationErrors = ValidateRequest(request);
            if (validationErrors.Any())
            {
                result.Errors.AddRange(validationErrors);
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
                return result;
            }

            // Step 2: Handle empty batch
            if (!request.EntityIds.Any())
            {
                _logger.LogInformation("Empty batch for {EntityType} batch {BatchNumber} - skipping processing",
                    request.EntityType, request.BatchNumber);
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
                return result;
            }

            // Step 3: Check entity type support
            if (!SupportedEntityTypes.Contains(request.EntityType.ToLowerInvariant()))
            {
                result.Errors.Add($"Unsupported entity type: {request.EntityType}");
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
                return result;
            }

            // Step 4: Fetch source entities
            List<Dictionary<string, object>> sourceEntities;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourceEntities = await FetchSourceEntitiesAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Migration batch {BatchNumber} for {EntityType} was cancelled",
                    request.BatchNumber, request.EntityType);
                result.Errors.Add("Migration was cancelled");
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
                return result;
            }
            catch (Exception ex)
            {
                // Log the error and preserve the original exception message
                _logger.LogError(ex, "Failed to fetch source entities for {EntityType} in migration {MigrationId}",
                    request.EntityType, request.MigrationId);
                
                var errorMessage = !string.IsNullOrEmpty(ex.Message) ? ex.Message : "Failed to fetch source entities";
                result.Errors.Add(errorMessage);
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
                return result;
            }

            // Step 5: Process entities individually (continue on failures as per user requirement)
            await ProcessEntitiesIndividuallyAsync(request, sourceEntities, result, cancellationToken);

            // Step 6: Store entity mappings if any were created
            if (result.EntityMappings.Any())
            {
                await _migrationStorageService.StoreEntityMappingsAsync(result.EntityMappings);
            }

            // Step 7: Log batch completion to OpenSearch
            await LogBatchProcessingAsync(request, result, cancellationToken);

            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;

            _logger.LogInformation("Completed batch processing for {EntityType} batch {BatchNumber}: {SuccessfulEntities} successful, {FailedEntities} failed",
                request.EntityType, request.BatchNumber, result.SuccessfulEntities, result.FailedEntities);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing batch {BatchNumber} for {EntityType} in migration {MigrationId}",
                request.BatchNumber, request.EntityType, request.MigrationId);
            
            result.Errors.Add($"Batch processing failed: {ex.Message}");
            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;
            return result;
        }
    }

    /// <summary>
    /// Validates the batch processing request
    /// </summary>
    /// <param name="request">Request to validate</param>
    /// <returns>List of validation errors</returns>
    private static List<string> ValidateRequest(BatchProcessingRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.MigrationId))
            errors.Add("MigrationId is required");

        if (string.IsNullOrWhiteSpace(request.EntityType))
            errors.Add("EntityType is required");

        if (request.BatchNumber < 1)
            errors.Add("BatchNumber must be greater than 0");

        if (request.TotalBatches < 1)
            errors.Add("TotalBatches must be greater than 0");

        if (request.SourceStore == null || !request.SourceStore.IsValid())
            errors.Add("Valid SourceStore is required");

        if (request.DestinationStore == null || !request.DestinationStore.IsValid())
            errors.Add("Valid DestinationStore is required");

        return errors;
    }

    /// <summary>
    /// Fetches source entities from the source store
    /// </summary>
    /// <param name="request">Batch processing request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of source entities</returns>
    private async Task<List<Dictionary<string, object>>> FetchSourceEntitiesAsync(BatchProcessingRequest request, CancellationToken cancellationToken)
    {
        // Apply rate limiting before API call
        if (!string.IsNullOrEmpty(request.SourceStore?.StoreId))
        {
            await ApplyRateLimitingAsync(request.SourceStore.StoreId);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return request.EntityType.ToLowerInvariant() switch
        {
            "categories" => await FetchCategoriesAsync(request, cancellationToken),
            "products" => await FetchProductsAsync(request, cancellationToken),
            "brands" => await FetchBrandsAsync(request, cancellationToken),
            "variants" => await FetchVariantsAsync(request, cancellationToken),
            "images" => await FetchImagesAsync(request, cancellationToken),
            "modifiers" => await FetchModifiersAsync(request, cancellationToken),
            _ => throw new ArgumentException($"Unsupported entity type: {request.EntityType}")
        };
    }

    /// <summary>
    /// Fetches categories from source store with enhanced validation and error handling
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchCategoriesAsync(BatchProcessingRequest request, CancellationToken cancellationToken)
    {
        // Validate category tree context
        if (request.CategoryTreeContext == null)
        {
            throw new InvalidOperationException("CategoryTreeContext is required for category migration");
        }

        var sourceTreeId = request.CategoryTreeContext.SourceCategoryTreeId;
        if (string.IsNullOrEmpty(sourceTreeId))
        {
            throw new InvalidOperationException("SourceCategoryTreeId is required for category migration");
        }

        var destinationTreeId = request.CategoryTreeContext.DestinationCategoryTreeId;
        if (string.IsNullOrEmpty(destinationTreeId))
        {
            throw new InvalidOperationException("DestinationCategoryTreeId is required for category migration");
        }

        try
        {
            _logger.LogDebug("Fetching categories from source store {SourceStore} tree {SourceTreeId} for migration {MigrationId}",
                request.SourceStore.StoreId, sourceTreeId, request.MigrationId);

            var categories = await _apiClient.GetCategoriesAsync(request.SourceStore, sourceTreeId, cancellationToken);

            _logger.LogInformation("Successfully fetched {CategoryCount} categories from source store {SourceStore} for migration {MigrationId}",
                categories.Count, request.SourceStore.StoreId, request.MigrationId);

            // Filter categories to only those in the requested EntityIds if specified
            if (request.EntityIds != null && request.EntityIds.Any())
            {
                var filteredCategories = categories.Where(c => 
                    c.TryGetValue("id", out var id) && 
                    id != null &&
                    request.EntityIds.Contains(id.ToString()!)).ToList();

                _logger.LogDebug("Filtered categories from {TotalCount} to {FilteredCount} based on EntityIds for migration {MigrationId}",
                    categories.Count, filteredCategories.Count, request.MigrationId);

                return filteredCategories;
            }

            return categories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch categories from source store {SourceStore} tree {SourceTreeId} for migration {MigrationId}",
                request.SourceStore.StoreId, sourceTreeId, request.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Fetches products from source store
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchProductsAsync(BatchProcessingRequest request, CancellationToken cancellationToken)
    {
        if (request.EntityIds == null || !request.EntityIds.Any())
        {
            return new List<Dictionary<string, object>>();
        }

        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = request.EntityIds.Count,
            IncludeDeleted = false,
            IncludeDrafts = true
        };

        var response = await _apiClient.GetPaginatedEntitiesAsync(
            request.SourceStore, 
            "products", 
            paginationRequest, 
            cancellationToken);

        // Filter to only the requested entity IDs
        return response.Data?.Where(p => 
            p.TryGetValue("id", out var id) && 
            id != null &&
            request.EntityIds.Contains(id.ToString()!)).ToList() ?? new List<Dictionary<string, object>>();
    }

    /// <summary>
    /// Fetches brands from source store
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchBrandsAsync(BatchProcessingRequest request, CancellationToken cancellationToken)
    {
        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = request.EntityIds.Count,
            IncludeDeleted = false
        };

        var response = await _apiClient.GetPaginatedEntitiesAsync(
            request.SourceStore,
            "brands",
            paginationRequest,
            cancellationToken);

        return response.Data.Where(b => 
            b.TryGetValue("id", out var id) && 
            id != null &&
            request.EntityIds.Contains(id.ToString()!)).ToList();
    }

    /// <summary>
    /// Fetches variants from source store
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchVariantsAsync(BatchProcessingRequest request, CancellationToken cancellationToken)
    {
        // For variants, we need to fetch from product-specific endpoints
        // This is a simplified implementation - in reality, you'd need product IDs
        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = request.EntityIds.Count
        };

        var response = await _apiClient.GetPaginatedEntitiesAsync(
            request.SourceStore,
            "variants",
            paginationRequest,
            cancellationToken);

        return response.Data.Where(v => 
            v.TryGetValue("id", out var id) && 
            id != null &&
            request.EntityIds.Contains(id.ToString()!)).ToList();
    }

    /// <summary>
    /// Fetches images from source store
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchImagesAsync(BatchProcessingRequest request, CancellationToken cancellationToken)
    {
        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = request.EntityIds.Count
        };

        var response = await _apiClient.GetPaginatedEntitiesAsync(
            request.SourceStore,
            "images",
            paginationRequest,
            cancellationToken);

        return response.Data.Where(i => 
            i.TryGetValue("id", out var id) && 
            id != null &&
            request.EntityIds.Contains(id.ToString()!)).ToList();
    }

    /// <summary>
    /// Fetches modifiers from source store
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchModifiersAsync(BatchProcessingRequest request, CancellationToken cancellationToken)
    {
        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = request.EntityIds.Count
        };

        var response = await _apiClient.GetPaginatedEntitiesAsync(
            request.SourceStore,
            "modifiers",
            paginationRequest,
            cancellationToken);

        return response.Data.Where(m => 
            m.TryGetValue("id", out var id) && 
            id != null &&
            request.EntityIds.Contains(id.ToString()!)).ToList();
    }

    /// <summary>
    /// Processes entities individually, continuing on failures as per user requirement
    /// </summary>
    /// <param name="request">Batch processing request</param>
    /// <param name="sourceEntities">Source entities to process</param>
    /// <param name="result">Result object to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task ProcessEntitiesIndividuallyAsync(
        BatchProcessingRequest request, 
        List<Dictionary<string, object>> sourceEntities, 
        BatchProcessingResult result,
        CancellationToken cancellationToken)
    {
        // Track entities created successfully for proper mapping
        var destinationEntityIndex = 0;
        List<Dictionary<string, object>>? allCreatedEntities = null;

        for (int i = 0; i < sourceEntities.Count; i++)
        {
            // Check for cancellation before processing each entity
            cancellationToken.ThrowIfCancellationRequested();
            
            var entity = sourceEntities[i];
            result.TotalProcessed++;
            
            try
            {
                // Apply rate limiting before each entity creation
                await ApplyRateLimitingAsync(request.DestinationStore.StoreId!);

                // Transform entity for destination store
                var transformedEntity = await TransformEntityAsync(entity, request);

                // Create entity in destination store
                var createdEntities = await CreateEntitiesInDestinationAsync(new List<Dictionary<string, object>> { transformedEntity }, request, cancellationToken);

                if (createdEntities != null && createdEntities.Any())
                {
                    // Store all created entities for mapping (handles batch creation)
                    if (allCreatedEntities == null)
                    {
                        allCreatedEntities = new List<Dictionary<string, object>>(createdEntities);
                    }
                    else
                    {
                        allCreatedEntities.AddRange(createdEntities);
                    }

                    result.SuccessfulEntities++;
                    
                    // Get the corresponding destination entity (either single or from batch)
                    var createdEntity = createdEntities.Count > 1 && destinationEntityIndex < createdEntities.Count 
                        ? createdEntities[destinationEntityIndex] 
                        : createdEntities.First();

                    // Create entity mapping
                    var mapping = CreateEntityMapping(entity, createdEntity, request);
                    result.EntityMappings.Add(mapping);

                    _logger.LogDebug("Successfully processed {EntityType} {EntityId} in migration {MigrationId}",
                        request.EntityType, entity["id"], request.MigrationId);
                        
                    destinationEntityIndex++;
                }
                else
                {
                    result.FailedEntities++;
                    var error = $"Failed to create {request.EntityType} {entity["id"]}: No entity returned";
                    result.Errors.Add(error);
                    _logger.LogWarning(error);
                }
            }
            catch (Exception ex)
            {
                // Continue processing on failures (user requirement)
                result.FailedEntities++;
                var entityId = entity.TryGetValue("id", out var id) ? id.ToString() : "unknown";
                var error = $"Failed to process {request.EntityType} {entityId}: {ex.Message}";
                result.Errors.Add(error);
                
                _logger.LogWarning(ex, "Failed to process {EntityType} {EntityId} in migration {MigrationId}",
                    request.EntityType, entityId, request.MigrationId);
            }
        }
    }

    /// <summary>
    /// Transforms source entity for destination store
    /// </summary>
    /// <param name="sourceEntity">Source entity</param>
    /// <param name="request">Batch processing request</param>
    /// <returns>Transformed entity</returns>
    private async Task<Dictionary<string, object>> TransformEntityAsync(
        Dictionary<string, object> sourceEntity, 
        BatchProcessingRequest request)
    {
        // Create a copy of the entity for transformation
        var transformedEntity = new Dictionary<string, object>(sourceEntity);

        // Remove source-specific fields that shouldn't be migrated
        transformedEntity.Remove("id");
        transformedEntity.Remove("date_created");
        transformedEntity.Remove("date_modified");

        // Apply entity-specific transformations
        switch (request.EntityType.ToLowerInvariant())
        {
            case "categories":
                await TransformCategoryAsync(transformedEntity, request);
                break;
            case "products":
                await TransformProductAsync(transformedEntity, request);
                break;
            case "brands":
                await TransformBrandAsync(transformedEntity, request);
                break;
            // Add other entity types as needed
        }

        return transformedEntity;
    }

    /// <summary>
    /// Transforms category for destination store with enhanced hierarchical support
    /// </summary>
    private async Task TransformCategoryAsync(Dictionary<string, object> category, BatchProcessingRequest request)
    {
        // Step 1: Handle parent category mapping for hierarchical relationships
        if (category.TryGetValue("parent_id", out var parentId) && 
            parentId != null && 
            parentId.ToString() != "0" && 
            int.TryParse(parentId.ToString(), out var parentIdInt) && 
            parentIdInt > 0)
        {
            var parentMapping = await _migrationStorageService.GetEntityMappingAsync(
                request.MigrationId, "categories", parentId.ToString()!);
            
            if (parentMapping != null)
            {
                category["parent_id"] = int.Parse(parentMapping.DestinationId);
                _logger.LogDebug("Mapped parent category {SourceParentId} to {DestinationParentId} for category {CategoryName}",
                    parentId, parentMapping.DestinationId, category.TryGetValue("name", out var name) ? name : "unknown");
            }
            else
            {
                // Parent not found - this could be a hierarchy ordering issue
                _logger.LogWarning("Parent category {ParentId} not found in mappings for category {CategoryName}. Setting as root category.",
                    parentId, category.TryGetValue("name", out var name) ? name : "unknown");
                category["parent_id"] = null; // Make it a root category
            }
        }
        else
        {
            // Root category or invalid parent_id
            category["parent_id"] = null;
        }

        // Step 2: Clean up BigCommerce-specific fields that shouldn't be migrated
        var fieldsToRemove = new[] 
        {
            "id", "date_created", "date_modified", "children", "category_id", 
            "is_visible_in_menu", "depth", "path", "url", "has_children",
            "product_count", "default_product_sort", "category_tree_id"
        };
        
        foreach (var field in fieldsToRemove)
        {
            category.Remove(field);
        }

        // Step 3: Ensure required fields have valid values
        if (!category.ContainsKey("name") || string.IsNullOrWhiteSpace(category["name"]?.ToString()))
        {
            throw new InvalidOperationException("Category name is required and cannot be empty");
        }

        // Step 4: Set default values for optional fields if not present
        if (!category.ContainsKey("sort_order"))
        {
            category["sort_order"] = 0;
        }

        if (!category.ContainsKey("is_visible"))
        {
            category["is_visible"] = true;
        }

        // Step 5: Transform BigCommerce V2 fields to V3 format if needed
        if (category.TryGetValue("description", out var description) && description is string descText)
        {
            // Remove HTML tags for better compatibility (optional enhancement)
            if (descText.Contains("<") && descText.Contains(">"))
            {
                // Basic HTML tag removal (in a real implementation, you might use a proper HTML parser)
                var plainText = System.Text.RegularExpressions.Regex.Replace(descText, "<.*?>", string.Empty);
                category["description"] = plainText.Trim();
            }
        }

        // Step 6: Handle image URLs - ensure they're valid for destination store
        if (category.TryGetValue("image_url", out var imageUrl) && !string.IsNullOrWhiteSpace(imageUrl?.ToString()))
        {
            var imageUrlString = imageUrl.ToString()!;
            
            // If it's a relative URL, we might need to convert it or handle it differently
            if (imageUrlString.StartsWith("/") || (!imageUrlString.StartsWith("http://") && !imageUrlString.StartsWith("https://")))
            {
                _logger.LogDebug("Found relative image URL for category {CategoryName}: {ImageUrl}. " +
                    "This may need manual handling for proper migration.", 
                    category.TryGetValue("name", out var name) ? name : "unknown", imageUrlString);
                // For now, keep the URL as-is, but log it for manual review
            }
        }

        // Step 7: Add migration metadata for tracking
        category["migrated_from_store"] = request.SourceStore.StoreId;
        category["migration_id"] = request.MigrationId;
        category["migrated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        _logger.LogDebug("Successfully transformed category {CategoryName} for migration {MigrationId}",
            category.TryGetValue("name", out var categoryName) ? categoryName : "unknown", request.MigrationId);
    }

    /// <summary>
    /// Transforms product for destination store
    /// </summary>
    private async Task TransformProductAsync(Dictionary<string, object> product, BatchProcessingRequest request)
    {
        // Handle category mappings
        if (product.TryGetValue("categories", out var categories) && categories is List<object> categoryList)
        {
            var mappedCategories = new List<int>();
            
            foreach (var categoryId in categoryList)
            {
                var mapping = await _migrationStorageService.GetEntityMappingAsync(
                    request.MigrationId, "categories", categoryId.ToString()!);
                
                if (mapping != null)
                {
                    mappedCategories.Add(int.Parse(mapping.DestinationId));
                }
            }
            
            product["categories"] = mappedCategories;
        }

        // Add any product-specific transformations here
        await Task.CompletedTask;
    }

    /// <summary>
    /// Transforms brand for destination store
    /// </summary>
    private async Task TransformBrandAsync(Dictionary<string, object> brand, BatchProcessingRequest request)
    {
        // Add any brand-specific transformations here
        await Task.CompletedTask;
    }

    /// <summary>
    /// Creates entities in destination store
    /// </summary>
    /// <param name="entities">Entities to create</param>
    /// <param name="request">Batch processing request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created entities or null if failed</returns>
    private async Task<List<Dictionary<string, object>>?> CreateEntitiesInDestinationAsync(
        List<Dictionary<string, object>> entities, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        return request.EntityType.ToLowerInvariant() switch
        {
            "categories" => await CreateCategoriesAsync(entities, request, cancellationToken),
            "products" => await CreateProductsAsync(entities, request, cancellationToken),
            "brands" => await CreateBrandsAsync(entities, request, cancellationToken),
            "variants" => await CreateVariantsAsync(entities, request, cancellationToken),
            "images" => await CreateImagesAsync(entities, request, cancellationToken),
            "modifiers" => await CreateModifiersAsync(entities, request, cancellationToken),
            _ => throw new ArgumentException($"Unsupported entity type: {request.EntityType}")
        };
    }

    /// <summary>
    /// Creates categories in destination store with enhanced error handling and logging
    /// </summary>
    private async Task<List<Dictionary<string, object>>?> CreateCategoriesAsync(
        List<Dictionary<string, object>> categories, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        var destinationTreeId = request.CategoryTreeContext?.DestinationCategoryTreeId;
        if (string.IsNullOrEmpty(destinationTreeId))
        {
            throw new InvalidOperationException("DestinationCategoryTreeId is required for category creation");
        }

        if (!categories.Any())
        {
            _logger.LogWarning("No categories provided for creation in migration {MigrationId}", request.MigrationId);
            return new List<Dictionary<string, object>>();
        }

        try
        {
            _logger.LogDebug("Creating {CategoryCount} categories in destination store {DestinationStore} tree {DestinationTreeId} for migration {MigrationId}",
                categories.Count, request.DestinationStore.StoreId, destinationTreeId, request.MigrationId);

            // Log category details for debugging
            foreach (var category in categories)
            {
                var categoryName = category.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                var parentId = category.TryGetValue("parent_id", out var parent) ? parent?.ToString() : "null";
                
                _logger.LogDebug("Creating category: Name='{CategoryName}', ParentId={ParentId} in migration {MigrationId}",
                    categoryName, parentId, request.MigrationId);
            }

            var createdCategories = await _apiClient.CreateCategoriesAsync(
                request.DestinationStore, 
                destinationTreeId, 
                categories, 
                cancellationToken);

            if (createdCategories != null && createdCategories.Any())
            {
                _logger.LogInformation("Successfully created {CreatedCount} categories in destination store {DestinationStore} for migration {MigrationId}",
                    createdCategories.Count, request.DestinationStore.StoreId, request.MigrationId);

                // Log created category mappings for debugging
                for (int i = 0; i < Math.Min(categories.Count, createdCategories.Count); i++)
                {
                    var sourceName = categories[i].TryGetValue("name", out var sName) ? sName.ToString() : "unknown";
                    var createdId = createdCategories[i].TryGetValue("id", out var cId) ? cId.ToString() : "unknown";
                    
                    _logger.LogDebug("Created category mapping: '{CategoryName}' -> ID {CreatedId} in migration {MigrationId}",
                        sourceName, createdId, request.MigrationId);
                }
            }
            else
            {
                _logger.LogWarning("Category creation returned no results for migration {MigrationId}", request.MigrationId);
            }

            return createdCategories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create {CategoryCount} categories in destination store {DestinationStore} tree {DestinationTreeId} for migration {MigrationId}",
                categories.Count, request.DestinationStore.StoreId, destinationTreeId, request.MigrationId);

            // Log individual category details for troubleshooting
            foreach (var category in categories)
            {
                var categoryName = category.TryGetValue("name", out var name) ? name.ToString() : "unknown";
                _logger.LogError("Failed category: Name='{CategoryName}', Data={CategoryData}", 
                    categoryName, System.Text.Json.JsonSerializer.Serialize(category));
            }

            throw;
        }
    }

    /// <summary>
    /// Creates products in destination store
    /// </summary>
    private async Task<List<Dictionary<string, object>>?> CreateProductsAsync(
        List<Dictionary<string, object>> products, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        var createdProducts = await _apiClient.CreateProductsAsync(
            request.DestinationStore, 
            products, 
            cancellationToken);

        return createdProducts;
    }

    /// <summary>
    /// Creates brands in destination store
    /// </summary>
    private async Task<List<Dictionary<string, object>>?> CreateBrandsAsync(
        List<Dictionary<string, object>> brands, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        // Note: BigCommerce API client would need CreateBrandsAsync method
        // For now, using a placeholder implementation
        await Task.CompletedTask;
        return brands;
    }

    /// <summary>
    /// Creates variants in destination store
    /// </summary>
    private async Task<List<Dictionary<string, object>>?> CreateVariantsAsync(
        List<Dictionary<string, object>> variants, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        // Placeholder implementation
        await Task.CompletedTask;
        return variants;
    }

    /// <summary>
    /// Creates images in destination store
    /// </summary>
    private async Task<List<Dictionary<string, object>>?> CreateImagesAsync(
        List<Dictionary<string, object>> images, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        // Placeholder implementation
        await Task.CompletedTask;
        return images;
    }

    /// <summary>
    /// Creates modifiers in destination store
    /// </summary>
    private async Task<List<Dictionary<string, object>>?> CreateModifiersAsync(
        List<Dictionary<string, object>> modifiers, 
        BatchProcessingRequest request,
        CancellationToken cancellationToken)
    {
        // Placeholder implementation
        await Task.CompletedTask;
        return modifiers;
    }

    /// <summary>
    /// Creates entity mapping for successful migration
    /// </summary>
    /// <param name="sourceEntity">Source entity</param>
    /// <param name="destinationEntity">Destination entity</param>
    /// <param name="request">Batch processing request</param>
    /// <returns>Entity mapping</returns>
    private static EntityMapping CreateEntityMapping(
        Dictionary<string, object> sourceEntity, 
        Dictionary<string, object> destinationEntity, 
        BatchProcessingRequest request)
    {
        return new EntityMapping
        {
            MigrationId = request.MigrationId,
            EntityType = request.EntityType,
            SourceId = sourceEntity["id"].ToString()!,
            DestinationId = destinationEntity["id"].ToString()!,
            SourceStoreId = request.SourceStore.StoreId!,
            DestinationStoreId = request.DestinationStore.StoreId!,
            Status = "completed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Applies rate limiting as per user requirement (12 req/sec)
    /// </summary>
    /// <param name="storeId">Store ID for rate limiting</param>
    private async Task ApplyRateLimitingAsync(string storeId)
    {
        var rateLimitResult = await _rateLimitService.CheckRateLimitAsync(storeId);
        
        if (rateLimitResult != null && !rateLimitResult.CanProceed && rateLimitResult.DelayMs > 0)
        {
            _logger.LogDebug("Rate limit delay: {DelayMs}ms for store {StoreId}", 
                rateLimitResult.DelayMs, storeId);
            
            await Task.Delay(rateLimitResult.DelayMs);
        }
    }

    /// <summary>
    /// Logs batch processing results to OpenSearch
    /// </summary>
    /// <param name="request">Batch processing request</param>
    /// <param name="result">Batch processing result</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task LogBatchProcessingAsync(BatchProcessingRequest request, BatchProcessingResult result, CancellationToken cancellationToken)
    {
        try
        {
            var logData = new
            {
                migrationId = request.MigrationId,
                entityType = request.EntityType,
                batchNumber = request.BatchNumber,
                totalBatches = request.TotalBatches,
                totalProcessed = result.TotalProcessed,
                successfulEntities = result.SuccessfulEntities,
                failedEntities = result.FailedEntities,
                processingTimeMs = result.ProcessingTime.TotalMilliseconds,
                errors = result.Errors,
                entityMappings = result.EntityMappings.Count
            };

            await _openSearchService.LogEntityBatchProcessingAsync(
                request.MigrationId, 
                request.EntityType, 
                request.BatchNumber, 
                logData,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log batch processing to OpenSearch for migration {MigrationId}",
                request.MigrationId);
        }
    }
} 