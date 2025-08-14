using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Models;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity function for completing a migration
/// </summary>
public class CompleteMigrationActivity
{
    private readonly IMigrationStorageService _storageService;
    private readonly IOpenSearchService _openSearchService;
    private readonly ILogger<CompleteMigrationActivity> _logger;

    public CompleteMigrationActivity(
        IMigrationStorageService storageService,
        IOpenSearchService openSearchService,
        ILogger<CompleteMigrationActivity> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Completes the migration process
    /// </summary>
    /// <param name="request">Migration completion request</param>
    [Function("CompleteMigration")]
    public async Task CompleteMigrationAsync([ActivityTrigger] CompleteMigrationRequest request)
    {
        try
        {
            _logger.LogInformation("Completing migration {MigrationId} with status {Status}", 
                request.MigrationId, request.Result.Status);

            // Update migration status in storage
            var migrationEntry = await _storageService.GetMigrationAsync(request.MigrationId);
            if (migrationEntry != null)
            {
                migrationEntry.Status = request.Result.Status;
                migrationEntry.UpdatedAt = DateTime.UtcNow;
                migrationEntry.ErrorMessage = request.Result.Errors.Any() ? string.Join("; ", request.Result.Errors) : null;
                
                await _storageService.UpdateMigrationAsync(migrationEntry);
            }

            // Log completion event to OpenSearch
            await _openSearchService.LogMigrationEventAsync(
                "migration_completed",
                request.MigrationId,
                new
                {
                    Status = request.Result.Status.ToString(),
                    Duration = request.Result.Duration,
                    EntityResults = request.Result.EntityResults,
                    Statistics = request.Result.Statistics,
                    ErrorCount = request.Result.Errors.Count
                });

            _logger.LogInformation("Migration {MigrationId} completed successfully with status {Status}", 
                request.MigrationId, request.Result.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete migration {MigrationId}", request.MigrationId);
            throw;
        }
    }
}

/// <summary>
/// Request model for migration completion
/// </summary>
public class CompleteMigrationRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public MigrationResult Result { get; set; } = new();
} 