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
    [Function("ProcessEntityBatchActivity")]
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

            // Step 4: Check for migration-specific cancellation before fetching
            try
            {
                var migrationCancellationToken = await _migrationStorageService.GetCancellationTokenAsync(request.MigrationId);
                if (migrationCancellationToken != null && !migrationCancellationToken.IsProcessed)
                {
                    _logger.LogInformation("Migration {MigrationId} was cancelled before fetching entities for batch {BatchNumber}",
                        request.MigrationId, request.BatchNumber);
                    result.Errors.Add("Migration was cancelled before fetching entities");
                    stopwatch.Stop();
                    result.ProcessingTime = stopwatch.Elapsed;
                    return result;
                }
            }
            catch (Exception ex)
            {
                // Log error but continue - cancellation check failure shouldn't break migration
                _logger.LogError(ex, "Failed to check migration cancellation status for {MigrationId} before fetching entities", request.MigrationId);
            }

            // Step 5: Fetch source entities
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

            // Step 6: Process entities individually (continue on failures as per user requirement)
            await ProcessEntitiesIndividuallyAsync(request, sourceEntities, result, cancellationToken);

            // Step 6: Store any entity mappings that failed during individual processing (fallback)
            // NOTE: Most mappings should already be stored individually for hierarchy relationships
            _logger.LogDebug("Creating entity mappings batch: {MappingCount} mappings", result.EntityMappings.Count);
            if (result.EntityMappings.Any())
            {
                try
                {
                    await _migrationStorageService.StoreEntityMappingsAsync(result.EntityMappings);
                    _logger.LogInformation("Successfully created {MappingCount} entity mappings", result.EntityMappings.Count);
                }
                catch (Azure.Data.Tables.TableTransactionFailedException ex) when (ex.ErrorCode == "EntityAlreadyExists")
                {
                    // Mappings already exist from individual storage - this is expected and not an error
                    _logger.LogDebug("Entity mappings already exist from individual storage (expected behavior). Mappings: {MappingCount}", result.EntityMappings.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to store {MappingCount} entity mappings as fallback", result.EntityMappings.Count);
                    // Don't fail the entire batch for mapping storage issues
                }
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
    /// Fetches categories from source store
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

            // OPTIMIZATION: Check if we have cached entity data from discovery phase
            if (request.CachedEntityData != null && request.CachedEntityData.Any())
            {
                _logger.LogInformation("Using cached entity data from V3 discovery phase for categories batch {BatchNumber} in migration {MigrationId}",
                    request.BatchNumber, request.MigrationId);

                // Filter cached data to only the entities in this batch
                var filteredCategories = request.CachedEntityData.Where(c => 
                    c.TryGetValue("id", out var id) && 
                    id != null &&
                    request.EntityIds.Contains(id.ToString()!)).ToList();

                _logger.LogDebug("Filtered cached categories from {TotalCount} to {FilteredCount} based on EntityIds for batch {BatchNumber}",
                    request.CachedEntityData.Count, filteredCategories.Count, request.BatchNumber);

                return filteredCategories;
            }

            // FALLBACK: Direct pagination for V2 APIs or when cached data is not available
            _logger.LogInformation("Using direct pagination for categories (V2 API or no cached data) in migration {MigrationId}",
                request.MigrationId);

            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = 1,
                Limit = 250, // Use large limit to minimize pagination
                IncludeDeleted = false,
                CategoryTreeId = sourceTreeId,
                SortBy = "id",
                SortDirection = "asc"
            };

            var allCategories = new List<Dictionary<string, object>>();
            var currentPage = 1;
            int totalPages = 1; // Initialize with default
            int totalFromApi = 0; // Track total from API response

            // Use the same pagination logic as discovery phase
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                paginationRequest.Page = currentPage;
                var response = await _apiClient.GetPaginatedEntitiesAsync(
                    request.SourceStore,
                    "categories",
                    paginationRequest,
                    cancellationToken);

                if (response.Data != null && response.Data.Any())
                {
                    allCategories.AddRange(response.Data);
                    _logger.LogDebug("Added {PageEntities} categories from page {Page} for batch processing", response.Data.Count, currentPage);
                }

                // Track pagination metadata
                totalPages = response.TotalPages ?? 1;
                totalFromApi = response.TotalItems ?? 0;
                currentPage++;

                _logger.LogDebug("Fetched page {Page}/{TotalPages} for category batch processing in migration {MigrationId}. Page entities: {PageEntities}, Total so far: {TotalSoFar}",
                    currentPage - 1, totalPages, request.MigrationId, response.Data?.Count ?? 0, allCategories.Count);

                // For V2 APIs: Break if no more data (more efficient than checking total pages)
                if (response.Data == null || response.Data.Count == 0)
                {
                    _logger.LogDebug("No more data available, breaking pagination loop for V2 API");
                    break;
                }

                // Safety check: prevent infinite loops
                if (currentPage > 50) // Max 50 pages (12,500 categories at 250 per page)
                {
                    _logger.LogWarning("Breaking pagination loop after 50 pages to prevent infinite loop. Current total: {Total}", allCategories.Count);
                    break;
                }

            } while (currentPage <= totalPages);

            _logger.LogInformation("Pagination completed for category batch processing: Fetched {ActualCount} categories (API reported {ApiTotal}) across {PagesProcessed} pages in migration {MigrationId}",
                allCategories.Count, totalFromApi, currentPage - 1, request.MigrationId);

            // Filter categories to only those in the requested EntityIds if specified
            if (request.EntityIds != null && request.EntityIds.Any())
            {
                var filteredCategories = allCategories.Where(c => 
                    c.TryGetValue("id", out var id) && 
                    id != null &&
                    request.EntityIds.Contains(id.ToString()!)).ToList();

                _logger.LogDebug("Filtered categories from {TotalCount} to {FilteredCount} based on EntityIds for batch {BatchNumber} in migration {MigrationId}",
                    allCategories.Count, filteredCategories.Count, request.BatchNumber, request.MigrationId);

                allCategories = filteredCategories;
            }

            // NOTE: Categories are already sorted hierarchically in the discovery phase
            // No need to sort again - just maintain the order from the pre-sorted EntityIds
            _logger.LogInformation("Processing {CategoryCount} categories (already hierarchically sorted) for migration {MigrationId}",
                allCategories.Count, request.MigrationId);

            return allCategories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch categories from source store {SourceStore} tree {SourceTreeId} for migration {MigrationId}",
                request.SourceStore.StoreId, sourceTreeId, request.MigrationId);
            throw;
        }
    }

    /// <summary>
    /// Fetches products from source store with memory-efficient batch processing
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchProductsAsync(BatchProcessingRequest request, CancellationToken cancellationToken)
    {
        if (request.EntityIds == null || !request.EntityIds.Any())
        {
            return new List<Dictionary<string, object>>();
        }

        // OPTIMIZATION: Check if we have cached entity data from discovery phase
        if (request.CachedEntityData != null && request.CachedEntityData.Any())
        {
            _logger.LogInformation("Using cached entity data from V3 discovery phase for products batch {BatchNumber} in migration {MigrationId}",
                request.BatchNumber, request.MigrationId);

            // Filter cached data to only the entities in this batch
            var filteredProducts = request.CachedEntityData.Where(p => 
                p.TryGetValue("id", out var id) && 
                id != null &&
                request.EntityIds.Contains(id.ToString()!)).ToList();

            _logger.LogDebug("Filtered cached products from {TotalCount} to {FilteredCount} based on EntityIds for batch {BatchNumber}",
                request.CachedEntityData.Count, filteredProducts.Count, request.BatchNumber);

            return filteredProducts;
        }

        // MEMORY-EFFICIENT: Direct fetch for this batch only (no full caching)
        _logger.LogInformation("Using memory-efficient direct fetch for products batch {BatchNumber} in migration {MigrationId}",
            request.BatchNumber, request.MigrationId);

        var paginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = Math.Min(250, request.EntityIds.Count * 2), // Fetch slightly more than batch size for efficiency
            IncludeDeleted = false,
            IncludeDrafts = true,
            SortBy = "id",
            SortDirection = "asc"
        };

        var allProducts = new List<Dictionary<string, object>>();
        var currentPage = 1;
        var foundCount = 0;
        var targetIds = request.EntityIds.ToHashSet(); // For faster lookup
        BigCommercePaginatedResponse<Dictionary<string, object>>? lastResponse = null;

        // Fetch pages until we find all required products or exhaust reasonable search
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            paginationRequest.Page = currentPage;
            var response = await _apiClient.GetPaginatedEntitiesAsync(
                request.SourceStore, 
                "products", 
                paginationRequest, 
                cancellationToken);

            lastResponse = response;

            if (response.Data != null && response.Data.Any())
            {
                // Filter to only the products we need for this batch
                var relevantProducts = response.Data.Where(p => 
                    p.TryGetValue("id", out var id) && 
                    id != null &&
                    targetIds.Contains(id.ToString()!)).ToList();

                allProducts.AddRange(relevantProducts);
                foundCount += relevantProducts.Count;

                _logger.LogDebug("Found {RelevantCount} relevant products out of {PageCount} on page {Page} for batch {BatchNumber}",
                    relevantProducts.Count, response.Data.Count, currentPage, request.BatchNumber);

                // Early exit if we found all required products
                if (foundCount >= request.EntityIds.Count)
                {
                    _logger.LogDebug("Found all {RequiredCount} products for batch {BatchNumber}, stopping search",
                        request.EntityIds.Count, request.BatchNumber);
                    break;
                }
            }

            // For V2 APIs: Break if no more data
            if (response.Data == null || response.Data.Count == 0)
            {
                _logger.LogDebug("No more data available, breaking pagination loop for products");
                break;
            }

            currentPage++;

            // Safety check: prevent excessive searching
            if (currentPage > 20) // Max 20 pages for batch search
            {
                _logger.LogWarning("Breaking product search after 20 pages to prevent excessive API calls. Found {FoundCount}/{RequiredCount} products",
                    foundCount, request.EntityIds.Count);
                break;
            }

        } while (currentPage <= (lastResponse?.TotalPages ?? int.MaxValue));

        _logger.LogInformation("Memory-efficient product fetch completed for batch {BatchNumber}: Found {FoundCount}/{RequiredCount} products across {PagesSearched} pages",
            request.BatchNumber, allProducts.Count, request.EntityIds.Count, currentPage - 1);

        return allProducts;
    }

    /// <summary>
    /// Fetches brands from source store with memory-efficient batch processing
    /// </summary>
    private async Task<List<Dictionary<string, object>>> FetchBrandsAsync(BatchProcessingRequest request, CancellationToken cancellationToken)
    {
        if (request.EntityIds == null || !request.EntityIds.Any())
        {
            return new List<Dictionary<string, object>>();
        }

        // OPTIMIZATION: Check if we have cached entity data from discovery phase
        if (request.CachedEntityData != null && request.CachedEntityData.Any())
        {
            _logger.LogInformation("Using cached entity data from V3 discovery phase for brands batch {BatchNumber}",
                request.BatchNumber);

            var filteredBrands = request.CachedEntityData.Where(b => 
                b.TryGetValue("id", out var id) && 
                id != null &&
                request.EntityIds.Contains(id.ToString()!)).ToList();

            return filteredBrands;
        }

        // MEMORY-EFFICIENT: Direct fetch for this batch only
        _logger.LogInformation("Using memory-efficient direct fetch for brands batch {BatchNumber}",
            request.BatchNumber);

        var allBrands = new List<Dictionary<string, object>>();
        var targetIds = request.EntityIds.ToHashSet();
        var currentPage = 1;
        var foundCount = 0;

        BigCommercePaginatedResponse<Dictionary<string, object>>? lastResponse = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var paginationRequest = new BigCommercePaginationRequest
            {
                Page = currentPage,
                Limit = 250,
                IncludeDeleted = false,
                SortBy = "id",
                SortDirection = "asc"
            };

            var response = await _apiClient.GetPaginatedEntitiesAsync(
                request.SourceStore,
                "brands",
                paginationRequest,
                cancellationToken);

            lastResponse = response;

            if (response.Data != null && response.Data.Any())
            {
                var relevantBrands = response.Data.Where(b => 
                    b.TryGetValue("id", out var id) && 
                    id != null &&
                    targetIds.Contains(id.ToString()!)).ToList();

                allBrands.AddRange(relevantBrands);
                foundCount += relevantBrands.Count;

                if (foundCount >= request.EntityIds.Count)
                {
                    break;
                }
            }

            if (response.Data == null || response.Data.Count == 0)
            {
                break;
            }

            currentPage++;

            if (currentPage > 20)
            {
                _logger.LogWarning("Breaking brand search after 20 pages. Found {FoundCount}/{RequiredCount}",
                    foundCount, request.EntityIds.Count);
                break;
            }

        } while (currentPage <= (lastResponse?.TotalPages ?? int.MaxValue));

        return allBrands;
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
            // Check for standard .NET cancellation token
            cancellationToken.ThrowIfCancellationRequested();
            
            // Check for migration-specific cancellation from database
            try
            {
                var migrationCancellationToken = await _migrationStorageService.GetCancellationTokenAsync(request.MigrationId);
                if (migrationCancellationToken != null && !migrationCancellationToken.IsProcessed)
                {
                    _logger.LogInformation("Migration {MigrationId} was cancelled during batch processing - stopping at entity {EntityIndex}/{TotalEntities}", 
                        request.MigrationId, i + 1, sourceEntities.Count);
                    
                    result.Errors.Add($"Migration was cancelled during batch processing at entity {i + 1}/{sourceEntities.Count}");
                    return; // Stop processing remaining entities in this batch
                }
            }
            catch (Exception ex)
            {
                // Log error but don't stop processing - cancellation check failure shouldn't break migration
                _logger.LogError(ex, "Failed to check migration cancellation status for {MigrationId} - continuing with batch processing", request.MigrationId);
            }
            
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

                    // CRITICAL FIX: Store mapping immediately for hierarchy relationships
                    // This ensures parent mappings are available for subsequent child entities
                    try
                    {
                        await _migrationStorageService.StoreEntityMappingsAsync(new List<EntityMapping> { mapping });
                        _logger.LogDebug("Stored entity mapping for {EntityType} {SourceId} -> {DestinationId} in migration {MigrationId}",
                            request.EntityType, mapping.SourceId, mapping.DestinationId, request.MigrationId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to store entity mapping for {EntityType} {SourceId} in migration {MigrationId}. Will retry at batch end.",
                            request.EntityType, mapping.SourceId, request.MigrationId);
                    }

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
    /// Transforms category for destination store with BigCommerce API v3 format compliance
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
                category["parent_id"] = 0; // Make it a root category - BigCommerce expects 0, not null
            }
        }
        else
        {
            // Root category or invalid parent_id - BigCommerce expects 0 for root categories
            category["parent_id"] = 0;
        }

        // Step 2: Add required tree_id field for BigCommerce API
        var destinationTreeId = request.CategoryTreeContext?.DestinationCategoryTreeId;
        if (!string.IsNullOrEmpty(destinationTreeId) && int.TryParse(destinationTreeId, out var treeId))
        {
            category["tree_id"] = treeId;
        }
        else
        {
            throw new InvalidOperationException($"Valid DestinationCategoryTreeId is required for category creation. Current value: {destinationTreeId}");
        }

        // Step 3: Clean up BigCommerce-specific fields that shouldn't be migrated
        var fieldsToRemove = new[] 
        {
            "id", "date_created", "date_modified", "children", "category_id", 
            "is_visible_in_menu", "depth", "path", "has_children",
            "product_count", "category_tree_id", "migration_id", "migrated_from_store", "migrated_at", "metadata"
        };
        
        foreach (var field in fieldsToRemove)
        {
            category.Remove(field);
        }

        // Step 4: Ensure required fields have valid values and correct types
        if (!category.ContainsKey("name") || string.IsNullOrWhiteSpace(category["name"]?.ToString()))
        {
            throw new InvalidOperationException("Category name is required and cannot be empty");
        }

        // Step 5: Transform URL field to proper BigCommerce format
        var categoryName = category["name"].ToString()!;
        var urlPath = GenerateUrlPath(categoryName);
        category["url"] = new Dictionary<string, object>
        {
            ["path"] = urlPath,
            ["is_customized"] = false
        };

        // Step 6: Set default values for optional fields with correct types
        if (!category.ContainsKey("sort_order") || !int.TryParse(category["sort_order"]?.ToString(), out _))
        {
            category["sort_order"] = 0;
        }
        else
        {
            category["sort_order"] = int.Parse(category["sort_order"].ToString()!);
        }

        if (!category.ContainsKey("is_visible"))
        {
            category["is_visible"] = true;
        }
        else if (category["is_visible"] is string visibleStr)
        {
            category["is_visible"] = visibleStr.ToLowerInvariant() == "true" || visibleStr == "1";
        }

        // Step 7: Handle page_title (use name if not provided)
        if (!category.ContainsKey("page_title") || string.IsNullOrWhiteSpace(category["page_title"]?.ToString()))
        {
            category["page_title"] = categoryName;
        }

        // Step 8: Transform meta_keywords to array format if it exists
        if (category.TryGetValue("meta_keywords", out var metaKeywords))
        {
            if (metaKeywords is string keywordsStr && !string.IsNullOrWhiteSpace(keywordsStr))
            {
                // Split by comma and clean up
                var keywordsArray = keywordsStr.Split(',')
                    .Select(k => k.Trim())
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToArray();
                category["meta_keywords"] = keywordsArray;
            }
            else if (metaKeywords is not string[] && metaKeywords is not List<string>)
            {
                // Remove invalid format
                category.Remove("meta_keywords");
            }
        }

        // Step 9: Ensure description is properly formatted (keep HTML if present)
        if (category.TryGetValue("description", out var description))
        {
            if (string.IsNullOrWhiteSpace(description?.ToString()))
            {
                category.Remove("description");
            }
            else
            {
                category["description"] = description.ToString()!.Trim();
            }
        }

        // Step 10: Handle image_url validation
        if (category.TryGetValue("image_url", out var imageUrl))
        {
            var imageUrlString = imageUrl?.ToString();
            if (string.IsNullOrWhiteSpace(imageUrlString))
            {
                category.Remove("image_url");
            }
            else
            {
                // Ensure it's a full URL
                if (!imageUrlString.StartsWith("http://") && !imageUrlString.StartsWith("https://"))
                {
                    _logger.LogDebug("Relative image URL detected for category {CategoryName}: {ImageUrl}. Removing from payload.",
                        categoryName, imageUrlString);
                    category.Remove("image_url");
                }
            }
        }

        // Step 11: Handle optional numeric fields
        if (category.TryGetValue("views", out var views))
        {
            if (int.TryParse(views?.ToString(), out var viewsInt))
            {
                category["views"] = viewsInt;
            }
            else
            {
                category.Remove("views");
            }
        }

        // Step 12: Set default values for other optional fields
        if (!category.ContainsKey("default_product_sort"))
        {
            category["default_product_sort"] = "use_store_settings";
        }

        // Step 13: Clean up any remaining null or empty values
        var keysToRemove = category.Where(kvp => kvp.Value == null || 
            (kvp.Value is string str && string.IsNullOrWhiteSpace(str)))
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var key in keysToRemove)
        {
            category.Remove(key);
        }

        _logger.LogDebug("Successfully transformed category {CategoryName} for migration {MigrationId}. " +
            "Final structure: parent_id={ParentId}, tree_id={TreeId}, url_path={UrlPath}",
            categoryName, 
            request.MigrationId,
            category.TryGetValue("parent_id", out var finalParentId) ? finalParentId : "unknown",
            category.TryGetValue("tree_id", out var finalTreeId) ? finalTreeId : "unknown",
            category.TryGetValue("url", out var urlObj) && urlObj is Dictionary<string, object> urlDict ? 
                urlDict.TryGetValue("path", out var pathVal) ? pathVal : "unknown" : "unknown");
    }

    /// <summary>
    /// Generates a URL path from category name following BigCommerce conventions
    /// </summary>
    private static string GenerateUrlPath(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return "/";
        }

        // Convert to lowercase, replace spaces with hyphens, remove special characters
        var path = categoryName.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("&", "and")
            .Replace("'", "")
            .Replace("\"", "");

        // Remove any characters that aren't alphanumeric, hyphens, or underscores
        path = System.Text.RegularExpressions.Regex.Replace(path, @"[^a-z0-9\-_]", "");
        
        // Remove multiple consecutive hyphens
        path = System.Text.RegularExpressions.Regex.Replace(path, @"-+", "-");
        
        // Remove leading/trailing hyphens
        path = path.Trim('-');

        // Ensure it starts and ends with forward slashes
        return $"/{path}/";
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
        // Get source ID safely
        if (!sourceEntity.TryGetValue("id", out var sourceId) || sourceId == null)
        {
            throw new InvalidOperationException($"Source entity is missing required 'id' field. Available keys: {string.Join(", ", sourceEntity.Keys)}");
        }

        // Get destination ID safely - BigCommerce API might return different field names
        object? destinationId = null;
        var possibleIdFields = new[] { "id", "category_id", "product_id", "brand_id" };
        
        foreach (var idField in possibleIdFields)
        {
            if (destinationEntity.TryGetValue(idField, out destinationId) && destinationId != null)
            {
                break;
            }
        }

        if (destinationId == null)
        {
            throw new InvalidOperationException($"Destination entity is missing ID field. " +
                $"Available keys: {string.Join(", ", destinationEntity.Keys)}. " +
                $"Expected one of: {string.Join(", ", possibleIdFields)}");
        }

        return new EntityMapping
        {
            MigrationId = request.MigrationId,
            EntityType = request.EntityType,
            SourceId = sourceId.ToString()!,
            DestinationId = destinationId.ToString()!,
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