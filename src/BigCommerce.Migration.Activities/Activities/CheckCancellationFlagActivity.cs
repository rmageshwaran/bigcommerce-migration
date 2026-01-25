using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity function for checking cancellation flags using native Durable Functions approach
/// </summary>
public class CheckCancellationFlagActivity
{
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<CheckCancellationFlagActivity> _logger;

    public CheckCancellationFlagActivity(
        ICancellationStore cancellationStore,
        ILogger<CheckCancellationFlagActivity> logger)
    {
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks if a cancellation flag exists for the specified migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple indicating if cancelled and the reason</returns>
    [Function("CheckCancellationFlag")]
    public async Task<(bool IsCancelled, string Reason)> CheckCancellationFlagAsync(
        [ActivityTrigger] string migrationId, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("🔍 [CHECK-CANCEL-FLAG] Checking cancellation flag. MigrationId: {MigrationId}", migrationId);

            var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
            
            // Only get reason if cancelled to minimize blob operations
            if (isCancelled)
            {
                var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "No reason provided";
                _logger.LogInformation("🚫 [CHECK-CANCEL-FLAG] Migration is cancelled. MigrationId: {MigrationId}, Reason: {Reason}", 
                    migrationId, reason);
                
                return (true, reason);
            }
            else
            {
                _logger.LogDebug("✅ [CHECK-CANCEL-FLAG] Migration is not cancelled. MigrationId: {MigrationId}", migrationId);
                return (false, string.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔍 [CHECK-CANCEL-FLAG] Error checking cancellation flag. MigrationId: {MigrationId}", migrationId);
            
            // Return not cancelled on error to avoid false positives
            return (false, $"Error checking cancellation: {ex.Message}");
        }
    }
}

