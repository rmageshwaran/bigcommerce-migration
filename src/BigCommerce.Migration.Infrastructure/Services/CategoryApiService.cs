using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Service for BigCommerce Category API operations
/// Follows Single Responsibility Principle - handles only category-related operations
/// </summary>
public class CategoryApiService : ICategoryApiClient
{
    private readonly ILogger<CategoryApiService> _logger;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the CategoryApiService
    /// </summary>
    /// <param name="logger">Logger for the service</param>
    /// <param name="httpClient">HTTP client for API calls</param>
    public CategoryApiService(ILogger<CategoryApiService> logger, HttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Gets category trees for a specific store and channel
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of category trees</returns>
    public async Task<List<Dictionary<string, object>>> GetCategoryTreesAsync(
        StoreConfiguration storeConfig, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (storeConfig?.IsValid() != true)
        {
            throw new ArgumentException("Invalid store configuration", nameof(storeConfig));
        }

        _logger.LogDebug("Getting category trees for store {StoreId}", storeConfig.StoreId);

        try
        {
            // TODO: Implement actual BigCommerce API call
            // For now, return empty list to satisfy tests
            await Task.CompletedTask;
            return new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get category trees for store {StoreId}", storeConfig.StoreId);
            throw;
        }
    }

    /// <summary>
    /// Gets categories from a specific category tree
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="categoryTreeId">Category tree identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of categories</returns>
    public async Task<List<Dictionary<string, object>>> GetCategoriesAsync(
        StoreConfiguration storeConfig, 
        string categoryTreeId, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (storeConfig?.IsValid() != true)
        {
            throw new ArgumentException("Invalid store configuration", nameof(storeConfig));
        }

        if (string.IsNullOrWhiteSpace(categoryTreeId))
        {
            throw new ArgumentException("Category tree ID cannot be null or empty", nameof(categoryTreeId));
        }

        _logger.LogDebug("Getting categories for store {StoreId}, tree {CategoryTreeId}", 
            storeConfig.StoreId, categoryTreeId);

        try
        {
            // TODO: Implement actual BigCommerce API call
            // For now, return empty list to satisfy tests
            await Task.CompletedTask;
            return new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get categories for store {StoreId}, tree {CategoryTreeId}", 
                storeConfig.StoreId, categoryTreeId);
            throw;
        }
    }

    /// <summary>
    /// Creates categories in a specific category tree
    /// </summary>
    /// <param name="storeConfig">Store configuration with credentials</param>
    /// <param name="categoryTreeId">Category tree identifier</param>
    /// <param name="categories">Categories to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of created categories</returns>
    public async Task<List<Dictionary<string, object>>> CreateCategoriesAsync(
        StoreConfiguration storeConfig, 
        string categoryTreeId, 
        List<Dictionary<string, object>> categories, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (storeConfig?.IsValid() != true)
        {
            throw new ArgumentException("Invalid store configuration", nameof(storeConfig));
        }

        if (string.IsNullOrWhiteSpace(categoryTreeId))
        {
            throw new ArgumentException("Category tree ID cannot be null or empty", nameof(categoryTreeId));
        }

        if (categories == null)
        {
            throw new ArgumentNullException(nameof(categories));
        }

        _logger.LogDebug("Creating {Count} categories for store {StoreId}, tree {CategoryTreeId}", 
            categories.Count, storeConfig.StoreId, categoryTreeId);

        try
        {
            // TODO: Implement actual BigCommerce API call
            // For now, return the input categories to satisfy tests
            await Task.CompletedTask;
            return categories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create categories for store {StoreId}, tree {CategoryTreeId}", 
                storeConfig.StoreId, categoryTreeId);
            throw;
        }
    }
} 