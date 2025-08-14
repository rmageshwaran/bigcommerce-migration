namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Simple blob-based cancellation flag storage for migration operations
/// </summary>
public interface ICancellationStore
{
    /// <summary>
    /// Sets a cancellation flag for the specified migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="reason">Cancellation reason</param>
    /// <returns>Task representing the async operation</returns>
    Task SetCancellationFlagAsync(string migrationId, string reason);
    
    /// <summary>
    /// Checks if a cancellation flag exists for the specified migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>True if migration is cancelled, false otherwise</returns>
    Task<bool> CheckCancellationFlagAsync(string migrationId);
    
    /// <summary>
    /// Removes the cancellation flag for the specified migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Task representing the async operation</returns>
    Task RemoveCancellationFlagAsync(string migrationId);
    
    /// <summary>
    /// Gets the cancellation reason for the specified migration (if cancelled)
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Cancellation reason or null if not cancelled</returns>
    Task<string?> GetCancellationReasonAsync(string migrationId);
}