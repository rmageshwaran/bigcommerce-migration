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
/// Handles product components (options, modifiers, reviews) for existing products
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
    private readonly IProgressTracker _progressTracker; // 🎯 CUMULATIVE PROGRESS: For proper progress integration
    private readonly ICentralizedProgressBroadcastService? _centralizedBroadcastService; // 🚨 FIX: Optional broadcast service for real-time updates

    public ProductComponentsMigrationPipeline(
        ISignalREventFactory signalREventFactory,
        IProgressEventPublisher progressEventPublisher,
        ILogger<ProductComponentsMigrationPipeline> logger,
        IEntityCreateService entityCreateService,
        IEntityTransformService entityTransformService,
        IMigrationStorageService migrationStorageService,
        IEntityErrorHandlingService errorHandlingService,
        ICancellationStore cancellationStore,
        IProgressTracker progressTracker)
    {
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entityCreateService = entityCreateService ?? throw new ArgumentNullException(nameof(entityCreateService));
        _entityTransformService = entityTransformService ?? throw new ArgumentNullException(nameof(entityTransformService));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _errorHandlingService = errorHandlingService ?? throw new ArgumentNullException(nameof(errorHandlingService));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore)); // ✅ Phase 3.2.1: Native cancellation injection
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker)); // 🎯 CUMULATIVE PROGRESS: Store progress tracker reference
        _centralizedBroadcastService = null; // 🚨 FIX: Service not available in Activities project DI scope
    }

    /// <summary>
    /// Processes product components (options, modifiers, reviews) for existing products
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

            // Phase 3.2.4: Pipeline status logged (individual component events provide UI progress)
            _logger.LogInformation("🚀 [PRODUCT-COMPONENTS-PIPELINE] Initializing product components processing - individual component events will provide UI progress");

            // 🔗 EXTRACT ALL COMPONENTS: Parse all products and extract components by type
            // Phase 3.2.2: Enhanced with periodic cancellation checks during extraction
            var allComponents = await ExtractAllComponentsFromProductsAsync(productsWithComponents, migrationId);
            
            _logger.LogInformation("🔍 [PRODUCT-COMPONENTS-PIPELINE] Extracted components: Options={OptionsCount}, Modifiers={ModifiersCount}, Reviews={ReviewsCount}", 
                allComponents["options"].Count, allComponents["modifiers"].Count, allComponents["reviews"].Count);

            // 🎯 INDIVIDUAL COMPONENT PROGRESS INTEGRATION: Initialize progress tracking for each component
            await InitializeIndividualComponentProgressAsync(migrationId, allComponents);

            // Phase 3.2.1: Check for cancellation after component extraction
            await CheckCancellationAsync(migrationId);

            // Phase 3.2.4: Log extraction completion (individual component events provide UI progress)
            var totalComponents = allComponents.Values.Sum(c => c.Count);
            _logger.LogInformation("🔍 [PRODUCT-COMPONENTS-PIPELINE] Extracted {TotalComponents} components from {ProductCount} products - individual component discovery events published", 
                totalComponents, productsWithComponents.Count);

            // Initialize component statistics with actual counts
            var componentTypes = new[] { "options", "modifiers", "reviews" };
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

            // 🎯 INDIVIDUAL COMPONENT COMPLETION: Update progress tracker with final statistics for cumulative tracking
            await CompleteIndividualComponentProgressAsync(migrationId, result.SubEntityStatistics);

            // Phase 3.2.4: Log processing completion (individual component events provide UI progress)
            _logger.LogInformation("✅ [PRODUCT-COMPONENTS-PIPELINE] Completed component processing - individual component completion events published");

            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;



            _logger.LogInformation("✅ [PRODUCT-COMPONENTS-PIPELINE] Completed product components migration for {ProductCount} products. " +
                                 "Options: {OptionsCount}, Modifiers: {ModifiersCount}, Reviews: {ReviewsCount}. " +
                                 "Duration: {Duration}ms",
                productsWithComponents.Count,
                result.SubEntityStatistics["options"].SuccessfulCount,
                result.SubEntityStatistics["modifiers"].SuccessfulCount,
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
            
            // Log final completion (individual component completion events already published)
            _logger.LogInformation("✅ [PRODUCT-COMPONENTS-PIPELINE] Successfully processed {TotalSuccessful}/{TotalProcessed} components - individual component events provide UI visibility", 
                totalSuccessful, totalProcessed);

            return result;
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;

            _logger.LogWarning(ex, "🚫 [PRODUCT-COMPONENTS-PIPELINE] Product components processing cancelled for migration {MigrationId}", migrationId);
            
            // Phase 3.2.4: Log cancellation status (individual component events handle UI updates)
            _logger.LogWarning("🚫 [PRODUCT-COMPONENTS-PIPELINE] Product components processing cancelled: {ErrorMessage} - individual component events will reflect cancellation", ex.Message);
            
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;

            _logger.LogError(ex, "❌ [PRODUCT-COMPONENTS-PIPELINE] Failed to process product components for migration {MigrationId}", migrationId);
            
            // Phase 3.2.4: Log failure status (individual component events handle UI updates)
            _logger.LogError("❌ [PRODUCT-COMPONENTS-PIPELINE] Product components processing failed: {ErrorMessage} - individual component events will reflect failures", ex.Message);
            
            throw;
        }
    }

    /// <summary>
    /// Processes components (options, modifiers, reviews) for a single product
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
        // Process Reviews
        if (product.ContainsKey("reviews") && product["reviews"] is List<object> reviews)
        {
            await ProcessComponentType("reviews", reviews.Cast<Dictionary<string, object>>().ToList(),
                productId, migrationId, sourceStore, destinationStore, result, cancellationToken);
        }
    }

    /// <summary>
    /// Processes a specific component type (options, modifiers, or reviews) for a product
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
                        // 🔗 NO ENTITY MAPPING: Components (options, modifiers, reviews) don't need individual EntityMapping records
                        // - Options: Use hierarchical mapping in OptionsCreationStrategy for variant migration
                        // - Modifiers/Reviews: No dependents, no mapping needed
                        
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
        var averageComponentsPerProduct = 15; // 5 options + 5 modifiers + 5 reviews
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

            _logger.LogInformation("🔧 [OPTIONS-MAPPING-UPDATE] Updating OptionsMappingData for product {SourceProductId} → {DestinationProductId}. JSON length: {JsonLength}",
                sourceProductId, productMapping.DestinationId, jsonString.Length);

            await _migrationStorageService.UpdateEntityMappingAsync(productMapping);

            _logger.LogInformation("✅ [OPTIONS-MAPPING-UPDATE] Successfully updated OptionsMappingData for product {SourceProductId}",
                sourceProductId);

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
    /// Extracts all components (options, modifiers, reviews) from products
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
            var hasReviews = product.ContainsKey("reviews");
            
            _logger.LogInformation("🔍 [EXTRACTION-DEBUG] Product {ProductId} component fields: Options={HasOptions}, Modifiers={HasModifiers}, Reviews={HasReviews}", 
                productId, hasOptions, hasModifiers, hasReviews);
            
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

        _logger.LogInformation("🔍 [EXTRACTION-DEBUG] ✅ EXTRACTION COMPLETE: {OptionsCount} options, {ModifiersCount} modifiers, {ReviewsCount} reviews from {ProductCount} products",
            allComponents["options"].Count, allComponents["modifiers"].Count, allComponents["reviews"].Count, products.Count);

        // Phase 3.2.2: Final cancellation check before returning extraction results
        await CheckCancellationAsync(migrationId);

        return allComponents;
    }

    /// <summary>
    /// Processes a specific component type (options, modifiers, reviews) in parallel
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

            // 🎯 REAL-TIME PROGRESS: Update progress tracker with final component statistics for real-time UI updates
            await UpdateComponentProgressAsync(migrationId, componentType, stats);

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
                            // Modifiers, reviews don't need mappings as they're not referenced by variants
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
    /// Components like modifiers, reviews don't need mappings (not referenced by variants)
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

        // For non-option components (modifiers, reviews), we don't need mappings
        // These are not referenced by variants, so no mapping storage is required
        _logger.LogDebug("🔗 [MAPPING-{ComponentType}] No mapping required for component type", componentType.ToUpper());
    }

        /// <summary>
    /// 🎯 INDIVIDUAL COMPONENT PROGRESS: Pipeline-level events disabled in favor of individual component tracking
    /// Individual component discovery and completion events provide much better UI visibility and troubleshooting
    /// This method is kept for backward compatibility but no longer publishes SignalR events
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
        await Task.CompletedTask; // No-op to maintain async signature
        
        // 🎯 INDIVIDUAL COMPONENT PROGRESS: Pipeline-level events disabled
        // Individual component events (options, modifiers, reviews) provide much better visibility:
        // - PublishIndividualComponentDiscoveryAsync() publishes discovery events
        // - PublishIndividualComponentCompletionAsync() publishes completion events
        // This gives UI users separate progress bars for each component type for enhanced troubleshooting
        
        _logger.LogDebug("📊 [PRODUCT-COMPONENTS-PROGRESS] Pipeline progress: {Status} - individual component events provide UI progress", status);
    }

    /// <summary>
    /// Initializes individual component progress tracking with ProgressTracker for cumulative progress
    /// This integrates with the standard progress tracking system for proper cumulative counts
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="allComponents">Extracted components by type</param>
    private async Task InitializeIndividualComponentProgressAsync(string migrationId, Dictionary<string, List<ComponentWithContext>> allComponents)
    {
        try
        {
            var componentTypes = new[] { "options", "modifiers", "reviews" };
            
            foreach (var componentType in componentTypes)
            {
                var componentCount = allComponents[componentType].Count;
                
                _logger.LogInformation("📊 [COMPONENT-PROGRESS-INIT] Initializing progress tracking for {ComponentType}: {Count} components discovered", 
                    componentType, componentCount);

                // Update progress tracking with discovered count (EntityProgress entry already created by orchestrator)
                // This updates the TotalCount from 0 (progressive discovery) to actual discovered count
                await _progressTracker.UpdateProgressAsync(migrationId, new ProgressUpdate
                {
                    EntityType = componentType,
                    TotalCount = componentCount, // Update with actual discovered count
                    ProcessedCount = 0,
                    SuccessCount = 0,
                    FailureCount = 0,
                    SkippedCount = 0,
                    CancelledCount = 0
                });

                // 🚨 FIX: Enable ShowTotalCount once we have actual counts
                // Get the current progress and enable ShowTotalCount for UI display
                var currentProgress = await _progressTracker.GetProgressAsync(migrationId);
                if (currentProgress.EntityProgress.ContainsKey(componentType))
                {
                    currentProgress.EntityProgress[componentType].ShowTotalCount = true;
                    
                    _logger.LogInformation("✅ [SHOW-TOTAL-COUNT-FIX] Enabled total count display for {ComponentType} with count {Count}", 
                        componentType, componentCount);
                }
                
                _logger.LogInformation("✅ [COMPONENT-PROGRESS-INIT] Started progress tracking for {ComponentType} with total count {Count}", 
                    componentType, componentCount);
            }
            
            _logger.LogInformation("✅ [COMPONENT-PROGRESS-INIT] Successfully initialized progress tracking for all component types");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [COMPONENT-PROGRESS-INIT] Failed to initialize component progress tracking for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't fail processing due to progress tracking issues
        }
    }

    /// <summary>
    /// Updates individual component progress during processing for real-time UI updates
    /// Provides incremental progress tracking as components are processed
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="componentType">Component type (options, modifiers, reviews)</param>
    /// <param name="stats">Current processing statistics</param>
    private async Task UpdateComponentProgressAsync(string migrationId, string componentType, SubEntityStatistics stats)
    {
        try
        {
            _logger.LogInformation("📊 [COMPONENT-PROGRESS-UPDATE] Updating real-time progress for {ComponentType}: {Successful}/{Total} processed", 
                componentType, stats.SuccessfulCount, stats.TotalProcessed);

            // Update progress using ProgressTracker for cumulative tracking
            await _progressTracker.UpdateProgressAsync(migrationId, new ProgressUpdate
            {
                EntityType = componentType,
                ProcessedCount = stats.TotalProcessed,
                SuccessCount = stats.SuccessfulCount,
                FailureCount = stats.FailedCount,
                SkippedCount = stats.SkippedCount,
                CancelledCount = 0
                // TotalCount is not updated here - it was set during initialization
            });

            // 🚨 CRITICAL FIX: Add chunkincrementevents tracking for component entities
            // This provides same incremental tracking as other entities for consistency and cancellation recovery
            try
            {
                await _progressTracker.IncrementProgressAsync(
                    migrationId: migrationId,
                    entityType: componentType,
                    chunkNumber: 1, // Component processing is single "chunk"
                    chunkStartIndex: 0,
                    chunkSize: stats.TotalProcessed,
                    successfulEntities: stats.SuccessfulCount,
                    failedEntities: stats.FailedCount,
                    skippedEntities: stats.SkippedCount,
                    cancelledEntities: 0,
                    processingStartTime: DateTime.UtcNow.AddSeconds(-30), // Approximate start time
                    processingEndTime: DateTime.UtcNow,
                    sourceStore: "component-source", // Component entities don't have traditional source
                    destinationStore: "component-destination",
                    errors: null
                );

                _logger.LogInformation("✅ [COMPONENT-INCREMENTAL] Successfully recorded incremental progress for {ComponentType} in chunkincrementevents table", componentType);
            }
            catch (Exception incrementEx)
            {
                _logger.LogWarning(incrementEx, "⚠️ [COMPONENT-INCREMENTAL] Failed to record incremental progress for {ComponentType} - continuing without chunkincrementevents tracking", componentType);
                // Don't fail the entire component processing due to incremental tracking issues
            }

            // 🚨 FIX: Broadcast real-time progress updates via CentralizedProgressBroadcastService
            // Get the current progress from ProgressTracker to get cumulative counts and total
            var currentProgress = await _progressTracker.GetLatestAggregatedProgressAsync(migrationId);
            var entityProgress = currentProgress.EntityProgress.GetValueOrDefault(componentType);
            
            if (entityProgress != null)
            {
                var chunkProgress = new EntityChunkProgress
                {
                    EntityType = componentType,
                    ChunkNumber = 1, // Component processing doesn't use chunks, use 1
                    ChunkSize = stats.TotalProcessed,
                    ProcessedInChunk = stats.SuccessfulCount,
                    FailedInChunk = stats.FailedCount,
                    
                    // Use cumulative counts from ProgressTracker
                    CumulativeProcessed = entityProgress.ProcessedCount,
                    CumulativeFailed = entityProgress.FailureCount,
                    CumulativeSkipped = entityProgress.SkippedCount,
                    CumulativeCancelled = entityProgress.CancelledCount,
                    TotalEntitiesForType = entityProgress.TotalCount,
                    
                    ProgressPercentage = entityProgress.ProgressPercentage,
                    Status = entityProgress.Status,
                    ProcessingTimeMs = (long)entityProgress.ProcessingTime.TotalMilliseconds,
                    ShowTotalCount = entityProgress.ShowTotalCount
                };

                // Broadcast the progress update (if service is available)
                if (_centralizedBroadcastService != null)
                {
                    await _centralizedBroadcastService.BroadcastEntityChunkProgressAsync(migrationId, chunkProgress);
                }
                else
                {
                    _logger.LogWarning("⚠️ [COMPONENT-PROGRESS-BROADCAST] CentralizedProgressBroadcastService not available - skipping real-time broadcast for {ComponentType}", componentType);
                }
                
                _logger.LogInformation("📢 [COMPONENT-PROGRESS-BROADCAST] Broadcasted real-time progress for {ComponentType}: {Processed}/{Total}", 
                    componentType, entityProgress.ProcessedCount, entityProgress.TotalCount);
            }
            
            _logger.LogInformation("✅ [COMPONENT-PROGRESS-UPDATE] Successfully updated real-time progress for {ComponentType}", componentType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [COMPONENT-PROGRESS-UPDATE] Failed to update real-time progress for {ComponentType} in migration {MigrationId}: {ErrorMessage}",
                componentType, migrationId, ex.Message);
            // Don't fail processing due to progress tracking issues
        }
    }

    /// <summary>
    /// Completes individual component progress tracking with final statistics using ProgressTracker
    /// This integrates with the standard progress tracking system for proper cumulative progress and SignalR events
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="componentStatistics">Statistics for each component type</param>
    private async Task CompleteIndividualComponentProgressAsync(string migrationId, Dictionary<string, SubEntityStatistics> componentStatistics)
    {
        try
        {
            var componentTypes = new[] { "options", "modifiers", "reviews" };
            
            foreach (var componentType in componentTypes)
            {
                if (componentStatistics.TryGetValue(componentType, out var stats))
                {
                    _logger.LogInformation("📊 [COMPONENT-PROGRESS-UPDATE] Updating progress for {ComponentType}: {Successful}/{Total} successful", 
                        componentType, stats.SuccessfulCount, stats.TotalProcessed);

                    // Update progress using ProgressTracker for cumulative tracking and proper SignalR events
                    await _progressTracker.UpdateProgressAsync(migrationId, new ProgressUpdate
                    {
                        EntityType = componentType,
                        ProcessedCount = stats.TotalProcessed,
                        SuccessCount = stats.SuccessfulCount,
                        FailureCount = stats.FailedCount,
                        SkippedCount = stats.SkippedCount,
                        CancelledCount = 0,
                        TotalCount = stats.TotalProcessed // Set total to processed count for completion
                    });

                    // 🚨 CRITICAL FIX: Add final chunkincrementevents tracking for component completion
                    // This ensures component entities have same incremental tracking as other entity types
                    try
                    {
                        await _progressTracker.IncrementProgressAsync(
                            migrationId: migrationId,
                            entityType: componentType,
                            chunkNumber: 1, // Component processing is single "chunk" 
                            chunkStartIndex: 0,
                            chunkSize: stats.TotalProcessed,
                            successfulEntities: stats.SuccessfulCount,
                            failedEntities: stats.FailedCount,
                            skippedEntities: stats.SkippedCount,
                            cancelledEntities: 0,
                            processingStartTime: DateTime.UtcNow.AddMinutes(-1), // Approximate processing time
                            processingEndTime: DateTime.UtcNow,
                            sourceStore: "component-source", // Component entities are derived from products
                            destinationStore: "component-destination",
                            errors: null
                        );

                        _logger.LogInformation("✅ [COMPONENT-INCREMENTAL-COMPLETE] Successfully recorded final incremental progress for {ComponentType} in chunkincrementevents table", componentType);
                    }
                    catch (Exception incrementEx)
                    {
                        _logger.LogWarning(incrementEx, "⚠️ [COMPONENT-INCREMENTAL-COMPLETE] Failed to record final incremental progress for {ComponentType} - continuing", componentType);
                        // Don't fail completion due to incremental tracking issues
                    }
                    
                    // Complete the entity processing to trigger final events
                    await _progressTracker.CompleteEntityProcessingAsync(migrationId, componentType);
                    
                    // 🚨 FIX: Broadcast final completion status via CentralizedProgressBroadcastService
                    var finalProgress = await _progressTracker.GetLatestAggregatedProgressAsync(migrationId);
                    var finalEntityProgress = finalProgress.EntityProgress.GetValueOrDefault(componentType);
                    
                    if (finalEntityProgress != null)
                    {
                        var finalChunkProgress = new EntityChunkProgress
                        {
                            EntityType = componentType,
                            ChunkNumber = 1,
                            ChunkSize = stats.TotalProcessed,
                            ProcessedInChunk = stats.SuccessfulCount,
                            FailedInChunk = stats.FailedCount,
                            
                            // Use final cumulative counts
                            CumulativeProcessed = finalEntityProgress.ProcessedCount,
                            CumulativeFailed = finalEntityProgress.FailureCount,
                            CumulativeSkipped = finalEntityProgress.SkippedCount,
                            CumulativeCancelled = finalEntityProgress.CancelledCount,
                            TotalEntitiesForType = finalEntityProgress.TotalCount,
                            
                            ProgressPercentage = finalEntityProgress.ProgressPercentage,
                            Status = finalEntityProgress.Status,
                            ProcessingTimeMs = (long)finalEntityProgress.ProcessingTime.TotalMilliseconds,
                            ShowTotalCount = finalEntityProgress.ShowTotalCount
                        };

                        if (_centralizedBroadcastService != null)
                        {
                            await _centralizedBroadcastService.BroadcastEntityChunkProgressAsync(migrationId, finalChunkProgress);
                        }
                        
                        _logger.LogInformation("📢 [COMPONENT-COMPLETION-BROADCAST] Broadcasted completion status for {ComponentType}: {Status}", 
                            componentType, finalEntityProgress.Status);
                    }
                    
                    _logger.LogInformation("✅ [COMPONENT-PROGRESS-UPDATE] Completed progress tracking for {ComponentType}", componentType);
                }
            }
            
            _logger.LogInformation("✅ [COMPONENT-PROGRESS-COMPLETION] Successfully completed progress tracking for all component types");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [COMPONENT-PROGRESS-COMPLETION] Failed to complete component progress tracking for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't fail processing due to progress tracking issues
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
