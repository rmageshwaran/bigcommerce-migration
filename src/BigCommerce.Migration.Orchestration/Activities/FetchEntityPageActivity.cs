using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System.Diagnostics;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Azure Durable Functions activity for fetching paginated entity data from BigCommerce API
/// Handles both V2 and V3 API versions with proper pagination support
/// </summary>
public class FetchEntityPageActivity
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<FetchEntityPageActivity> _logger;
    private readonly BigCommercePaginationConfig _paginationConfig;

    /// <summary>
    /// Initializes the FetchEntityPage activity
    /// </summary>
    public FetchEntityPageActivity(
        IBigCommerceApiClient apiClient, 
        ILogger<FetchEntityPageActivity> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paginationConfig = new BigCommercePaginationConfig(); // Default configuration
    }

    /// <summary>
    /// Fetches a specific page of entities from BigCommerce API
    /// </summary>
    /// <param name="request">Entity page request parameters</param>
    /// <returns>Entity page result with data and pagination metadata</returns>
    [Function("FetchEntityPage")]
    public async Task<EntityPageResult> FetchEntityPageAsync([ActivityTrigger] EntityPageRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new EntityPageResult
        {
            EntityType = request.EntityType,
            PageNumber = request.PaginationRequest.Page,
            ResponseTimestamp = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation(
                "Fetching page {PageNumber} of {EntityType} for migration {MigrationId}",
                request.PaginationRequest.Page,
                request.EntityType,
                request.MigrationId);

            // Validate the request
            var validationErrors = ValidateRequest(request);
            if (validationErrors.Any())
            {
                result.Errors.AddRange(validationErrors);
                return result;
            }

            // Apply pagination strategy for this entity type
            ApplyPaginationStrategy(request);

            // Fetch the paginated data
            var paginatedResponse = await FetchEntityPageWithRetryAsync(request);
            
            if (paginatedResponse != null)
            {
                result.Entities = paginatedResponse.Data;
                result.EntityIds = ExtractEntityIds(paginatedResponse.Data, request.EntityType);
                result.PaginationInfo = paginatedResponse;
                
                _logger.LogInformation(
                    "Successfully fetched page {PageNumber} of {EntityType} - {Count} entities retrieved",
                    request.PaginationRequest.Page,
                    request.EntityType,
                    result.Entities.Count);
            }
            else
            {
                result.Errors.Add("Failed to fetch entity page - null response received");
            }
        }
        catch (BigCommerceApiException ex)
        {
            _logger.LogError(ex,
                "BigCommerce API error fetching page {PageNumber} of {EntityType}: {ErrorMessage}",
                request.PaginationRequest.Page,
                request.EntityType,
                ex.Message);
            
            result.Errors.Add($"BigCommerce API error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error fetching page {PageNumber} of {EntityType}: {ErrorMessage}",
                request.PaginationRequest.Page,
                request.EntityType,
                ex.Message);
            
            result.Errors.Add($"Unexpected error: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            result.ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;
            
            _logger.LogInformation(
                "Completed fetching page {PageNumber} of {EntityType} in {ElapsedMs}ms - Success: {Success}",
                request.PaginationRequest.Page,
                request.EntityType,
                result.ResponseTimeMs,
                result.IsSuccessful);
        }

        return result;
    }

    /// <summary>
    /// Fetches entity page with retry logic
    /// </summary>
    private async Task<BigCommercePaginatedResponse<Dictionary<string, object>>?> FetchEntityPageWithRetryAsync(
        EntityPageRequest request)
    {
        var maxRetries = 0; // 🚨 DISABLED: No retries to avoid hitting rate limits unnecessarily
        var retryCount = 0;
        Exception? lastException = null;

        while (retryCount <= maxRetries)
        {
            try
            {
                // Get the paginated response based on entity type
                return await _apiClient.GetPaginatedEntitiesAsync(
                    request.SourceStore,
                    request.EntityType,
                    request.PaginationRequest,
                    CancellationToken.None);
            }
            catch (BigCommerceApiException ex) when (ShouldRetry(ex, retryCount, maxRetries))
            {
                lastException = ex;
                retryCount++;
                
                var delayMs = CalculateRetryDelay(retryCount);
                _logger.LogWarning(
                    "Retry {RetryCount}/{MaxRetries} for page {PageNumber} of {EntityType} after {DelayMs}ms delay. Error: {ErrorMessage}",
                    retryCount,
                    maxRetries,
                    request.PaginationRequest.Page,
                    request.EntityType,
                    delayMs,
                    ex.Message);
                
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Non-retryable error fetching page {PageNumber} of {EntityType}",
                    request.PaginationRequest.Page,
                    request.EntityType);
                throw;
            }
        }

        // All retries exhausted
        if (lastException != null)
        {
            throw lastException;
        }

        return null;
    }

    /// <summary>
    /// Determines if an API exception should trigger a retry
    /// </summary>
    private static bool ShouldRetry(BigCommerceApiException ex, int retryCount, int maxRetries)
    {
        if (retryCount >= maxRetries)
            return false;

        // Retry on rate limiting, server errors, and temporary network issues
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("rate limit") ||
               message.Contains("too many requests") ||
               message.Contains("server error") ||
               message.Contains("timeout") ||
               message.Contains("connection") ||
               message.Contains("503") ||
               message.Contains("502") ||
               message.Contains("500");
    }

    /// <summary>
    /// Calculates retry delay with exponential backoff
    /// </summary>
    private static int CalculateRetryDelay(int retryCount)
    {
        // Exponential backoff: 1s, 2s, 4s, 8s, etc.
        var baseDelayMs = 1000;
        return (int)(baseDelayMs * Math.Pow(2, retryCount - 1));
    }

    /// <summary>
    /// Applies entity-specific pagination strategy
    /// </summary>
    private void ApplyPaginationStrategy(EntityPageRequest request)
    {
        if (_paginationConfig.Strategies.TryGetValue(request.EntityType, out var strategy))
        {
            // Apply preferred page size if not explicitly set
            if (request.PaginationRequest.Limit <= 0)
            {
                request.PaginationRequest.Limit = strategy.PreferredPageSize;
            }
            
            // Enforce maximum page size
            if (request.PaginationRequest.Limit > strategy.MaxPageSize)
            {
                request.PaginationRequest.Limit = strategy.MaxPageSize;
                _logger.LogWarning(
                    "Page size reduced from {RequestedSize} to {MaxSize} for entity type {EntityType}",
                    request.PaginationRequest.Limit,
                    strategy.MaxPageSize,
                    request.EntityType);
            }
            
            // Apply safety limits for V2 API
            if (request.PaginationRequest.MaxPages == null && strategy.MaxPagesLimit.HasValue)
            {
                request.PaginationRequest.MaxPages = strategy.MaxPagesLimit.Value;
            }
        }
        else
        {
            _logger.LogWarning(
                "No pagination strategy found for entity type {EntityType}, using defaults",
                request.EntityType);
        }
    }

    /// <summary>
    /// Extracts entity IDs from the response data
    /// </summary>
    private static List<string> ExtractEntityIds(List<Dictionary<string, object>> entities, string entityType)
    {
        var entityIds = new List<string>();
        
        foreach (var entity in entities)
        {
            if (entity.TryGetValue("id", out var idValue))
            {
                entityIds.Add(idValue.ToString() ?? string.Empty);
            }
            else if (entity.TryGetValue("Id", out var idValueCapital))
            {
                entityIds.Add(idValueCapital.ToString() ?? string.Empty);
            }
            else
            {
                // For some entities, ID might be in a different field
                var possibleIdFields = new[] { "product_id", "category_id", "brand_id", "variant_id", "image_id", "modifier_id" };
                foreach (var field in possibleIdFields)
                {
                    if (entity.TryGetValue(field, out var altIdValue))
                    {
                        entityIds.Add(altIdValue.ToString() ?? string.Empty);
                        break;
                    }
                }
            }
        }
        
        return entityIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
    }

    /// <summary>
    /// Validates the entity page request
    /// </summary>
    private static List<string> ValidateRequest(EntityPageRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(request.MigrationId))
        {
            errors.Add("MigrationId is required");
        }

        if (string.IsNullOrEmpty(request.EntityType))
        {
            errors.Add("EntityType is required");
        }

        if (request.SourceStore == null)
        {
            errors.Add("SourceStore is required");
        }
        else
        {
            if (string.IsNullOrEmpty(request.SourceStore.StoreId))
            {
                errors.Add("SourceStore.StoreId is required");
            }

            if (string.IsNullOrEmpty(request.SourceStore.AccessToken))
            {
                errors.Add("SourceStore.AccessToken is required");
            }
        }

        if (request.PaginationRequest == null)
        {
            errors.Add("PaginationRequest is required");
        }
        else
        {
            if (request.PaginationRequest.Page < 1)
            {
                errors.Add("Page number must be 1 or greater");
            }

            if (request.PaginationRequest.Limit < 1)
            {
                errors.Add("Page limit must be 1 or greater");
            }

            if (request.PaginationRequest.Limit > 250)
            {
                errors.Add("Page limit cannot exceed 250");
            }
        }

        return errors;
    }
} 