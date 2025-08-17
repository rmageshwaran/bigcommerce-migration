using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigCommerce.Migration.Activities.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating product modifiers in destination stores
/// Implements Open/Closed Principle: specific to modifiers without modifying base service
/// Phase 3.3.4.2: Enhanced with blob-based cooperative cancellation support
/// </summary>
public class ModifierCreationStrategy : IEntityCreationStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<ModifierCreationStrategy> _logger;
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly IMigrationStorageService _migrationStorageService;

    public ModifierCreationStrategy(
        IBigCommerceApiClient apiClient,
        ICancellationStore cancellationStore,
        ILogger<ModifierCreationStrategy> logger,
        IApiRequestHandler apiRequestHandler,
        IMigrationStorageService migrationStorageService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiRequestHandler = apiRequestHandler ?? throw new ArgumentNullException(nameof(apiRequestHandler));
        _migrationStorageService = migrationStorageService ?? throw new ArgumentNullException(nameof(migrationStorageService));
    }

    /// <summary>
    /// The entity type this strategy handles
    /// </summary>
    public string EntityType => "modifiers";

    /// <summary>
    /// Creates product modifiers in the destination store
    /// </summary>
    /// <param name="entities">Modifiers to create</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (not used for modifiers)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created modifiers with destination IDs</returns>
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("No modifiers provided for creation in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        var executionId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("⚙️ [MOD-{ExecutionId}] Creating {Count} modifiers for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        // Phase 3.3.4.2: Check for cancellation at start of modifiers creation
        await CheckCancellationAsync(migrationId);

        try
        {
            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // Log modifier details for debugging
            foreach (var modifier in entities)
            {
                var modifierName = modifier.TryGetValue("display_name", out var name) ? name?.ToString() : "unknown";
                var productId = modifier.TryGetValue("product_id", out var prodId) ? prodId?.ToString() : "unknown";
                
                _logger.LogDebug("⚙️ [MOD-{ExecutionId}] Creating modifier: Name='{ModifierName}', ProductId='{ProductId}' in migration {MigrationId}",
                    executionId, modifierName, productId, migrationId);
            }

            // Note: The API client doesn't have individual modifier creation methods, so we'll implement mock creation
            var createdModifiers = new List<Dictionary<string, object>>();

            int modifierIndex = 0;
            foreach (var modifier in entities)
            {
                // Phase 3.3.4.2: Periodic cancellation check (every 5 modifiers)
                if (modifierIndex > 0 && modifierIndex % 5 == 0)
                {
                    await CheckCancellationAsync(migrationId);
                }
                modifierIndex++;

                try
                {
                    if (!modifier.TryGetValue("product_id", out var productId))
                    {
                        _logger.LogWarning("⚙️ [MOD-{ExecutionId}] Modifier missing product_id for migration {MigrationId}", 
                            executionId, migrationId);
                        continue;
                    }

                    // Create modifier using BigCommerce API
                    // Based on: https://developer.bigcommerce.com/docs/rest-catalog/product-modifiers#create-a-product-modifier
                    var createdModifier = await CreateModifierAsync(modifier, destinationStore, migrationId, executionId, cancellationToken);
                    if (createdModifier != null)
                    {
                        createdModifiers.Add(createdModifier);
                    }
                    
                    var modifierName = modifier.TryGetValue("display_name", out var name) ? name?.ToString() : "no-name";
                    _logger.LogDebug("⚙️ [MOD-{ExecutionId}] Successfully created modifier '{ModifierName}' for product {ProductId} in migration {MigrationId}", 
                        executionId, modifierName, productId, migrationId);
                }
                catch (Exception ex)
                {
                    // 🚫 CANCELLATION FIX: Distinguish between actual failures and cancellation-induced failures
                    bool isCancellationError = ex.Message.Contains("Migration cancelled", StringComparison.OrdinalIgnoreCase) ||
                                             ex.Message.Contains("User requested cancellation", StringComparison.OrdinalIgnoreCase) ||
                                             ex is OperationCanceledException;
                    
                    if (isCancellationError)
                    {
                        var modifierName = modifier.TryGetValue("display_name", out var name) ? name?.ToString() : "unknown";
                        var productId = modifier.TryGetValue("product_id", out var prodId) ? prodId?.ToString() : "unknown";
                        
                        _logger.LogInformation("🚫 [MOD-{ExecutionId}] Modifier '{ModifierName}' for product {ProductId} was cancelled in migration {MigrationId}: {ErrorMessage}",
                            executionId, modifierName, productId, migrationId, ex.Message);
                        
                        // Return a special object to indicate cancellation
                        createdModifiers.Add(new Dictionary<string, object>
                        {
                            ["status"] = "cancelled",
                            ["reason"] = "migration_cancelled",
                            ["original_product_id"] = productId,
                            ["display_name"] = modifierName
                        });
                    }
                    else
                    {
                        _logger.LogError(ex, "⚙️ [MOD-{ExecutionId}] Failed to create individual modifier for migration {MigrationId}", 
                            executionId, migrationId);
                        // Continue with next modifier instead of failing entire batch
                    }
                }
            }

            _logger.LogInformation("✅ [MOD-{ExecutionId}] Successfully created {CreatedCount}/{TotalCount} modifiers for migration {MigrationId}", 
                executionId, createdModifiers.Count, entities.Count, migrationId);

            return createdModifiers;
        }
        catch (Exception ex)
        {
            // Extract detailed API error information
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "⚙️ [MOD-{ExecutionId}] ❌ Failed to create {ModifierCount} modifiers in migration {MigrationId}. {DetailedError}",
                executionId, entities.Count, migrationId, detailedErrorMessage);

            // Return empty list to avoid causing Durable Functions replay issues
            _logger.LogWarning("⚙️ [MOD-{ExecutionId}] Returning empty result to prevent replay in migration {MigrationId}", 
                executionId, migrationId);
            
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Validates store configuration
    /// </summary>
    /// <param name="storeConfig">Store configuration to validate</param>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
    private static void ValidateStoreConfiguration(StoreConfiguration storeConfig)
    {
        if (storeConfig == null || !storeConfig.IsValid())
            throw new ArgumentException("Invalid destination store configuration");
    }

    /// <summary>
    /// Extracts detailed error information from API exceptions
    /// </summary>
    /// <param name="exception">The exception to analyze</param>
    /// <returns>Detailed error message if available, otherwise empty string</returns>
    private static string ExtractDetailedErrorMessage(Exception exception)
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
    /// Creates a single modifier using BigCommerce API
    /// Based on: https://developer.bigcommerce.com/docs/rest-catalog/product-modifiers#create-a-product-modifier
    /// </summary>
    private async Task<Dictionary<string, object>?> CreateModifierAsync(
        Dictionary<string, object> modifier,
        StoreConfiguration destinationStore,
        string migrationId,
        string executionId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!modifier.TryGetValue("product_id", out var productIdObj) || productIdObj == null)
            {
                _logger.LogWarning("⚙️ [MOD-{ExecutionId}] Cannot create modifier - missing product_id", executionId);
                return null;
            }

            // Use product_id directly (already mapped by transform strategy)
            var destinationProductId = productIdObj.ToString()!;
            _logger.LogDebug("⚙️ [MOD-{ExecutionId}] Using product_id {ProductId} (already mapped by transform strategy)", 
                executionId, destinationProductId);

            // Check if this is a checkbox modifier (requires two-step creation)
            var modifierType = modifier.TryGetValue("type", out var typeObj) ? typeObj?.ToString()?.ToLowerInvariant() : "";
            var isCheckboxModifier = string.Equals(modifierType, "checkbox", StringComparison.InvariantCultureIgnoreCase);
            
            if (isCheckboxModifier)
            {
                _logger.LogDebug("⚙️ [MOD-{ExecutionId}] Detected checkbox modifier - using two-step creation process", executionId);
            }

            // Prepare modifier payload for BigCommerce API (exclude option_values for checkbox during initial creation)
            var modifierPayload = PrepareModifierPayload(modifier, destinationProductId, excludeOptionValues: isCheckboxModifier);
            
            // Build the API request URL - modifiers are created under specific products
            var url = $"{destinationStore.GetApiBaseUrl()}/catalog/products/{destinationProductId}/modifiers";

            // Serialize the modifier data
            var jsonContent = JsonSerializer.Serialize(modifierPayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            _logger.LogInformation("⚙️ [MOD-{ExecutionId}] 🌐 API CALL: URL={Url}, ProductId={ProductId}", 
                executionId, url, destinationProductId);
            
            _logger.LogDebug("🔍 [MOD-{ExecutionId}] REQUEST DETAILS: Method=POST, ContentType=application/json, PayloadSize={PayloadSize} bytes", 
                executionId, jsonContent.Length);

            // Create and execute the API request
            var request = ApiRequest.CreatePost(url, jsonContent, destinationStore);
            
            var apiStartTime = DateTime.UtcNow;
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            var apiDuration = DateTime.UtcNow - apiStartTime;
            
            _logger.LogInformation("⚙️ [MOD-{ExecutionId}] ✅ API SUCCESS: ProductId={ProductId}, Duration={Duration}ms", 
                executionId, destinationProductId, apiDuration.TotalMilliseconds);
            
            // Handle the response data 
            Dictionary<string, object>? createdModifier = null;
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                createdModifier = JsonSerializer.Deserialize<Dictionary<string, object>>(dataElement.GetRawText());
            }
            else if (response != null && response.ContainsKey("id"))
            {
                createdModifier = response;
            }

            if (createdModifier != null)
            {
                var createdId = createdModifier.TryGetValue("id", out var id) ? id.ToString() : "unknown";
                _logger.LogDebug("✅ [MOD-{ExecutionId}] Successfully created modifier with ID {ModifierId} for product {ProductId}",
                    executionId, createdId, destinationProductId);

                // ✅ STEP 2: For checkbox modifiers, update with option_values in a separate API call
                if (isCheckboxModifier && modifier.ContainsKey("option_values"))
                {
                    _logger.LogDebug("⚙️ [MOD-{ExecutionId}] Step 2: Updating checkbox modifier {ModifierId} with option_values", 
                        executionId, createdId);
                    
                    var updateSuccess = await UpdateCheckboxModifierWithOptionValues(
                        modifier, createdId, destinationProductId, destinationStore, executionId, cancellationToken);
                    
                    if (!updateSuccess)
                    {
                        _logger.LogWarning("⚠️ [MOD-{ExecutionId}] Checkbox modifier {ModifierId} created but failed to update with option_values", 
                            executionId, createdId);
                    }
                }

                return createdModifier;
            }

            _logger.LogWarning("⚠️ [MOD-{ExecutionId}] Unexpected API response format for modifier creation on product {ProductId}",
                executionId, destinationProductId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚙️ [MOD-{ExecutionId}] Failed to create modifier", executionId);
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
    /// Prepares modifier payload according to BigCommerce API requirements
    /// </summary>
    private Dictionary<string, object> PrepareModifierPayload(Dictionary<string, object> sourceModifier, string destinationProductId, bool excludeOptionValues = false)
    {
        var payload = new Dictionary<string, object>();

        // ✅ Copy ALL fields from source modifier WITHOUT hardcoded defaults
        foreach (var kvp in sourceModifier)
        {
            // Skip system fields that shouldn't be migrated
            if (kvp.Key == "id" || kvp.Key == "product_id" || kvp.Key == "_source_product_id") continue;
            
            // ✅ CRITICAL FIX: Skip option_values for checkbox modifiers during initial creation
            if (kvp.Key == "option_values" && excludeOptionValues)
            {
                _logger.LogDebug("🔄 [MOD] Excluding option_values for checkbox modifier (will be added in update step)");
                continue;
            }
            
            // Handle option_values specially to ensure proper structure
            if (kvp.Key == "option_values" && kvp.Value is List<object> optionValuesList)
            {
                var optionValues = new List<Dictionary<string, object>>();
                
                foreach (var optionValueData in optionValuesList.Cast<Dictionary<string, object>>())
                {
                    var optionValuePayload = new Dictionary<string, object>();
                    
                    // Copy ALL option value fields except system fields that cause API errors  
                    foreach (var optionValueKvp in optionValueData)
                    {
                        // ✅ CRITICAL FIX: Exclude 'id' field - BigCommerce API rejects option_values with id during creation
                        if (optionValueKvp.Key == "id" || optionValueKvp.Key == "modifier_id") continue;
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
    /// Updates a checkbox modifier with option_values in a separate API call (required by BigCommerce)
    /// </summary>
    private async Task<bool> UpdateCheckboxModifierWithOptionValues(
        Dictionary<string, object> sourceModifier,
        string modifierId,
        string productId,
        StoreConfiguration destinationStore,
        string executionId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Prepare update payload with option_values
            var updatePayload = PrepareModifierPayload(sourceModifier, productId, excludeOptionValues: false);
            
            // Build the update URL
            var updateUrl = $"{destinationStore.GetApiBaseUrl()}/catalog/products/{productId}/modifiers/{modifierId}";
            
            // Serialize the update data
            var jsonContent = JsonSerializer.Serialize(updatePayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            _logger.LogDebug("⚙️ [MOD-{ExecutionId}] 🌐 UPDATE API CALL: URL={Url}, ModifierId={ModifierId}", 
                executionId, updateUrl, modifierId);

            // Create and execute the PUT request
            var request = ApiRequest.CreatePut(updateUrl, jsonContent, destinationStore);
            
            var apiStartTime = DateTime.UtcNow;
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            var apiDuration = DateTime.UtcNow - apiStartTime;

            _logger.LogInformation("⚙️ [MOD-{ExecutionId}] ✅ UPDATE SUCCESS: ModifierId={ModifierId}, Duration={Duration}ms", 
                executionId, modifierId, apiDuration.TotalMilliseconds);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚙️ [MOD-{ExecutionId}] Failed to update checkbox modifier {ModifierId} with option_values", 
                executionId, modifierId);
            return false;
        }
    }

    /// <summary>
    /// Phase 3.3.4.2: Helper method to check for blob-based cancellation
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
                _logger.LogInformation("🚫 [CANCELLATION] Modifiers creation cancelled: {Reason}", reason);
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