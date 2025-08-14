using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Simple blob-based cancellation flag storage implementation
/// </summary>
public class CancellationStore : ICancellationStore
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<CancellationStore> _logger;
    
    private const string CancellationContainerName = "migration-cancellation";

    /// <summary>
    /// Initializes a new instance of the CancellationStore class
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <param name="logger">Logger instance</param>
    public CancellationStore(IConfiguration configuration, ILogger<CancellationStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
        
        // Use the same connection pattern as BlobService
        var connectionString = configuration.GetConnectionString("AzureWebJobsStorage") 
            ?? configuration["AzureWebJobsStorage"]
            ?? throw new ArgumentNullException("AzureWebJobsStorage connection string is required");
        
        _blobServiceClient = new BlobServiceClient(connectionString);
    }

    /// <summary>
    /// Sets a cancellation flag for the specified migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="reason">Cancellation reason</param>
    /// <returns>Task representing the async operation</returns>
    public async Task SetCancellationFlagAsync(string migrationId, string reason)
    {
        try
        {
            _logger.LogInformation("Setting cancellation flag for migration: {MigrationId}, Reason: {Reason}", 
                migrationId, reason);

            var containerClient = await GetContainerClientAsync();
            var blobName = GetBlobName(migrationId);
            var blobClient = containerClient.GetBlobClient(blobName);

            // Store the reason as blob content, timestamp as metadata
            var content = Encoding.UTF8.GetBytes(reason);
            using var stream = new MemoryStream(content);
            
            var metadata = new Dictionary<string, string>
            {
                ["CancelledAt"] = DateTime.UtcNow.ToString("O"),
                ["MigrationId"] = migrationId
            };

            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "text/plain"
                },
                Metadata = metadata
            });

            _logger.LogInformation("Cancellation flag set successfully for migration: {MigrationId}", migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cancellation flag for migration: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Checks if a cancellation flag exists for the specified migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>True if migration is cancelled, false otherwise</returns>
    public async Task<bool> CheckCancellationFlagAsync(string migrationId)
    {
        try
        {
            var containerClient = await GetContainerClientAsync();
            var blobName = GetBlobName(migrationId);
            var blobClient = containerClient.GetBlobClient(blobName);

            var exists = await blobClient.ExistsAsync();
            
            _logger.LogDebug("Cancellation flag check for migration {MigrationId}: {Exists}", 
                migrationId, exists.Value);
                
            return exists.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cancellation flag for migration: {MigrationId}", migrationId);
            // In case of storage issues, assume not cancelled to avoid false positives
            return false;
        }
    }

    /// <summary>
    /// Removes the cancellation flag for the specified migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Task representing the async operation</returns>
    public async Task RemoveCancellationFlagAsync(string migrationId)
    {
        try
        {
            _logger.LogInformation("Removing cancellation flag for migration: {MigrationId}", migrationId);

            var containerClient = await GetContainerClientAsync();
            var blobName = GetBlobName(migrationId);
            var blobClient = containerClient.GetBlobClient(blobName);

            await blobClient.DeleteIfExistsAsync();

            _logger.LogInformation("Cancellation flag removed successfully for migration: {MigrationId}", migrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cancellation flag for migration: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Gets the cancellation reason for the specified migration (if cancelled)
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Cancellation reason or null if not cancelled</returns>
    public async Task<string?> GetCancellationReasonAsync(string migrationId)
    {
        try
        {
            var containerClient = await GetContainerClientAsync();
            var blobName = GetBlobName(migrationId);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                return null;
            }

            var response = await blobClient.DownloadContentAsync();
            var reason = response.Value.Content.ToString();
            
            _logger.LogDebug("Retrieved cancellation reason for migration {MigrationId}: {Reason}", 
                migrationId, reason);
                
            return reason;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cancellation reason for migration: {MigrationId}", migrationId);
            return null;
        }
    }

    /// <summary>
    /// Gets the blob container client, creating the container if it doesn't exist
    /// </summary>
    /// <returns>Blob container client</returns>
    private async Task<BlobContainerClient> GetContainerClientAsync()
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(CancellationContainerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
        return containerClient;
    }

    /// <summary>
    /// Gets the blob name for a migration's cancellation flag
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>Blob name</returns>
    private static string GetBlobName(string migrationId) => $"{migrationId}.flag";
}