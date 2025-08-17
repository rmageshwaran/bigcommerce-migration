using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigCommerce.Migration.Activities.Services.EntityCreation;

/// <summary>
/// Strategy implementation for creating product reviews in destination stores
/// Based on: https://developer.bigcommerce.com/docs/rest-catalog/products/reviews#create-a-product-review
/// Implements Open/Closed Principle: specific to reviews without modifying base service
/// Phase 3.3.4.4: Enhanced with blob-based cooperative cancellation support
/// </summary>
public class ReviewsCreationStrategy : IEntityCreationStrategy
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<ReviewsCreationStrategy> _logger;
    private readonly IApiRequestHandler _apiRequestHandler;
    private readonly IMigrationStorageService _migrationStorageService;

    public ReviewsCreationStrategy(
        IBigCommerceApiClient apiClient,
        ICancellationStore cancellationStore,
        ILogger<ReviewsCreationStrategy> logger,
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
    public string EntityType => "reviews";

    /// <summary>
    /// Creates product reviews in the destination store
    /// </summary>
    /// <param name="entities">Reviews to create</param>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="destinationStore">Destination store configuration</param>
    /// <param name="categoryTreeContext">Category tree context (not used for reviews)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created reviews with destination IDs</returns>
    public async Task<List<Dictionary<string, object>>?> CreateEntitiesAsync(
        List<Dictionary<string, object>> entities,
        string migrationId,
        StoreConfiguration destinationStore,
        CategoryTreeContext? categoryTreeContext = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null || !entities.Any())
        {
            _logger.LogWarning("No reviews provided for creation in migration {MigrationId}", migrationId);
            return new List<Dictionary<string, object>>();
        }

        var executionId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("⭐ [REV-{ExecutionId}] Creating {Count} reviews for migration {MigrationId}", 
            executionId, entities.Count, migrationId);

        // Phase 3.3.4.4: Check for cancellation at start of reviews creation
        await CheckCancellationAsync(migrationId);

        try
        {
            // Validate store configuration
            ValidateStoreConfiguration(destinationStore);

            // Log review details for debugging
            foreach (var review in entities)
            {
                var reviewTitle = review.TryGetValue("title", out var title) ? title?.ToString() : "unknown";
                var productId = review.TryGetValue("product_id", out var prodId) ? prodId?.ToString() : "unknown";
                var rating = review.TryGetValue("rating", out var rat) ? rat?.ToString() : "unknown";
                
                _logger.LogDebug("⭐ [REV-{ExecutionId}] Creating review: Title='{ReviewTitle}', ProductId='{ProductId}', Rating='{Rating}' in migration {MigrationId}",
                    executionId, reviewTitle, productId, rating, migrationId);
            }

            var createdReviews = new List<Dictionary<string, object>>();

            int reviewIndex = 0;
            foreach (var review in entities)
            {
                // Phase 3.3.4.4: Periodic cancellation check (every 5 reviews)
                if (reviewIndex > 0 && reviewIndex % 5 == 0)
                {
                    await CheckCancellationAsync(migrationId);
                }
                reviewIndex++;

                try
                {
                    if (!review.TryGetValue("product_id", out var productId))
                    {
                        _logger.LogWarning("⭐ [REV-{ExecutionId}] Review missing product_id for migration {MigrationId}", 
                            executionId, migrationId);
                        continue;
                    }

                    // Create review using BigCommerce API
                    // Based on: https://developer.bigcommerce.com/docs/rest-catalog/products/reviews#create-a-product-review
                    var createdReview = await CreateReviewAsync(review, destinationStore, migrationId, executionId, cancellationToken);
                    if (createdReview != null)
                    {
                        createdReviews.Add(createdReview);
                    }
                    
                    var reviewTitle = review.TryGetValue("title", out var title) ? title?.ToString() : "no-title";
                    _logger.LogDebug("⭐ [REV-{ExecutionId}] Successfully created review '{ReviewTitle}' for product {ProductId} in migration {MigrationId}", 
                        executionId, reviewTitle, productId, migrationId);
                }
                catch (Exception ex)
                {
                    // 🚫 CANCELLATION FIX: Distinguish between actual failures and cancellation-induced failures
                    bool isCancellationError = ex.Message.Contains("Migration cancelled", StringComparison.OrdinalIgnoreCase) ||
                                             ex.Message.Contains("User requested cancellation", StringComparison.OrdinalIgnoreCase) ||
                                             ex is OperationCanceledException;
                    
                    if (isCancellationError)
                    {
                        var reviewTitle = review.TryGetValue("title", out var title) ? title?.ToString() : "unknown";
                        var productId = review.TryGetValue("product_id", out var prodId) ? prodId?.ToString() : "unknown";
                        
                        _logger.LogInformation("🚫 [REV-{ExecutionId}] Review '{ReviewTitle}' for product {ProductId} was cancelled in migration {MigrationId}: {ErrorMessage}",
                            executionId, reviewTitle, productId, migrationId, ex.Message);
                        
                        // Return a special object to indicate cancellation
                        createdReviews.Add(new Dictionary<string, object>
                        {
                            ["status"] = "cancelled",
                            ["reason"] = "migration_cancelled",
                            ["original_product_id"] = productId,
                            ["title"] = reviewTitle
                        });
                    }
                    else
                    {
                        _logger.LogError(ex, "⭐ [REV-{ExecutionId}] Failed to create individual review for migration {MigrationId}", 
                            executionId, migrationId);
                        // Continue with next review instead of failing entire batch
                    }
                }
            }

            _logger.LogInformation("✅ [REV-{ExecutionId}] Successfully created {CreatedCount}/{TotalCount} reviews for migration {MigrationId}", 
                executionId, createdReviews.Count, entities.Count, migrationId);

            return createdReviews;
        }
        catch (Exception ex)
        {
            // Extract detailed API error information
            var detailedErrorMessage = ExtractDetailedErrorMessage(ex);
            
            _logger.LogError(ex, "⭐ [REV-{ExecutionId}] ❌ Failed to create {ReviewCount} reviews in migration {MigrationId}. {DetailedError}",
                executionId, entities.Count, migrationId, detailedErrorMessage);

            // Return empty list to avoid causing Durable Functions replay issues
            _logger.LogWarning("⭐ [REV-{ExecutionId}] Returning empty result to prevent replay in migration {MigrationId}", 
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
    /// Creates a single review using BigCommerce API
    /// Based on: https://developer.bigcommerce.com/docs/rest-catalog/products/reviews#create-a-product-review
    /// </summary>
    private async Task<Dictionary<string, object>?> CreateReviewAsync(
        Dictionary<string, object> review,
        StoreConfiguration destinationStore,
        string migrationId,
        string executionId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!review.TryGetValue("product_id", out var productIdObj) || productIdObj == null)
            {
                _logger.LogWarning("⭐ [REV-{ExecutionId}] Cannot create review - missing product_id", executionId);
                return null;
            }

                        // Use product_id directly (already mapped by transform strategy)
            var destinationProductId = productIdObj.ToString()!;
            _logger.LogDebug("⭐ [REV-{ExecutionId}] Using product_id {ProductId} (already mapped by transform strategy)", 
                executionId, destinationProductId);

            // Prepare review payload for BigCommerce API
            var reviewPayload = PrepareReviewPayload(review, destinationProductId);
            
            // Build the API request URL for product reviews
            var url = $"{destinationStore.GetApiBaseUrl()}/catalog/products/{destinationProductId}/reviews";

            // Serialize the review data
            var jsonContent = JsonSerializer.Serialize(reviewPayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            _logger.LogDebug("⭐ [REV-{ExecutionId}] 🌐 API CALL: URL={Url}, ProductId={ProductId}", 
                executionId, url, destinationProductId);
            
            // Create and execute the API request
            var request = ApiRequest.CreatePost(url, jsonContent, destinationStore);
            
            var apiStartTime = DateTime.UtcNow;
            var response = await _apiRequestHandler.ExecuteRequestAsync<Dictionary<string, object>>(request, cancellationToken);
            var apiDuration = DateTime.UtcNow - apiStartTime;
            
            _logger.LogDebug("⭐ [REV-{ExecutionId}] ✅ API SUCCESS: ProductId={ProductId}, Duration={Duration}ms", 
                executionId, destinationProductId, apiDuration.TotalMilliseconds);
            
            // Handle the response data 
            Dictionary<string, object>? createdReview = null;
            if (response?.TryGetValue("data", out var dataValue) == true && dataValue is JsonElement dataElement)
            {
                createdReview = JsonSerializer.Deserialize<Dictionary<string, object>>(dataElement.GetRawText());
            }
            else if (response != null && response.ContainsKey("id"))
            {
                createdReview = response;
            }

            if (createdReview != null)
            {
                var createdId = createdReview.TryGetValue("id", out var id) ? id.ToString() : "unknown";
                _logger.LogDebug("✅ [REV-{ExecutionId}] Successfully created review with ID {ReviewId} for product {ProductId}",
                    executionId, createdId, destinationProductId);
                return createdReview;
            }

            _logger.LogWarning("⚠️ [REV-{ExecutionId}] Unexpected API response format for review creation on product {ProductId}",
                executionId, destinationProductId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⭐ [REV-{ExecutionId}] Failed to create review", executionId);
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
    /// Prepares review payload according to BigCommerce API requirements
    /// </summary>
    private Dictionary<string, object> PrepareReviewPayload(Dictionary<string, object> sourceReview, string destinationProductId)
    {
        var payload = new Dictionary<string, object>();

        // ✅ Copy ALL fields from source review WITHOUT hardcoded defaults
        foreach (var kvp in sourceReview)
        {
            // Skip system fields that shouldn't be migrated
            if (kvp.Key == "id" || kvp.Key == "product_id" || kvp.Key == "_source_product_id") continue;
            
            // Copy all other fields exactly as they are in source
            payload[kvp.Key] = kvp.Value;
        }

        // Set destination product ID (the only field we need to override)
        payload["product_id"] = destinationProductId;

        return payload;
    }

    /// <summary>
    /// Phase 3.3.4.4: Helper method to check for blob-based cancellation
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
                _logger.LogInformation("🚫 [CANCELLATION] Reviews creation cancelled: {Reason}", reason);
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