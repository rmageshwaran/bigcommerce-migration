using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service interface for Azure Blob Storage operations
/// Handles CSV reports, payload logging, and file storage
/// </summary>
public interface IBlobService
{
    #region CSV Report Operations

    /// <summary>
    /// Generates and uploads a migration report as CSV
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="reportData">Report data to generate CSV from</param>
    /// <param name="reportType">Type of report (e.g., "summary", "detailed", "errors")</param>
    /// <returns>Blob URL for the generated CSV report</returns>
    Task<string> CreateMigrationReportAsync(string migrationId, List<Dictionary<string, object>> reportData, string reportType);

    /// <summary>
    /// Generates and uploads an entity mapping report as CSV
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityMappings">Entity mappings to include in report</param>
    /// <returns>Blob URL for the generated CSV report</returns>
    Task<string> CreateEntityMappingReportAsync(string migrationId, List<EntityMapping> entityMappings);

    /// <summary>
    /// Generates and uploads an API call report as CSV
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="apiCalls">API calls to include in report</param>
    /// <returns>Blob URL for the generated CSV report</returns>
    Task<string> CreateApiCallReportAsync(string migrationId, List<ApiCallTracking> apiCalls);

    /// <summary>
    /// Gets a signed URL for downloading a report
    /// </summary>
    /// <param name="blobUrl">Blob URL of the report</param>
    /// <param name="expiryTime">URL expiry time</param>
    /// <returns>Signed download URL</returns>
    Task<string> GetReportDownloadUrlAsync(string blobUrl, TimeSpan? expiryTime = null);

    /// <summary>
    /// Lists all reports for a migration
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <returns>List of report metadata</returns>
    Task<List<ReportMetadata>> ListMigrationReportsAsync(string migrationId);

    /// <summary>
    /// Deletes a report file
    /// </summary>
    /// <param name="blobUrl">Blob URL of the report to delete</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeleteReportAsync(string blobUrl);

    #endregion

    #region Payload Logging Operations

    /// <summary>
    /// Stores a large request payload that exceeded HTTP function limits
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="requestId">Request ID</param>
    /// <param name="payload">Large payload content</param>
    /// <param name="contentType">Content type of the payload</param>
    /// <returns>Blob URL where payload is stored</returns>
    Task<string> StoreRequestPayloadAsync(string migrationId, string requestId, string payload, string contentType = "application/json");

    /// <summary>
    /// Stores a large response payload for audit purposes
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="requestId">Request ID</param>
    /// <param name="payload">Response payload content</param>
    /// <param name="contentType">Content type of the payload</param>
    /// <returns>Blob URL where payload is stored</returns>
    Task<string> StoreResponsePayloadAsync(string migrationId, string requestId, string payload, string contentType = "application/json");

    /// <summary>
    /// Retrieves a stored payload
    /// </summary>
    /// <param name="blobUrl">Blob URL of the stored payload</param>
    /// <returns>Payload content</returns>
    Task<string> GetStoredPayloadAsync(string blobUrl);

    /// <summary>
    /// Compresses and stores a large payload
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="requestId">Request ID</param>
    /// <param name="payload">Payload content to compress</param>
    /// <param name="compressionType">Compression type (e.g., "gzip", "deflate")</param>
    /// <returns>Blob URL where compressed payload is stored</returns>
    Task<string> StoreCompressedPayloadAsync(string migrationId, string requestId, string payload, string compressionType = "gzip");

    /// <summary>
    /// Retrieves and decompresses a stored payload
    /// </summary>
    /// <param name="blobUrl">Blob URL of the compressed payload</param>
    /// <param name="compressionType">Compression type used</param>
    /// <returns>Decompressed payload content</returns>
    Task<string> GetCompressedPayloadAsync(string blobUrl, string compressionType = "gzip");

    #endregion

    #region File Management Operations

    /// <summary>
    /// Uploads a file to blob storage
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <param name="fileName">File name</param>
    /// <param name="content">File content</param>
    /// <param name="contentType">Content type</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>Blob URL of the uploaded file</returns>
    Task<string> UploadFileAsync(string containerName, string fileName, Stream content, string contentType, Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Downloads a file from blob storage
    /// </summary>
    /// <param name="blobUrl">Blob URL of the file</param>
    /// <returns>File content as stream</returns>
    Task<Stream> DownloadFileAsync(string blobUrl);

    /// <summary>
    /// Gets file metadata
    /// </summary>
    /// <param name="blobUrl">Blob URL of the file</param>
    /// <returns>File metadata</returns>
    Task<BlobMetadata> GetFileMetadataAsync(string blobUrl);

    /// <summary>
    /// Checks if a file exists
    /// </summary>
    /// <param name="blobUrl">Blob URL to check</param>
    /// <returns>True if file exists</returns>
    Task<bool> FileExistsAsync(string blobUrl);

    /// <summary>
    /// Deletes a file from blob storage
    /// </summary>
    /// <param name="blobUrl">Blob URL of the file to delete</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeleteFileAsync(string blobUrl);

    /// <summary>
    /// Lists files in a container with optional prefix filter
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <param name="prefix">Optional prefix filter</param>
    /// <returns>List of blob metadata</returns>
    Task<List<BlobMetadata>> ListFilesAsync(string containerName, string? prefix = null);

    #endregion

    #region Container Management Operations

    /// <summary>
    /// Creates a container if it doesn't exist
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <param name="isPublic">Whether the container should be public</param>
    /// <returns>True if created or already exists</returns>
    Task<bool> CreateContainerIfNotExistsAsync(string containerName, bool isPublic = false);

    /// <summary>
    /// Deletes a container and all its contents
    /// </summary>
    /// <param name="containerName">Container name to delete</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeleteContainerAsync(string containerName);

    /// <summary>
    /// Lists all containers
    /// </summary>
    /// <returns>List of container names</returns>
    Task<List<string>> ListContainersAsync();

    /// <summary>
    /// Gets container properties and metadata
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <returns>Container metadata</returns>
    Task<ContainerMetadata> GetContainerMetadataAsync(string containerName);

    #endregion

    #region Cleanup and Retention Operations

    /// <summary>
    /// Cleans up files older than the specified retention period
    /// </summary>
    /// <param name="containerName">Container to clean up</param>
    /// <param name="retentionPeriod">Retention period</param>
    /// <param name="prefix">Optional prefix filter</param>
    /// <returns>Number of files deleted</returns>
    Task<int> CleanupOldFilesAsync(string containerName, TimeSpan retentionPeriod, string? prefix = null);

    /// <summary>
    /// Archives old files to a different storage tier
    /// </summary>
    /// <param name="containerName">Container containing files to archive</param>
    /// <param name="archiveAfter">Archive files older than this period</param>
    /// <param name="prefix">Optional prefix filter</param>
    /// <returns>Number of files archived</returns>
    Task<int> ArchiveOldFilesAsync(string containerName, TimeSpan archiveAfter, string? prefix = null);

    /// <summary>
    /// Gets storage usage statistics for a container
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <returns>Storage usage statistics</returns>
    Task<StorageUsageStatistics> GetStorageUsageAsync(string containerName);

    #endregion
} 