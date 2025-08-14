using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity function for initializing a migration session
/// </summary>
public class InitializeMigrationActivity
{
    private readonly IMigrationStorageService _storageService;
    private readonly ILogger<InitializeMigrationActivity> _logger;

    public InitializeMigrationActivity(
        IMigrationStorageService storageService,
        ILogger<InitializeMigrationActivity> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes a migration session
    /// </summary>
    /// <param name="request">Initialization request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("InitializeMigration")]
    public async Task<InitializeMigrationResult> InitializeMigrationAsync([ActivityTrigger] InitializeMigrationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("Initializing migration session");

            // The migration entry should already exist from the HTTP function, so we don't need to create it again
            // Just return success to indicate initialization completed
            
            _logger.LogInformation("Migration session initialized successfully");

            return new InitializeMigrationResult
            {
                IsSuccess = true
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Migration initialization was cancelled");
            
            // Activity should return proper result, not throw
            return new InitializeMigrationResult
            {
                IsSuccess = false,
                ErrorMessage = "Migration initialization was cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize migration");
            
            return new InitializeMigrationResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// Request model for initializing migration
/// </summary>
public class InitializeMigrationRequest
{
    public MigrationRequest MigrationRequest { get; set; } = new();
    public CategoryTreeContext? CategoryTreeContext { get; set; }
}

/// <summary>
/// Result model for migration initialization  
/// </summary>
public class InitializeMigrationResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
} 