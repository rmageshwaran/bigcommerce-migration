using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Azure;

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
    public async Task<bool> CheckMigrationCancellationAsync([ActivityTrigger] string migrationId, CancellationToken cancellationToken = default)
    {
        // CRITICAL FIX: Strip quotes if they exist in the migration ID
        var cleanMigrationId = migrationId?.Trim('"') ?? migrationId;
        if (cleanMigrationId != migrationId)
        {
            _logger.LogWarning("🔧 QUOTE FIX: Stripped quotes from migration ID. Original: {Original}, Clean: {Clean}", 
                migrationId, cleanMigrationId);
        }
        
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("🔍 CANCELLATION CHECK: Starting check for migration {MigrationId}", cleanMigrationId);
            _logger.LogInformation("🔍 DEBUG: Using storage service: {ServiceType}", _storageService.GetType().FullName);

            // Check if a cancellation token exists for this migration
            CancellationTokenEntry? cancellationTokenEntry = null;
            try 
            {
                _logger.LogInformation("🔍 DEBUG: About to call GetCancellationTokenAsync for migration {MigrationId}", cleanMigrationId);
                _logger.LogInformation("🔍 DEBUG: Storage service type: {StorageServiceType}, Assembly: {Assembly}", 
                    _storageService.GetType().FullName, _storageService.GetType().Assembly.FullName);
                
                cancellationTokenEntry = await _storageService.GetCancellationTokenAsync(cleanMigrationId);
                _logger.LogInformation("🔍 DEBUG: GetCancellationTokenAsync returned for migration {MigrationId}", cleanMigrationId);
                
                _logger.LogInformation("🔍 CANCELLATION CHECK: Token found={TokenExists}, IsProcessed={IsProcessed} for migration {MigrationId}", 
                    cancellationTokenEntry != null, cancellationTokenEntry?.IsProcessed, cleanMigrationId);
                
                if (cancellationTokenEntry != null)
                {
                    _logger.LogInformation("🔍 TOKEN DETAILS: MigrationId={MigrationId}, Reason={Reason}, RequestedAt={RequestedAt}, Status={Status}", 
                        cancellationTokenEntry.MigrationId, cancellationTokenEntry.Reason, cancellationTokenEntry.RequestedAt, cancellationTokenEntry.Status);
                }
                else 
                {
                    _logger.LogWarning("🔍 TOKEN NOT FOUND: No cancellation token entry found for migration {MigrationId}", cleanMigrationId);
                }
            }
            catch (Azure.RequestFailedException azureEx)
            {
                _logger.LogError(azureEx, "🔍 AZURE ERROR: Azure RequestFailedException while calling GetCancellationTokenAsync for migration {MigrationId}. Status: {Status}, ErrorCode: {ErrorCode}", 
                    cleanMigrationId, azureEx.Status, azureEx.ErrorCode);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔍 ERROR: Exception while calling GetCancellationTokenAsync for migration {MigrationId}. Type: {ExceptionType}", 
                    cleanMigrationId, ex.GetType().FullName);
                throw;
            }
            
            var isCancelled = cancellationTokenEntry != null && !cancellationTokenEntry.IsProcessed;
            
            if (isCancelled)
            {
                _logger.LogWarning("🚨 MIGRATION CANCELLED: Migration {MigrationId} has been cancelled! Reason: {Reason}", 
                    cleanMigrationId, cancellationTokenEntry?.Reason);
                return true;
            }
            
            _logger.LogInformation("✅ CANCELLATION CHECK: Migration {MigrationId} not cancelled, continuing...", cleanMigrationId);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Cancellation check was cancelled for migration {MigrationId}", cleanMigrationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check cancellation status for migration {MigrationId}", cleanMigrationId);
            return false; // On error, assume not cancelled to allow migration to continue
        }
    }
} 