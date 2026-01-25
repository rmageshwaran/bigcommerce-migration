using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity function for setting cancellation flags using native Durable Functions approach
/// </summary>
public class SetCancellationFlagActivity
{
    private readonly ICancellationStore _cancellationStore;
    private readonly ILogger<SetCancellationFlagActivity> _logger;

    public SetCancellationFlagActivity(
        ICancellationStore cancellationStore,
        ILogger<SetCancellationFlagActivity> logger)
    {
        _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sets a cancellation flag for the specified migration
    /// </summary>
    /// <param name="request">Cancellation request with migration ID and reason</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the operation</returns>
    [Function("SetCancellationFlag")]
    public async Task SetCancellationFlagAsync(
        [ActivityTrigger] SetCancellationFlagRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("🚫 [SET-CANCEL-FLAG] Setting cancellation flag. MigrationId: {MigrationId}, Reason: {Reason}", 
                request.MigrationId, request.Reason);

            await _cancellationStore.SetCancellationFlagAsync(request.MigrationId, request.Reason);

            _logger.LogInformation("🚫 [SET-CANCEL-FLAG] Cancellation flag set successfully. MigrationId: {MigrationId}", 
                request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚫 [SET-CANCEL-FLAG] Error setting cancellation flag. MigrationId: {MigrationId}", 
                request.MigrationId);
            throw;
        }
    }
}

/// <summary>
/// Request model for setting a cancellation flag
/// </summary>
public class SetCancellationFlagRequest
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Reason for cancellation
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}