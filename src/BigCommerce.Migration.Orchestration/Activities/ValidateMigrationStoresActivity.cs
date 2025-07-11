using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for validating migration stores
/// </summary>
public class ValidateMigrationStoresActivity
{
    private readonly IBigCommerceApiClient _apiClient;
    private readonly ILogger<ValidateMigrationStoresActivity> _logger;

    public ValidateMigrationStoresActivity(
        IBigCommerceApiClient apiClient,
        ILogger<ValidateMigrationStoresActivity> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates source and destination stores
    /// </summary>
    /// <param name="request">Validation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("ValidateMigrationStores")]
    public async Task<ValidateStoresResult> ValidateMigrationStoresAsync([ActivityTrigger] ValidateStoresRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Validating source and destination stores");

            // Validate source store
            cancellationToken.ThrowIfCancellationRequested();
            var sourceValid = await _apiClient.IsHealthyAsync(request.SourceStore, cancellationToken);
            if (!sourceValid)
            {
                return new ValidateStoresResult
                {
                    IsValid = false,
                    ErrorMessage = "Source store is not accessible or credentials are invalid"
                };
            }

            // Validate destination store
            cancellationToken.ThrowIfCancellationRequested();
            var destinationValid = await _apiClient.IsHealthyAsync(request.DestinationStore, cancellationToken);
            if (!destinationValid)
            {
                return new ValidateStoresResult
                {
                    IsValid = false,
                    ErrorMessage = "Destination store is not accessible or credentials are invalid"
                };
            }

            _logger.LogInformation("Store validation completed successfully");
            
            return new ValidateStoresResult
            {
                IsValid = true
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Store validation was cancelled");
            return new ValidateStoresResult
            {
                IsValid = false,
                ErrorMessage = "Store validation was cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate stores");
            return new ValidateStoresResult
            {
                IsValid = false,
                ErrorMessage = $"Store validation failed: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// Request model for validating stores
/// </summary>
public class ValidateStoresRequest
{
    public StoreConfiguration SourceStore { get; set; } = new();
    public StoreConfiguration DestinationStore { get; set; } = new();
}

/// <summary>
/// Result model for store validation
/// </summary>
public class ValidateStoresResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
} 