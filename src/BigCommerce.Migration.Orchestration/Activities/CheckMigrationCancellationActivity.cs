using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for checking if a migration has been cancelled
/// </summary>
public class CheckMigrationCancellationActivity
{
    private readonly IMigrationStorageService _storageService;
    private readonly ILogger<CheckMigrationCancellationActivity> _logger;

    public CheckMigrationCancellationActivity(
        IMigrationStorageService storageService,
        ILogger<CheckMigrationCancellationActivity> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks if a migration has been cancelled
    /// </summary>
    /// <param name="migrationId">Migration ID to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("CheckMigrationCancellation")]
    public async Task<CheckCancellationResult> CheckMigrationCancellationAsync([ActivityTrigger] string migrationId, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogDebug("Checking cancellation status for migration {MigrationId}", migrationId);

            // Check if a cancellation token exists for this migration
            var cancellationTokenEntry = await _storageService.GetCancellationTokenAsync(migrationId);
            
            var isCancelled = cancellationTokenEntry != null && !cancellationTokenEntry.IsProcessed;
            
            if (isCancelled)
            {
                _logger.LogInformation("Migration {MigrationId} has been cancelled", migrationId);
                return new CheckCancellationResult
                {
                    IsCancelled = true,
                    CancellationReason = cancellationTokenEntry?.Reason ?? "Migration was cancelled"
                };
            }
            
            return new CheckCancellationResult
            {
                IsCancelled = false
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Cancellation check was cancelled for migration {MigrationId}", migrationId);
            return new CheckCancellationResult
            {
                IsCancelled = true,
                CancellationReason = "Cancellation check was cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check cancellation status for migration {MigrationId}", migrationId);
            return new CheckCancellationResult
            {
                IsCancelled = false
            }; // On error, assume not cancelled to allow migration to continue
        }
    }
}

/// <summary>
/// Result model for checking cancellation
/// </summary>
public class CheckCancellationResult
{
    public bool IsCancelled { get; set; }
    public string? CancellationReason { get; set; }
} 