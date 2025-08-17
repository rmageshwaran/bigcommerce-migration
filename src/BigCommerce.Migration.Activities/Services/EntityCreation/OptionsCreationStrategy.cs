using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigCommerce.Migration.Activities.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating product variant options in destination stores
/// Based on: https://developer.bigcommerce.com/docs/rest-catalog/product-variant-options#create-a-product-variant-option
/// Implements Open/Closed Principle: specific to variant options without modifying base service
/// Phase 3.3.4.1: Enhanced with blob-based cooperative cancellation support
/// </summary>
public class OptionsCreationStrategy : IEntityCreationStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<OptionsCreationStrategy> _logger;
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly IHierarchicalOptionMappingService _hierarchicalOptionMappingService;

    public OptionsCreationStrategy(
        IBigCommerceApiClient apiClient,
        ICancellationStore cancellationStore,
        ILogger<OptionsCreationStrategy> logger,
        IApiRequestHandler apiRequestHandler,
        IMigrationStorageService migrationStorageService,
        IHierarchicalOptionMappingService hierarchicalOptionMappingService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
        _hierarchicalOptionMappingService = hierarchicalOptionMappingService ?? throw new ArgumentNullException(nameof(hierarchicalOptionMappingService));
    }

    /// <summary>
    /// The entity type this strategy handles
    /// </summary>
    public string EntityType => "options";

    /// <summary>
    /// Creates product variant options in the destination store
    /// </summary>
    /// <param name="entities">Options to create</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (not used for options)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created options with destination IDs</returns>
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("No options provided for creation in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        var executionId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("🎛️ [OPT-{ExecutionId}] Creating {Count} options for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        // Phase 3.3.4.1: Check for cancellation at start of options creation
        await CheckCancellationAsync(migrationId);

        try
        {
            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // Log option details for debugging
            foreach (var option in entities)
            {
                var optionName = option.TryGetValue("display_name", out var name) ? name?.ToString() : "unknown";
                var productId = option.TryGetValue("product_id", out var prodId) ? prodId?.ToString() : "unknown";
                
                _logger.LogDebug("🎛️ [OPT-{ExecutionId}] Creating option: Name='{OptionName}', ProductId='{ProductId}' in migration {MigrationId}",
                    executionId, optionName, productId, migrationId);
            }

            var createdOptions = new List<Dictionary<string, object>>();

            int optionIndex = 0;
            foreach (var option in entities)
            {
                // Phase 3.3.4.1: Periodic cancellation check (every 5 options)
                if (optionIndex > 0 && optionIndex % 5 == 0)
                {
                    await CheckCancellationAsync(migrationId);
                }
                optionIndex++;

                try
                {
                    if (!option.TryGetValue("product_id", out var productId))
                    {
                        _logger.LogWarning("🎛️ [OPT-{ExecutionId}] Option missing product_id for migration {MigrationId}", 
                            executionId, migrationId);
                        continue;
                    }

                    // Create option using BigCommerce API
                    // Based on: https://developer.bigcommerce.com/docs/rest-catalog/product-variant-options#create-a-product-variant-option
                    var createdOption = await CreateOptionAsync(option, destinationStore, migrationId, executionId, cancellationToken);
                    if (createdOption != null)
                    {
                        createdOptions.Add(createdOption);
                    }
                    
                    var optionName = option.TryGetValue("display_name", out var name) ? name?.ToString() : "no-name";
                    _logger.LogDebug("🎛️ [OPT-{ExecutionId}] Successfully created option '{OptionName}' for product {ProductId} in migration {MigrationId}", 
                        executionId, optionName, productId, migrationId);
                }
                catch (Exception ex)
                {
                    // 🚫 CANCELLATION FIX: Distinguish between actual failures and cancellation-induced failures
                    bool isCancellationError = ex.Message.Contains("Migration cancelled", StringComparison.OrdinalIgnoreCase) ||
                                             ex.Message.Contains("User requested cancellation", StringComparison.OrdinalIgnoreCase) ||
                                             ex is OperationCanceledException;
                    
                    if (isCancellationError)
                    {
                        var optionName = option.TryGetValue("display_name", out var name) ? name?.ToString() : "unknown";
                        var productId = option.TryGetValue("product_id", out var prodId) ? prodId?.ToString() : "unknown";
                        
                        _logger.LogInformation("🚫 [OPT-{ExecutionId}] Option '{OptionName}' for product {ProductId} was cancelled in migration {MigrationId}: {ErrorMessage}",
                            executionId, optionName, productId, migrationId, ex.Message);
                        
                        // Return a special object to indicate cancellation
                        createdOptions.Add(new Dictionary<string, object>
                        {
                            ["status"] = "cancelled",
                            ["reason"] = "migration_cancelled",
                            ["original_product_id"] = productId,
                            ["display_name"] = optionName
                        });
                    }
                    else
                    {
                        _logger.LogError(ex, "🎛️ [OPT-{ExecutionId}] Failed to create individual option for migration {MigrationId}", 
                            executionId, migrationId);
                        // Continue with next option instead of failing entire batch
                    }
                }
            }

            _logger.LogInformation("✅ [OPT-{ExecutionId}] Successfully created {CreatedCount}/{TotalCount} options for migration {MigrationId}", 
                executionId, createdOptions.Count, entities.Count, migrationId);

            return createdOptions;
        }
        catch (Exception ex)
        {
            // Extract detailed API error information
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "🎛️ [OPT-{ExecutionId}] ❌ Failed to create {OptionCount} options in migration {MigrationId}. {DetailedError}",
                executionId, entities.Count, migrationId, detailedErrorMessage);

            // Return empty list to avoid causing Durable Functions replay issues
            _logger.LogWarning("🎛️ [OPT-{ExecutionId}] Returning empty result to prevent replay in migration {MigrationId}", 
                executionId, migrationId);
            
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Validates the destination store configuration
    /// </summary>
    /// <param name="destinationStore">Store configuration to validate</param>
    /// <exception cref="ArgumentNullException">Thrown when store configuration is null</exception>
    /// <exception cref="ArgumentException">Thrown when store configuration is invalid</exception>
    private void ValidateStoreConfiguration(StoreConfiguration destinationStore)
    {
        if (destinationStore == null)
            throw new ArgumentNullException(nameof(destinationStore));

        if (string.IsNullOrWhiteSpace(destinationStore.StoreId))
            throw new ArgumentException("Store ID cannot be null or empty", nameof(destinationStore));

        if (string.IsNullOrWhiteSpace(destinationStore.AccessToken))
            throw new ArgumentException("Access token cannot be null or empty", nameof(destinationStore));
    }

    /// <summary>
    /// Extracts detailed error message from exception for better error reporting
    /// </summary>
    /// <param name="exception">Exception to extract message from</param>
    /// <returns>Detailed error message</returns>
    private string ExtractDetailedErrorMessage(Exception? exception)
    {
        if (exception == null) return string.Empty;

        // Check for inner exceptions with detailed error messages
        var innerException = exception.InnerException;
        while (innerException != null)
        {
            if (!string.IsNullOrEmpty(innerException.Message) && 
                innerException.Message != exception.Message)
            {
                return innerException.Message;
            }
            innerException = innerException.InnerException;
        }

        return exception.Message;
    }

    /// <summary>
    /// Creates a single option using BigCommerce API
    /// Based on: https://developer.bigcommerce.com/docs/rest-catalog/product-variant-options#create-a-product-variant-option
    /// </summary>
    private async Task<Dictionary<string, object>?> CreateOptionAsync(
        Dictionary<string, object> option,
        StoreConfiguration destinationStore,
        string migrationId,
        string executionId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!option.TryGetValue("product_id", out var productIdObj) || productIdObj == null)
            {
                _logger.LogWarning("🎛️ [OPT-{ExecutionId}] Cannot create option - missing product_id", executionId);
                return null;
            }

            // Use product_id directly (already mapped by transform strategy)
            var destinationProductId = productIdObj.ToString()!;
            _logger.LogDebug("🎛️ [OPT-{ExecutionId}] Using product_id {ProductId} (already mapped by transform strategy)", 
                executionId, destinationProductId);

            // Prepare option payload for BigCommerce API
            var optionPayload = PrepareOptionPayload(option, destinationProductId);
            
            // Build the API request URL - options are created under specific products
            var url = $"{destinationStore.GetApiBaseUrl()}/catalog/products/{destinationProductId}/options";

            // Serialize the option data
            var jsonContent = JsonSerializer.Serialize(optionPayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            _logger.LogInformation("🎛️ [OPT-{ExecutionId}] 🌐 API CALL: URL={Url}, ProductId={ProductId}", 
                executionId, url, destinationProductId);
            
            _logger.LogInformation("🔍 [OPT-{ExecutionId}] EXACT API PAYLOAD: {JsonPayload}", 
                executionId, jsonContent);
            
            _logger.LogDebug("🔍 [OPT-{ExecutionId}] REQUEST DETAILS: Method=POST, ContentType=application/json, PayloadSize={PayloadSize} bytes", 
                executionId, jsonContent.Length);

            // Create and execute the API request
            var request = ApiRequest.CreatePost(url, jsonContent, destinationStore);
            
            var apiStartTime = DateTime.UtcNow;
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            var apiDuration = DateTime.UtcNow - apiStartTime;
            
            _logger.LogInformation("🎛️ [OPT-{ExecutionId}] ✅ API SUCCESS: ProductId={ProductId}, Duration={Duration}ms", 
                executionId, destinationProductId, apiDuration.TotalMilliseconds);
            
            // Handle the response data 
            Dictionary<string, object>? createdOption = null;
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                createdOption = JsonSerializer.Deserialize<Dictionary<string, object>>(dataElement.GetRawText());
            }
            else if (response != null && response.ContainsKey("id"))
            {
                createdOption = response;
            }

            if (createdOption != null)
            {
                var createdId = createdOption.TryGetValue("id", out var id) ? id.ToString() : "unknown";
                
                // ✅ Option values are now created automatically with the option (included in payload)
                _logger.LogDebug("✅ [OPT-{ExecutionId}] Successfully created option with ID {OptionId} for product {ProductId}",
                    executionId, createdId, destinationProductId);
                
                // 🔗 HIERARCHICAL MAPPING: Store option mapping for Phase 3 variant migration
                try
                {
                    // 🔍 STAGE 4 DEBUG: Log complete input payload to creation strategy
                    _logger.LogInformation("🔍 [STAGE-4-CREATION-INPUT] [OPT-{ExecutionId}] ===== COMPLETE INPUT PAYLOAD ===== {InputPayload}", 
                        executionId, JsonSerializer.Serialize(option, new JsonSerializerOptions { WriteIndented = true }));
                    
                    var sourceOptionId = option.TryGetValue("_source_option_id", out var srcId) ? srcId?.ToString() : null;
                    var sourceProductId = GetSourceProductIdFromOption(option, migrationId);
                    
                    // Log retrieved values
                    _logger.LogInformation("🔍 [OPT-{ExecutionId}] Retrieved sourceOptionId: '{SourceOptionId}', sourceProductId: '{SourceProductId}'", 
                        executionId, sourceOptionId ?? "NULL", sourceProductId ?? "NULL");
                    
                    // Check specific field presence
                    _logger.LogInformation("🔍 [OPT-{ExecutionId}] Field check - _source_option_id exists: {HasSourceOptionId}, _source_product_id exists: {HasSourceProductId}", 
                        executionId, option.ContainsKey("_source_option_id"), option.ContainsKey("_source_product_id"));
                    
                    if (!string.IsNullOrEmpty(sourceOptionId) && !string.IsNullOrEmpty(sourceProductId))
                    {
                        _logger.LogInformation("🔗 [OPT-{ExecutionId}] DEBUG: About to call HierarchicalOptionMappingService with sourceOptionId: {SourceOptionId}, destinationOptionId: {DestinationOptionId}", 
                            executionId, sourceOptionId, createdId);
                        _logger.LogInformation("🔗 [OPT-{ExecutionId}] DEBUG: Service instance is null: {IsNull}", 
                            executionId, _hierarchicalOptionMappingService == null);
                            
                        await _hierarchicalOptionMappingService.StoreHierarchicalOptionMappingAsync(
                            option, createdOption, sourceOptionId, createdId, sourceProductId, migrationId, cancellationToken);
                            
                        _logger.LogInformation("✅ [OPT-{ExecutionId}] Successfully stored hierarchical option mapping for sourceOptionId: {SourceOptionId}, sourceProductId: {SourceProductId}", 
                            executionId, sourceOptionId, sourceProductId);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [OPT-{ExecutionId}] Cannot store hierarchical mapping - missing sourceOptionId or sourceProductId", executionId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [OPT-{ExecutionId}] Failed to store hierarchical option mapping", executionId);
                    // Don't fail the option creation for this
                }
                
                return createdOption;
            }

            _logger.LogWarning("⚠️ [OPT-{ExecutionId}] Unexpected API response format for option creation on product {ProductId}",
                executionId, destinationProductId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🎛️ [OPT-{ExecutionId}] Failed to create option", executionId);
            return null;
        }
    }

    /// <summary>
    /// Creates a single option value using BigCommerce API
    /// Based on: https://developer.bigcommerce.com/docs/rest-catalog/product-variant-options#create-a-product-variant-option-value
    /// </summary>
    private async Task<Dictionary<string, object>?> CreateOptionValueAsync(
        Dictionary<string, object> optionValue,
        string destinationOptionId,
        string destinationProductId,
        StoreConfiguration destinationStore,
        string executionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var optionValuePayload = PrepareOptionValuePayload(optionValue, destinationOptionId);
            
            // Build the API request URL for option values
            var url = $"{destinationStore.GetApiBaseUrl()}/catalog/products/{destinationProductId}/options/{destinationOptionId}/values";

            // Serialize the option value data
            var jsonContent = JsonSerializer.Serialize(optionValuePayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            _logger.LogDebug("🎛️ [OPT-VAL-{ExecutionId}] 🌐 API CALL: URL={Url}, OptionId={OptionId}", 
                executionId, url, destinationOptionId);
            
            // Create and execute the API request
            var request = ApiRequest.CreatePost(url, jsonContent, destinationStore);
            
            var apiStartTime = DateTime.UtcNow;
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            var apiDuration = DateTime.UtcNow - apiStartTime;
            
            _logger.LogDebug("🎛️ [OPT-VAL-{ExecutionId}] ✅ API SUCCESS: OptionId={OptionId}, Duration={Duration}ms", 
                executionId, destinationOptionId, apiDuration.TotalMilliseconds);
            
            // Handle the response data 
            Dictionary<string, object>? createdOptionValue = null;
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                createdOptionValue = JsonSerializer.Deserialize<Dictionary<string, object>>(dataElement.GetRawText());
            }
            else if (response != null && response.ContainsKey("id"))
            {
                createdOptionValue = response;
            }

            if (createdOptionValue != null)
            {
                var createdId = createdOptionValue.TryGetValue("id", out var id) ? id.ToString() : "unknown";
                _logger.LogDebug("✅ [OPT-VAL-{ExecutionId}] Successfully created option value with ID {OptionValueId} for option {OptionId}",
                    executionId, createdId, destinationOptionId);
                return createdOptionValue;
            }

            _logger.LogWarning("⚠️ [OPT-VAL-{ExecutionId}] Unexpected API response format for option value creation on option {OptionId}",
                executionId, destinationOptionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🎛️ [OPT-VAL-{ExecutionId}] Failed to create option value for option {OptionId}: {ErrorMessage}",
                executionId, destinationOptionId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Gets destination product ID from entity mapping
    /// </summary>
    private async Task<string?> GetDestinationProductId(string sourceProductId, string migrationId)
    {
        try
        {
            var mapping = await _migrationStorageService.GetEntityMappingAsync(migrationId, "products", sourceProductId);
            return mapping?.DestinationId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get destination product ID for source product {SourceProductId} in migration {MigrationId}", 
                sourceProductId, migrationId);
            return null;
        }
    }

    /// <summary>
    /// Prepares option payload according to BigCommerce API requirements
    /// </summary>
    private Dictionary<string, object> PrepareOptionPayload(Dictionary<string, object> sourceOption, string destinationProductId)
    {
        var payload = new Dictionary<string, object>();

        // ✅ Copy ALL fields from source option WITHOUT hardcoded defaults  
        foreach (var kvp in sourceOption)
        {
            // Skip system fields that shouldn't be migrated
            if (kvp.Key == "id" || kvp.Key == "product_id" || kvp.Key == "_source_product_id" || kvp.Key == "_source_option_id") continue;
            
            // Debug: Log option_values type to understand the issue
            if (kvp.Key == "option_values")
            {
                _logger.LogInformation("🔍 DEBUG: option_values type is: {Type}, value: {Value}", 
                    kvp.Value?.GetType().Name ?? "null", kvp.Value?.ToString() ?? "null");
            }

            // Handle option_values specially to ensure proper structure
            if (kvp.Key == "option_values")
            {
                var optionValues = new List<Dictionary<string, object>>();
                IEnumerable<Dictionary<string, object>> sourceOptionValues = null;
                
                // Handle different list types
                if (kvp.Value is List<object> objectList)
                {
                    sourceOptionValues = objectList.Cast<Dictionary<string, object>>();
                }
                else if (kvp.Value is List<Dictionary<string, object>> dictList)
                {
                    sourceOptionValues = dictList;
                }
                else
                {
                    _logger.LogWarning("🚨 Unknown option_values type: {Type}", kvp.Value?.GetType().Name ?? "null");
                    payload[kvp.Key] = kvp.Value;
                    continue;
                }
                
                foreach (var optionValueData in sourceOptionValues)
                {
                    var optionValuePayload = new Dictionary<string, object>();
                    
                    // Copy ALL option value fields except system fields that cause API errors
                    foreach (var optionValueKvp in optionValueData)
                    {
                        // ✅ CRITICAL FIX: Exclude internal fields - BigCommerce API rejects these fields during creation
                        if (optionValueKvp.Key == "id" || optionValueKvp.Key == "option_id" || optionValueKvp.Key == "_source_option_value_id") 
                        {
                            _logger.LogDebug("🚫 EXCLUDED field from API payload: {FieldName}={FieldValue}", 
                                optionValueKvp.Key, optionValueKvp.Value);
                            continue;
                        }
                        _logger.LogDebug("✅ INCLUDED field in API payload: {FieldName}={FieldValue}", 
                            optionValueKvp.Key, optionValueKvp.Value);
                        optionValuePayload[optionValueKvp.Key] = optionValueKvp.Value;
                    }
                    
                    optionValues.Add(optionValuePayload);
                }
                
                payload["option_values"] = optionValues;
            }
            else
            {
                // Copy all other fields exactly as they are in source
                payload[kvp.Key] = kvp.Value;
            }
        }

        // Set destination product ID (the only field we need to override)
        payload["product_id"] = destinationProductId;

        return payload;
    }

    /// <summary>
    /// Prepares option value payload according to BigCommerce API requirements
    /// </summary>
    private Dictionary<string, object> PrepareOptionValuePayload(Dictionary<string, object> sourceOptionValue, string optionId)
    {
        var payload = new Dictionary<string, object>();

        // Copy essential fields
        if (sourceOptionValue.TryGetValue("label", out var label)) payload["label"] = label;
        if (sourceOptionValue.TryGetValue("sort_order", out var sortOrder)) payload["sort_order"] = sortOrder;
        if (sourceOptionValue.TryGetValue("is_default", out var isDefault)) payload["is_default"] = isDefault;

        // Set option ID
        payload["option_id"] = optionId;

        return payload;
    }

    /// <summary>
    /// Gets the source product ID from the option for hierarchical mapping
    /// The source product ID should be added by the transform strategy
    /// </summary>
    private string? GetSourceProductIdFromOption(Dictionary<string, object> option, string migrationId)
    {
        _logger.LogInformation("🔍 [OPTIONS-CREATION] GetSourceProductIdFromOption called - checking for _source_product_id in option data");
        
        // The source product ID should be added by the transform strategy for hierarchical mapping
        if (option.TryGetValue("_source_product_id", out var sourceProductIdObj))
        {
            var result = sourceProductIdObj?.ToString();
            _logger.LogInformation("✅ [OPTIONS-CREATION] Found _source_product_id: '{SourceProductId}'", result ?? "NULL");
            return result;
        }

        _logger.LogWarning("⚠️ [OPTIONS-CREATION] Missing _source_product_id in option data for migration {MigrationId}. Available keys: {Keys}", 
            migrationId, string.Join(", ", option.Keys));
        return null;
    }

    /// <summary>
    /// Phase 3.3.4.1: Helper method to check for blob-based cancellation
    /// Uses cooperative cancellation pattern suitable for Azure Functions activities
    /// </summary>
    private async Task CheckCancellationAsync(string migrationId)
    {
        try
        {
            var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
            if (isCancelled)
            {
                var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
                _logger.LogInformation("🚫 [CANCELLATION] Options creation cancelled: {Reason}", reason);
                throw new OperationCanceledException($"Migration cancelled: {reason}");
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CANCELLATION-CHECK] Failed to check cancellation flag for migration {MigrationId}: {ErrorMessage}",
                migrationId, ex.Message);
            // Don't throw - continue processing if cancellation check fails
        }
    }
}