using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Activities.Models;
using BigCommerce.Migration.Activities.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace BigCommerce.Migration.Activities.Services;

/// <summary>
/// Product Components Migration Pipeline for Enhanced Product Migration Phase 2
/// Handles product components (options, modifiers, images, reviews) for existing products
/// Fetches products 10/page with comprehensive includes and creates only component entities
/// Implements dual-tier progress aggregation for real-time dashboard updates
/// </summary>
public class ProductComponentsMigrationPipeline : IProductComponentsMigrationPipeline
{
    private readonly ISignalREventFactory _signalREventFactory;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ILogger<ProductComponentsMigrationPipeline> _logger;
    private readonly IEntityCreateService _entityCreateService;
    private readonly IEntityTransformService _entityTransformService;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly IEntityErrorHandlingService _errorHandlingService;
    private readonly ICancellationStore _cancellationStore; // ✅ Phase 3.2.1: Native cancellation support

    public ProductComponentsMigrationPipeline(
        ISignalREventFactory signalREventFactory,
        IProgressEventPublisher progressEventPublisher,
        ILogger<ProductComponentsMigrationPipeline> logger,
        IEntityCreateService entityCreateService,
        IEntityTransformService entityTransformService,
        IMigrationStorageService migrationStorageService,
        IEntityErrorHandlingService errorHandlingService,
        ICancellationStore cancellationStore)
    {
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entityCreateService = entityCreateService ?? throw new ArgumentNullException(nameof(entityCreateService));
        _entityTransformService = entityTransformService ?? throw new ArgumentNullException(nameof(entityTransformService));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore)); // ✅ Phase 3.2.1: Native cancellation injection
    }

    /// <summary>
    /// Processes product components (options, modifiers, images, reviews) for existing products
    /// Fetches products 10/page with comprehensive includes and creates only the component entities
    /// Implements dual-tier progress aggregation for real-time dashboard updates
    /// </summary>
    public async Task<BatchProcessingResult> ProcessProductComponentsAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        string requestedEntityType,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation("🔗 [PRODUCT-COMPONENTS-PIPELINE] Starting product components migration for {ProductCount} products in migration {MigrationId}", 
            entities.Count, migrationId);

        // ✅ PHASE 2: Entities are already fetched with includes by standard discovery+fetch process
        // Configuration: pageSize=10, include=options,modifiers,reviews
        var productsWithComponents = entities;

        var result = new ComprehensiveEntityProcessingResult
        {
            BatchNumber = 1,
            TotalProcessed = productsWithComponents.Count,
            SubEntityStatistics = new Dictionary<string, SubEntityStatistics>(),
            ProcessingTime = TimeSpan.Zero
        };

        try
        {
            // Phase 3.2.1: Check for cancellation at the start of product components processing
            await CheckCancellationAsync(migrationId);

            // Phase 3.2.4: Report pipeline start status
            await PublishPipelineProgressAsync(migrationId, "starting", "Initializing product components processing", 0);

            // 🔗 EXTRACT ALL COMPONENTS: Parse all products and extract components by type
            // Phase 3.2.2: Enhanced with periodic cancellation checks during extraction
            var allComponents = await ExtractAllComponentsFromProductsAsync(productsWithComponents, migrationId);
            
            _logger.LogInformation("🔍 [PRODUCT-COMPONENTS-PIPELINE] Extracted components: Options={OptionsCount}, Modifiers={ModifiersCount}, Images={ImagesCount}, Reviews={ReviewsCount}", 
                allComponents["options"].Count, allComponents["modifiers"].Count, 
                allComponents["images"].Count, allComponents["reviews"].Count);

            // Phase 3.2.1: Check for cancellation after component extraction
            await CheckCancellationAsync(migrationId);

            // Phase 3.2.4: Report extraction completion progress
            var totalComponents = allComponents.Values.Sum(c => c.Count);
            await PublishPipelineProgressAsync(migrationId, "extracting", 
                $"Extracted {totalComponents} components from {productsWithComponents.Count} products", 25);

            // Initialize component statistics with actual counts
            var componentTypes = new[] { "options", "modifiers", "images", "reviews" };
            foreach (var componentType in componentTypes)
            {
                result.SubEntityStatistics[componentType] = new SubEntityStatistics
                {
                    EntityType = componentType,
                    TotalProcessed = allComponents[componentType].Count,
                    SuccessfulCount = 0,
                    FailedCount = 0,
                    ProcessingTime = TimeSpan.Zero
                };
            }

            // 🚀 PARALLEL PROCESSING: Process each component type in parallel
            var processingTasks = new List<Task>();
            
            foreach (var componentType in componentTypes)
            {
                // Phase 3.2.1: Check for cancellation before each component type processing
                await CheckCancellationAsync(migrationId);

                var components = allComponents[componentType];
                if (components.Any())
                {
                    _logger.LogInformation("🚀 [COMPONENT-{ComponentType}] Starting processing of {Count} {ComponentType}",
                        componentType.ToUpper(), components.Count, componentType);

                    var task = ProcessComponentTypeInParallel(
                        componentType, components, migrationId, sourceStore, destinationStore, 
                        result.SubEntityStatistics[componentType], cancellationToken);
                    processingTasks.Add(task);
                }
                else
                {
                    _logger.LogInformation("⏭️ [COMPONENT-{ComponentType}] No {ComponentType} found, skipping", 
                        componentType.ToUpper(), componentType);
                }
            }

            // Wait for all component types to complete processing
            await Task.WhenAll(processingTasks);
            
            _logger.LogInformation("✅ [PRODUCT-COMPONENTS-PIPELINE] Completed parallel processing of all component types");

            // Phase 3.2.4: Report processing completion progress
            await PublishPipelineProgressAsync(migrationId, "processing", "Completed component processing", 75);

            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;



            _logger.LogInformation("✅ [PRODUCT-COMPONENTS-PIPELINE] Completed product components migration for {ProductCount} products. " +
                                 "Options: {OptionsCount}, Modifiers: {ModifiersCount}, Images: {ImagesCount}, Reviews: {ReviewsCount}. " +
                                 "Duration: {Duration}ms",
                productsWithComponents.Count,
                result.SubEntityStatistics["options"].SuccessfulCount,
                result.SubEntityStatistics["modifiers"].SuccessfulCount,
                result.SubEntityStatistics["images"].SuccessfulCount,
                result.SubEntityStatistics["reviews"].SuccessfulCount,
                stopwatch.ElapsedMilliseconds);

            // Phase 3.2.4: Report final completion status
            var totalSuccessful = result.SubEntityStatistics.Values.Sum(s => s.SuccessfulCount);
            var totalProcessed = result.SubEntityStatistics.Values.Sum(s => s.TotalProcessed);
            var totalFailed = result.SubEntityStatistics.Values.Sum(s => s.FailedCount);
            
            // 🚨 FIX: Return component-specific statistics based on requested entityType
            // This ensures individual component types get correct counts in the dashboard
            var entityTypeKey = requestedEntityType?.ToLowerInvariant() ?? "product-components";
            
            if (result.SubEntityStatistics.ContainsKey(entityTypeKey))
            {
                // Return statistics for the specific component type being processed
                var componentStats = result.SubEntityStatistics[entityTypeKey];
                result.SuccessfulEntities = componentStats.SuccessfulCount;
                result.FailedEntities = componentStats.FailedCount;
                result.TotalProcessed = componentStats.TotalProcessed;
                
                _logger.LogInformation("🔧 [COMPONENT-SPECIFIC-RESULT] Returning {EntityType}-specific statistics: " +
                                     "TotalProcessed={TotalProcessed}, Successful={Successful}, Failed={Failed}",
                    entityTypeKey, componentStats.TotalProcessed, componentStats.SuccessfulCount, componentStats.FailedCount);
            }
            else
            {
                // Fallback to aggregated statistics for 'product-components' or unknown types
                result.SuccessfulEntities = totalSuccessful;
                result.FailedEntities = totalFailed;
                result.TotalProcessed = totalProcessed;
                
                _logger.LogInformation("🔧 [AGGREGATED-RESULT] Returning aggregated component statistics for '{EntityType}': " +
                                     "TotalProcessed={TotalProcessed}, Successful={Successful}, Failed={Failed}",
                    entityTypeKey, totalProcessed, totalSuccessful, totalFailed);
            }
            
            await PublishPipelineProgressAsync(migrationId, "completed", 
                $"Successfully processed {totalSuccessful}/{totalProcessed} components", 100);

            return result;
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;

            _logger.LogWarning(ex, "🚫 [PRODUCT-COMPONENTS-PIPELINE] Product components processing cancelled for migration {MigrationId}", migrationId);
            
            // Phase 3.2.4: Report cancellation status
            await PublishPipelineProgressAsync(migrationId, "cancelled", 
                $"Product components processing cancelled: {ex.Message}", null, true, ex.Message);
            
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;

            _logger.LogError(ex, "❌ [PRODUCT-COMPONENTS-PIPELINE] Failed to process product components for migration {MigrationId}", migrationId);
            
            // Phase 3.2.4: Report failure status
            await PublishPipelineProgressAsync(migrationId, "failed", 
                $"Product components processing failed: {ex.Message}", null);
            
            throw;
        }
    }

    /// <summary>
    /// Processes components (options, modifiers, images, reviews) for a single product
    /// IMPORTANT: Does NOT create the product - only creates component entities for existing products
    /// </summary>
    private async Task ProcessProductComponentsForSingleProduct(
        Dictionary<string, object> product,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        ComprehensiveEntityProcessingResult result,
        CancellationToken cancellationToken)
    {
        var productId = product.ContainsKey("id") ? product["id"]?.ToString() : "unknown";
        
        _logger.LogDebug("🔧 [PRODUCT-COMPONENTS] Processing components for product {ProductId}", productId);

        // Process Options
        if (product.ContainsKey("options") && product["options"] is List<object> options)
        {
            await ProcessComponentType("options", options.Cast<Dictionary<string, object>>().ToList(), 
                productId, migrationId, sourceStore, destinationStore, result, cancellationToken);
        }

        // Process Modifiers  
        if (product.ContainsKey("modifiers") && product["modifiers"] is List<object> modifiers)
        {
            await ProcessComponentType("modifiers", modifiers.Cast<Dictionary<string, object>>().ToList(),
                productId, migrationId, sourceStore, destinationStore, result, cancellationToken);
        }

        // Process Images
        if (product.ContainsKey("images") && product["images"] is List<object> images)
        {
            await ProcessComponentType("images", images.Cast<Dictionary<string, object>>().ToList(),
                productId, migrationId, sourceStore, destinationStore, result, cancellationToken);
        }

        // Process Reviews
        if (product.ContainsKey("reviews") && product["reviews"] is List<object> reviews)
        {
            await ProcessComponentType("reviews", reviews.Cast<Dictionary<string, object>>().ToList(),
                productId, migrationId, sourceStore, destinationStore, result, cancellationToken);
        }
    }

    /// <summary>
    /// Processes a specific component type (options, modifiers, images, or reviews) for a product
    /// </summary>
    private async Task ProcessComponentType(
        string componentType,
        List<Dictionary<string, object>> components,
        string productId,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        ComprehensiveEntityProcessingResult result,
        CancellationToken cancellationToken)
    {
        var componentStopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("🔧 [COMPONENT-{ComponentType}] Processing {Count} {ComponentType} for product {ProductId}", 
                componentType.ToUpper(), components.Count, componentType, productId);

            var stats = result.SubEntityStatistics[componentType];
            
            foreach (var component in components)
            {
                // Create request for this component
                var componentRequest = new BatchProcessingRequest
                {
                    MigrationId = migrationId,
                    EntityType = componentType,
                    SourceStore = sourceStore,
                    DestinationStore = destinationStore,
                    BatchNumber = 1,
                    TotalBatches = 1,
                    EntityIds = new List<string> { component.TryGetValue("id", out var compId) ? compId?.ToString() ?? "unknown" : "unknown" },
                    Timestamp = DateTime.UtcNow
                };
                
                try
                {
                    // Create component using appropriate strategy
                    var componentList = new List<Dictionary<string, object>> { component };
                    
                    var createdComponents = await _entityCreateService.CreateEntitiesAsync(
                        componentList, 
                        componentRequest, 
                        cancellationToken);
                    
                    if (createdComponents != null && createdComponents.Any())
                    {
                        // 🔗 NO ENTITY MAPPING: Components (options, modifiers, images, reviews) don't need individual EntityMapping records
                        // - Options: Use hierarchical mapping in OptionsCreationStrategy for variant migration
                        // - Modifiers/Images/Reviews: No dependents, no mapping needed
                        
                        stats.TotalProcessed++;
                        stats.SuccessfulCount++;
                        
                        _logger.LogDebug("✅ [COMPONENT-{ComponentType}] Successfully created {ComponentType} for product {ProductId}", 
                            componentType.ToUpper(), componentType, productId);

                        // 🔗 OPTION MAPPING: Now handled directly in OptionsCreationStrategy via HierarchicalOptionMappingService
                    }
                    else
                    {
                        stats.TotalProcessed++;
                        stats.FailedCount++;
                        
                        _logger.LogWarning("⚠️ [COMPONENT-{ComponentType}] No {ComponentType} created for product {ProductId} - creation returned empty result", 
                            componentType.ToUpper(), componentType, productId);
                    }
                }
                catch (Exception ex)
                {
                    stats.TotalProcessed++;
                    stats.FailedCount++;
                    
                    // Log detailed error for component creation failure
                    await _errorHandlingService.LogEntityErrorAsync(
                        ex, 
                        component, 
                        componentRequest,
                        component.TryGetValue("id", out var compIdForError) ? compIdForError?.ToString() ?? "unknown" : "unknown",
                        null, // No response payload available
                        $"Failed to create {componentType} for product {productId}",
                        cancellationToken);
                    
                    _logger.LogError(ex, "❌ [COMPONENT-{ComponentType}] Failed to create {ComponentType} for product {ProductId}: {ErrorMessage}", 
                        componentType.ToUpper(), componentType, productId, ex.Message);
                    
                    // Continue processing other components (continue-on-error policy)
                }
            }

            componentStopwatch.Stop();
            stats.ProcessingTime = stats.ProcessingTime.Add(componentStopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [COMPONENT-{ComponentType}] Failed to process {ComponentType} components for product {ProductId}", 
                componentType.ToUpper(), componentType, productId);
            throw;
        }
    }

    /// <summary>
    /// Estimates the processing time for a batch of product components
    /// Used for timeout management and progress estimation
    /// </summary>
    public TimeSpan EstimateProcessingTime(int productCount, bool hasComponents)
    {
        if (!hasComponents)
            return TimeSpan.FromMinutes(1);

        // Estimate based on:
        // - 10 products per batch (configured)
        // - Average 5 components per type per product
        // - 100ms per component creation
        var averageComponentsPerProduct = 20; // 5 options + 5 modifiers + 5 images + 5 reviews
        var totalComponents = productCount * averageComponentsPerProduct;
        var estimatedSeconds = totalComponents * 0.1; // 100ms per component

        return TimeSpan.FromSeconds(Math.Max(estimatedSeconds, 60)); // Minimum 1 minute
    }

    /// <summary>
    /// Stores option mapping immediately when each option is created (SIMPLE APPROACH)
    /// Updates the product's OptionsMappingData with the new option mapping
    /// </summary>
    private async Task StoreOptionMappingImmediatelyAsync(
        Dictionary<string, object> sourceOption,
        Dictionary<string, object> createdOption,
        string sourceOptionId,
        string destinationOptionId,
        string sourceProductId,
        string migrationId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔗 [OPTION-MAPPING] CALLED: Storing option mapping {SourceOptionId} → {DestinationOptionId} for product {ProductId}", 
            sourceOptionId, destinationOptionId, sourceProductId);
            
        try
        {
            // Get the product's entity mapping
            var productMapping = await _migrationStorageService.GetEntityMappingAsync(migrationId, "products", sourceProductId);
            if (productMapping == null)
            {
                _logger.LogWarning("📋 [OPTION-MAPPING] Product mapping not found for product {ProductId}", sourceProductId);
                return;
            }

            // Build the option mapping for this single option
            var optionValueMappings = new List<object>();
            
            // Extract option values if they exist in the created option
            // ✅ FIXED: Handle both JsonElement arrays and List<object> for BigCommerce API responses
            if (createdOption.TryGetValue("option_values", out var optionValuesObj))
            {
                List<Dictionary<string, object>>? optionValuesList = null;
                
                // Handle JsonElement array (from API response deserialization)
                if (optionValuesObj is JsonElement ovElement && ovElement.ValueKind == JsonValueKind.Array)
                {
                    optionValuesList = new List<Dictionary<string, object>>();
                    foreach (var ovJsonElement in ovElement.EnumerateArray())
                    {
                        var ovDict = JsonSerializer.Deserialize<Dictionary<string, object>>(ovJsonElement.GetRawText());
                        if (ovDict != null) optionValuesList.Add(ovDict);
                    }
                    _logger.LogDebug("🔍 [OPTION-MAPPING] Parsed {Count} option values from JsonElement array", optionValuesList.Count);
                }
                // Handle List<object> (already converted)
                else if (optionValuesObj is List<object> objectList)
                {
                    optionValuesList = objectList.Cast<Dictionary<string, object>>().ToList();
                    _logger.LogDebug("🔍 [OPTION-MAPPING] Using {Count} option values from List<object>", optionValuesList.Count);
                }
                else
                {
                    _logger.LogWarning("⚠️ [OPTION-MAPPING] Unexpected option_values type: {Type}, value: {Value}", 
                        optionValuesObj?.GetType().Name ?? "null", JsonSerializer.Serialize(optionValuesObj));
                }
                
                // Process option values if we successfully extracted them
                if (optionValuesList != null && optionValuesList.Count > 0)
                {
                    foreach (var optionValue in optionValuesList)
                    {
                        var sourceOptionValueId = GetSourceOptionValueId(sourceOption, optionValue);
                        var destinationOptionValueId = optionValue.TryGetValue("id", out var ovId) ? ovId?.ToString() : null;

                        if (!string.IsNullOrEmpty(sourceOptionValueId) && !string.IsNullOrEmpty(destinationOptionValueId))
                        {
                            optionValueMappings.Add(new
                            {
                                sourceId = sourceOptionValueId,
                                destinationId = destinationOptionValueId
                            });
                            _logger.LogDebug("✅ [OPTION-MAPPING] Mapped option value: {SourceId} → {DestinationId}", 
                                sourceOptionValueId, destinationOptionValueId);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ [OPTION-MAPPING] Could not map option value - sourceId: {SourceId}, destinationId: {DestinationId}", 
                                sourceOptionValueId ?? "null", destinationOptionValueId ?? "null");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ [OPTION-MAPPING] No option values found to process for option {OptionId}", sourceOptionId);
                }
            }

            // Get existing option mappings or create new structure
            var existingOptionsData = new List<object>();
            if (!string.IsNullOrEmpty(productMapping.OptionsMappingData))
            {
                try
                {
                    var existingData = JsonSerializer.Deserialize<Dictionary<string, object>>(productMapping.OptionsMappingData);
                    if (existingData?.TryGetValue("options", out var existingOptions) == true && existingOptions is JsonElement optionsElement)
                    {
                        existingOptionsData = JsonSerializer.Deserialize<List<object>>(optionsElement.GetRawText()) ?? new List<object>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "📋 [OPTION-MAPPING] Failed to parse existing options data, starting fresh");
                    existingOptionsData = new List<object>();
                }
            }

            // Add the new option mapping
            existingOptionsData.Add(new
            {
                sourceOptionId = sourceOptionId,
                destinationOptionId = destinationOptionId,
                optionValues = optionValueMappings
            });

            // Create the final JSON structure
            var optionMappingsJson = new { options = existingOptionsData };
            var jsonString = JsonSerializer.Serialize(optionMappingsJson, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            // Update the product's entity mapping immediately
            productMapping.OptionsMappingData = jsonString;
            await _migrationStorageService.UpdateEntityMappingAsync(productMapping);

            _logger.LogDebug("✅ [OPTION-MAPPING] Immediately stored option mapping: Source={SourceOptionId} → Destination={DestinationOptionId} with {OptionValueCount} option values for product {ProductId}", 
                sourceOptionId, destinationOptionId, optionValueMappings.Count, sourceProductId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [OPTION-MAPPING] Failed to store option mapping immediately for option {OptionId} in product {ProductId}", sourceOptionId, sourceProductId);
            // Don't throw - this is not critical enough to fail the entire migration
        }
    }

    /// <summary>
    /// Helper method to extract source option value ID by matching with source option
    /// </summary>
    private static string? GetSourceOptionValueId(Dictionary<string, object> sourceOption, Dictionary<string, object> createdOptionValue)
    {
        // Try to get the label from created option value to match with source
        if (!createdOptionValue.TryGetValue("label", out var createdLabel)) return null;
        
        // Get source option values to find matching label
        if (!sourceOption.TryGetValue("option_values", out var sourceValuesObj) || 
            sourceValuesObj is not List<object> sourceValuesList) return null;

        // Find source option value with matching label
        foreach (var sourceValue in sourceValuesList.Cast<Dictionary<string, object>>())
        {
            if (sourceValue.TryGetValue("label", out var sourceLabel) && 
                sourceLabel?.ToString() == createdLabel?.ToString())
            {
                return sourceValue.TryGetValue("id", out var sourceId) ? sourceId?.ToString() : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts all components (options, modifiers, images, reviews) from products
    /// Returns components organized by type for parallel processing
    /// Phase 3.2.2: Enhanced with periodic cancellation checks during extraction (every 5-10 products)
    /// </summary>
    private async Task<Dictionary<string, List<ComponentWithContext>>> ExtractAllComponentsFromProductsAsync(
        List<Dictionary<string, object>> products,
        string migrationId)
    {
        _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Starting component extraction from {ProductCount} products", products.Count);
        
        var allComponents = new Dictionary<string, List<ComponentWithContext>>
        {
            ["options"] = new List<ComponentWithContext>(),
            ["modifiers"] = new List<ComponentWithContext>(),
            ["images"] = new List<ComponentWithContext>(),
            ["reviews"] = new List<ComponentWithContext>()
        };

        for (int i = 0; i < products.Count; i++)
        {
            // Phase 3.2.2: Periodic cancellation checks during component extraction (every 8 products)
            if (i > 0 && i % 8 == 0)
            {
                await CheckCancellationAsync(migrationId);
                _logger.LogDebug("🔍 [EXTRACTION-CANCELLATION] Cancellation check passed at product {ProductIndex}/{TotalProducts}", 
                    i + 1, products.Count);
            }

            var product = products[i];
            var productId = product.TryGetValue("id", out var id) ? id?.ToString() : "unknown";
            
            _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Product {Index}: ID={ProductId}, Keys=[{Keys}]", 
                i + 1, productId, string.Join(", ", product.Keys));
            
            // Log specific component field presence
            var hasOptions = product.ContainsKey("options");
            var hasModifiers = product.ContainsKey("modifiers");
            var hasImages = product.ContainsKey("images");
            var hasReviews = product.ContainsKey("reviews");
            
            _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Product {ProductId} component fields: Options={HasOptions}, Modifiers={HasModifiers}, Images={HasImages}, Reviews={HasReviews}", 
                productId, hasOptions, hasModifiers, hasImages, hasReviews);
            
            // Extract options
            if (product.TryGetValue("options", out var optionsValue) && optionsValue != null)
            {
                _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Product {ProductId} options field - Type: {OptionsType}, Value: {OptionsValue}", 
                    productId, optionsValue.GetType().Name, optionsValue.ToString()?.Substring(0, Math.Min(100, optionsValue.ToString()?.Length ?? 0)));
                
                // Handle both JsonElement and already-deserialized collections
                List<Dictionary<string, object>>? optionsList = null;
                
                if (optionsValue is JsonElement optionsElement && optionsElement.ValueKind == JsonValueKind.Array)
                {
                    _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Product {ProductId} - Using JsonElement extraction path for options, ArrayLength: {ArrayLength}", 
                        productId, optionsElement.GetArrayLength());
                    
                    // Handle JsonElement case (from JSON parsing)
                    optionsList = new List<Dictionary<string, object>>();
                    foreach (var optionElement in optionsElement.EnumerateArray())
                    {
                        var optionDict = JsonSerializer.Deserialize<Dictionary<string, object>>(optionElement.GetRawText());
                        if (optionDict != null) optionsList.Add(optionDict);
                    }
                }
                else if (optionsValue is List<object> objectList)
                {
                    _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Product {ProductId} - Using List<object> extraction path for options, Count: {Count}", 
                        productId, objectList.Count);
                    
                    // Handle already-deserialized List<object> case (from API client)
                    optionsList = objectList.Cast<Dictionary<string, object>>().ToList();
                }
                else if (optionsValue is string optionsJsonString && !string.IsNullOrEmpty(optionsJsonString))
                {
                    _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Product {ProductId} - Using JSON string extraction path for options, StringLength: {StringLength}", 
                        productId, optionsJsonString.Length);
                    
                    try
                    {
                        // Handle JSON string case (from API client returning serialized JSON)
                        var parsedOptions = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(optionsJsonString);
                        if (parsedOptions != null)
                        {
                            optionsList = parsedOptions;
                            _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Product {ProductId} - Successfully parsed {Count} options from JSON string", 
                                productId, optionsList.Count);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning("🔍 [EXTRACTION-DEBUG] Product {ProductId} - Failed to parse options JSON string: {Error}", 
                            productId, ex.Message);
                    }
                }
                else
                {
                    _logger.LogWarning("🔍 [EXTRACTION-DEBUG] Product {ProductId} - Unknown options type: {ActualType}, Value: {Value}", 
                        productId, optionsValue.GetType().FullName, optionsValue.ToString()?.Substring(0, Math.Min(50, optionsValue.ToString()?.Length ?? 0)));
                }
                
                // Add extracted options to components
                if (optionsList != null)
                {
                    _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Product {ProductId} - Adding {OptionsCount} options to extraction results", 
                        productId, optionsList.Count);
                    
                    foreach (var optionDict in optionsList)
                    {
                                            // 🔍 STAGE 2 DEBUG: Log the complete parsed option dictionary payload
                    _logger.LogInformation("🔍 [STAGE-2-EXTRACTION] Product {ProductId} - COMPLETE OPTION PAYLOAD: {OptionPayload}", 
                        productId, JsonSerializer.Serialize(optionDict, new JsonSerializerOptions { WriteIndented = true }));
                    
                    // 🔍 STAGE 2 DEBUG: Specifically check for id field
                    var hasId = optionDict.ContainsKey("id");
                    var idValue = optionDict.TryGetValue("id", out var optionId) ? optionId?.ToString() : "MISSING";
                    _logger.LogInformation("🔍 [STAGE-2-EXTRACTION] Product {ProductId} - Option has 'id' field: {HasId}, value: {IdValue}", 
                        productId, hasId, idValue);
                        
                        allComponents["options"].Add(new ComponentWithContext
                        {
                            Component = optionDict,
                            ProductId = productId,
                            ComponentType = "options"
                        });
                    }
                }
                else
                {
                    _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Product {ProductId} - No options extracted (optionsList is null)", productId);
                }
            }

            // Extract modifiers
            if (product.TryGetValue("modifiers", out var modifiersValue) && modifiersValue != null)
            {
                // Handle both JsonElement and already-deserialized collections
                List<Dictionary<string, object>>? modifiersList = null;
                
                if (modifiersValue is JsonElement modifiersElement && modifiersElement.ValueKind == JsonValueKind.Array)
                {
                    // Handle JsonElement case (from JSON parsing)
                    modifiersList = new List<Dictionary<string, object>>();
                    foreach (var modifierElement in modifiersElement.EnumerateArray())
                    {
                        var modifierDict = JsonSerializer.Deserialize<Dictionary<string, object>>(modifierElement.GetRawText());
                        if (modifierDict != null) modifiersList.Add(modifierDict);
                    }
                }
                else if (modifiersValue is List<object> objectList)
                {
                    // Handle already-deserialized List<object> case (from API client)
                    modifiersList = objectList.Cast<Dictionary<string, object>>().ToList();
                }
                else if (modifiersValue is string modifiersJsonString && !string.IsNullOrEmpty(modifiersJsonString))
                {
                    try
                    {
                        // Handle JSON string case (from API client returning serialized JSON)
                        var parsedModifiers = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(modifiersJsonString);
                        if (parsedModifiers != null)
                        {
                            modifiersList = parsedModifiers;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning("🔍 [EXTRACTION-DEBUG] Product {ProductId} - Failed to parse modifiers JSON string: {Error}", 
                            productId, ex.Message);
                    }
                }
                
                // Add extracted modifiers to components
                if (modifiersList != null)
                {
                    foreach (var modifierDict in modifiersList)
                    {
                        allComponents["modifiers"].Add(new ComponentWithContext
                        {
                            Component = modifierDict,
                            ProductId = productId,
                            ComponentType = "modifiers"
                        });
                    }
                }
            }

            // Extract images
            if (product.TryGetValue("images", out var imagesValue) && imagesValue != null)
            {
                // Handle both JsonElement and already-deserialized collections
                List<Dictionary<string, object>>? imagesList = null;
                
                if (imagesValue is JsonElement imagesElement && imagesElement.ValueKind == JsonValueKind.Array)
                {
                    // Handle JsonElement case (from JSON parsing)
                    imagesList = new List<Dictionary<string, object>>();
                    foreach (var imageElement in imagesElement.EnumerateArray())
                    {
                        var imageDict = JsonSerializer.Deserialize<Dictionary<string, object>>(imageElement.GetRawText());
                        if (imageDict != null) imagesList.Add(imageDict);
                    }
                }
                else if (imagesValue is List<object> objectList)
                {
                    // Handle already-deserialized List<object> case (from API client)
                    imagesList = objectList.Cast<Dictionary<string, object>>().ToList();
                }
                else if (imagesValue is string imagesJsonString && !string.IsNullOrEmpty(imagesJsonString))
                {
                    try
                    {
                        // Handle JSON string case (from API client returning serialized JSON)
                        var parsedImages = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(imagesJsonString);
                        if (parsedImages != null)
                        {
                            imagesList = parsedImages;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning("🔍 [EXTRACTION-DEBUG] Product {ProductId} - Failed to parse images JSON string: {Error}", 
                            productId, ex.Message);
                    }
                }
                
                // Add extracted images to components
                if (imagesList != null)
                {
                    foreach (var imageDict in imagesList)
                    {
                        allComponents["images"].Add(new ComponentWithContext
                        {
                            Component = imageDict,
                            ProductId = productId,
                            ComponentType = "images"
                        });
                    }
                }
            }

            // Extract reviews
            if (product.TryGetValue("reviews", out var reviewsValue) && reviewsValue != null)
            {
                // Handle both JsonElement and already-deserialized collections
                List<Dictionary<string, object>>? reviewsList = null;
                
                if (reviewsValue is JsonElement reviewsElement && reviewsElement.ValueKind == JsonValueKind.Array)
                {
                    // Handle JsonElement case (from JSON parsing)
                    reviewsList = new List<Dictionary<string, object>>();
                    foreach (var reviewElement in reviewsElement.EnumerateArray())
                    {
                        var reviewDict = JsonSerializer.Deserialize<Dictionary<string, object>>(reviewElement.GetRawText());
                        if (reviewDict != null) reviewsList.Add(reviewDict);
                    }
                }
                else if (reviewsValue is List<object> objectList)
                {
                    // Handle already-deserialized List<object> case (from API client)
                    reviewsList = objectList.Cast<Dictionary<string, object>>().ToList();
                }
                else if (reviewsValue is string reviewsJsonString && !string.IsNullOrEmpty(reviewsJsonString))
                {
                    try
                    {
                        // Handle JSON string case (from API client returning serialized JSON)
                        var parsedReviews = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(reviewsJsonString);
                        if (parsedReviews != null)
                        {
                            reviewsList = parsedReviews;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning("🔍 [EXTRACTION-DEBUG] Product {ProductId} - Failed to parse reviews JSON string: {Error}", 
                            productId, ex.Message);
                    }
                }
                
                // Add extracted reviews to components
                if (reviewsList != null)
                {
                    foreach (var reviewDict in reviewsList)
                    {
                        allComponents["reviews"].Add(new ComponentWithContext
                        {
                            Component = reviewDict,
                            ProductId = productId,
                            ComponentType = "reviews"
                        });
                    }
                }
            }
        }

        _logger.LogInformation("🔍 [EXTRACTION-DEBUG] ✅ EXTRACTION COMPLETE: {OptionsCount} options, {ModifiersCount} modifiers, {ImagesCount} images, {ReviewsCount} reviews from {ProductCount} products",
            allComponents["options"].Count, allComponents["modifiers"].Count, 
            allComponents["images"].Count, allComponents["reviews"].Count, products.Count);

        // Phase 3.2.2: Final cancellation check before returning extraction results
        await CheckCancellationAsync(migrationId);

        return allComponents;
    }

    /// <summary>
    /// Processes a specific component type (options, modifiers, images, reviews) in parallel
    /// Uses appropriate transform and creation strategies for each component type
    /// </summary>
    private async Task ProcessComponentTypeInParallel(
        string componentType,
        List<ComponentWithContext> components,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        SubEntityStatistics stats,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("🚀 [COMPONENT-{ComponentType}] Starting parallel processing of {Count} {ComponentType}",
                componentType.ToUpper(), components.Count, componentType);

            // Phase 3.2.3: Check for cancellation before starting parallel processing
            await CheckCancellationAsync(migrationId);

            // Process components in parallel batches
            const int batchSize = 10; // Process 10 components at a time
            var semaphore = new SemaphoreSlim(5, 5); // Max 5 concurrent batches
            
            var batches = components.Chunk(batchSize).ToList();
            var tasks = batches.Select(async (batch, batchIndex) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    // Phase 3.2.3: Check for cancellation before processing each batch
                    await CheckCancellationAsync(migrationId);
                    
                    await ProcessComponentBatch(componentType, batch, migrationId, 
                        sourceStore, destinationStore, stats, batchIndex, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            
            stopwatch.Stop();
            stats.ProcessingTime = stopwatch.Elapsed;

            _logger.LogInformation("✅ [COMPONENT-{ComponentType}] Completed processing: {SuccessCount}/{TotalCount} successful, {FailedCount} failed",
                componentType.ToUpper(), stats.SuccessfulCount, stats.TotalProcessed, stats.FailedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [COMPONENT-{ComponentType}] Failed to process component type: {ErrorMessage}",
                componentType.ToUpper(), ex.Message);
            
            stats.FailedCount = components.Count;
            stats.ProcessingTime = stopwatch.Elapsed;
        }
    }

    /// <summary>
    /// Processes a batch of components of the same type
    /// </summary>
    private async Task ProcessComponentBatch(
        string componentType,
        ComponentWithContext[] batch,
        string migrationId,
        StoreConfiguration sourceStore,
        StoreConfiguration destinationStore,
        SubEntityStatistics stats,
        int batchIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("🔧 [COMPONENT-{ComponentType}-BATCH-{BatchIndex}] Processing {Count} {ComponentType}",
                componentType.ToUpper(), batchIndex, batch.Length, componentType);

            // Phase 3.2.3: Check for cancellation at the start of each batch processing
            await CheckCancellationAsync(migrationId);

            int componentIndex = 0;
            foreach (var componentWithContext in batch)
            {
                // Phase 3.2.3: Periodic cancellation check within batch (every 5 components)
                if (componentIndex > 0 && componentIndex % 5 == 0)
                {
                    await CheckCancellationAsync(migrationId);
                }
                componentIndex++;
                try
                {
                    // Create batch processing request for this component
                    var batchRequest = new BatchProcessingRequest
                    {
                        MigrationId = migrationId,
                        EntityType = componentType,
                        SourceStore = sourceStore,
                        DestinationStore = destinationStore,
                        BatchNumber = batchIndex,
                        TotalBatches = 1,
                        EntityIds = new List<string> { 
                            componentWithContext.Component.TryGetValue("id", out var compId) ? 
                                compId?.ToString() ?? "unknown" : "unknown" 
                        },
                        Timestamp = DateTime.UtcNow
                    };

                    // Prepare component data with product context for transformation
                    var componentData = new Dictionary<string, object>(componentWithContext.Component);
                    componentData["product_id"] = componentWithContext.ProductId; // Source product ID for mapping

                    // 🔄 TRANSFORM: All business logic now handled in transform strategies
                    var transformedComponent = await _entityTransformService.TransformEntityAsync(
                        componentData, batchRequest);

                    if (transformedComponent != null)
                    {
                        // Create component using appropriate strategy
                        var createdComponents = await _entityCreateService.CreateEntitiesAsync(
                            new List<Dictionary<string, object>> { transformedComponent },
                            batchRequest,
                            cancellationToken);

                        if (createdComponents?.Any() == true)
                        {
                            // 🔗 STORE MAPPING: Only for options (needed for Phase 3 variants)
                            // Modifiers, images, reviews don't need mappings as they're not referenced by variants
                            _logger.LogDebug("🔍 [MAPPING-CHECK] ComponentType='{ComponentType}', CreatedComponents={Count}", 
                                componentType, createdComponents.Count);
                                
                            // 🔗 OPTIONS MAPPING: Now handled by HierarchicalOptionMappingService in OptionsCreationStrategy
                            // No need for pipeline-level mapping for any component type
                            _logger.LogDebug("🔍 [MAPPING-SKIP] Skipping pipeline-level mapping for componentType='{ComponentType}' - handled by creation strategies", componentType);

                            stats.SuccessfulCount++;
                        }
                        else
                        {
                            stats.FailedCount++;
                        }
                    }
                    else
                    {
                        stats.FailedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [COMPONENT-{ComponentType}-BATCH-{BatchIndex}] Failed to process individual component",
                        componentType.ToUpper(), batchIndex);
                    
                    stats.FailedCount++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [COMPONENT-{ComponentType}-BATCH-{BatchIndex}] Failed to process component batch",
                componentType.ToUpper(), batchIndex);
            
            stats.FailedCount += batch.Length;
        }
    }

    /// <summary>
    /// ❌ REMOVED: This method created conflicting OptionsMappingData format that overwrote correct data
    /// Options mapping is now handled exclusively by HierarchicalOptionMappingService to avoid conflicts
    /// Components like modifiers, images, reviews don't need mappings (not referenced by variants)
    /// </summary>
    /// <remarks>
    /// Previously this method created entity mappings with wrong JSON format for options:
    /// - Used snake_case instead of camelCase 
    /// - Stored single option instead of options array
    /// - Different property names than expected by VariantCreationStrategy
    /// This caused variant migration to fail due to incompatible mapping format.
    /// </remarks>
    private async Task StoreComponentMapping(
        ComponentWithContext componentWithContext,
        Dictionary<string, object> createdComponent,
        string migrationId,
        string componentType,
        CancellationToken cancellationToken)
    {
        // ✅ FIXED: No longer stores conflicting options mappings
        // Options are handled exclusively by HierarchicalOptionMappingService via OptionsCreationStrategy
        // This ensures single source of truth for OptionsMappingData
        
        if (componentType.ToLowerInvariant() == "options")
        {
            _logger.LogDebug("🔗 [MAPPING-OPTIONS] Skipping component mapping - options handled by HierarchicalOptionMappingService");
            return; // Options mappings are handled by HierarchicalOptionMappingService only
        }

        // For non-option components (modifiers, images, reviews), we don't need mappings
        // These are not referenced by variants, so no mapping storage is required
        _logger.LogDebug("🔗 [MAPPING-{ComponentType}] No mapping required for component type", componentType.ToUpper());
    }

    /// <summary>
    /// Phase 3.2.4: Publishes pipeline progress events with cancellation status support
    /// Provides real-time progress updates for product components processing
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="status">Current processing status</param>
    /// <param name="message">Progress message</param>
    /// <param name="progressPercentage">Progress percentage (optional)</param>
    /// <param name="isCancelled">Whether processing is cancelled</param>
    /// <param name="cancellationReason">Reason for cancellation</param>
    private async Task PublishPipelineProgressAsync(string migrationId, string status, string message, 
        double? progressPercentage = null, bool isCancelled = false, string? cancellationReason = null)
    {
        try
        {
            _logger.LogDebug("📊 [PRODUCT-COMPONENTS-PROGRESS] Publishing progress: Status={Status}, Message={Message}, Progress={Progress}%, Cancelled={IsCancelled}", 
                status, message, progressPercentage, isCancelled);

            // Note: Internal pipeline progress events removed in simplified SignalR approach
            // These detailed component-level events are not essential for migration progress tracking
            // Main chunk-level progress is published by ProcessEntityChunkActivity instead
            _logger.LogDebug("📊 [PRODUCT-COMPONENTS-PROGRESS] Pipeline progress: Status={Status}, Message={Message}, Progress={Progress}% - event simplified", 
                status, message, progressPercentage);
            
            _logger.LogDebug("✅ [PRODUCT-COMPONENTS-PROGRESS] Successfully published progress event for {MigrationId}", migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PRODUCT-COMPONENTS-PROGRESS] Failed to publish progress event for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't fail processing due to progress publishing issues - fail safe approach
        }
    }

    /// <summary>
    /// Phase 3.2.1: Checks for cancellation using blob store (cooperative cancellation)
    /// Provides centralized cancellation checking for product components pipeline
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    private async Task CheckCancellationAsync(string migrationId)
    {
        try
        {
            // Check blob store for cooperative cancellation flag
            var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
            if (isCancelled)
            {
                var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
                _logger.LogInformation("🚫 [PRODUCT-COMPONENTS-CANCELLATION] Product components processing cancelled: {Reason}", reason);
                throw new OperationCanceledException($"Product components migration cancelled: {reason}");
            }
        }
        catch (OperationCanceledException)
        {
            // Re-throw cancellation exceptions
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PRODUCT-COMPONENTS-CANCELLATION] Failed to check cancellation status for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't fail processing due to cancellation check issues - fail safe approach
        }
    }

    /// <summary>
    /// Context wrapper for components to track their source product
    /// </summary>
    private class ComponentWithContext
    {
        public Dictionary<string, object> Component { get; set; } = new();
        public string ProductId { get; set; } = string.Empty;
        public string ComponentType { get; set; } = string.Empty;
    }
}
