using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for retrieving cancellation tokens for migration status determination.
/// Used by orchestrators to check if a migration was cancelled before determining final status.
/// </summary>
public class GetCancellationTokenActivity
{
    private readonly IMigrationStorageService _storageService;
    private readonly ILogger<GetCancellationTokenActivity> _logger;

    public GetCancellationTokenActivity(
        IMigrationStorageService storageService,
        ILogger<GetCancellationTokenActivity> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves the cancellation token for a migration if it exists
    /// </summary>
    /// <param name="migrationId">Migration ID to check</param>
    /// <param name="cancellationToken">Cancellation token for this operation</param>
    /// <returns>The cancellation token entry if found, null otherwise</returns>
    [Function("GetCancellationTokenActivity")]
    public async Task<CancellationTokenEntry?> GetCancellationTokenAsync(
        [ActivityTrigger] string migrationId, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogDebug("🔍 [GET-CANCELLATION-TOKEN] Checking for cancellation token for migration {MigrationId}", migrationId);

            // Get cancellation token from storage
            var cancellationTokenEntry = await _storageService.GetCancellationTokenAsync(migrationId);
            
            if (cancellationTokenEntry != null)
            {
                _logger.LogDebug("✅ [GET-CANCELLATION-TOKEN] Found cancellation token for migration {MigrationId}: {Reason}", 
                    migrationId, cancellationTokenEntry.Reason);
                return cancellationTokenEntry;
            }
            else
            {
                _logger.LogDebug("🚫 [GET-CANCELLATION-TOKEN] No cancellation token found for migration {MigrationId}", migrationId);
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("⚠️ [GET-CANCELLATION-TOKEN] Operation cancelled while checking cancellation token for migration {MigrationId}", migrationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [GET-CANCELLATION-TOKEN] Error retrieving cancellation token for migration {MigrationId}", migrationId);
            throw;
        }
    }
}