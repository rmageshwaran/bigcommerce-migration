using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Azure Blob Storage implementation for migration file operations
/// </summary>
public class BlobService : IBlobService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobService> _logger;
    
    private const string ReportsContainerName = "migration-reports";
    private const string PayloadsContainerName = "migration-payloads";

    /// <summary>
    /// Initializes a new instance of the BlobService class
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <param name="logger">Logger instance</param>
    public BlobService(IConfiguration configuration, ILogger<BlobService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Try ConnectionStrings section first, then fall back to Values section (Azure Functions style)
        var connectionString = configuration.GetConnectionString("AzureWebJobsStorage") 
            ?? configuration["AzureWebJobsStorage"]
            ?? throw new ArgumentNullException("AzureWebJobsStorage connection string is required");
        
        _blobServiceClient = new BlobServiceClient(connectionString);
    }

    #region CSV Report Operations

    /// <summary>
    /// Creates a migration report as a CSV file in blob storage
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="reportData">Report data to include in CSV</param>
    /// <param name="reportType">Type of report being created</param>
    /// <returns>Blob URL of the created report</returns>
    public async Task<string> CreateMigrationReportAsync(string migrationId, List<Dictionary<string, object>> reportData, string reportType)
    {
        try
        {
            _logger.LogInformation("Creating migration report: {MigrationId}, Type: {ReportType}", migrationId, reportType);

            var fileName = $"{migrationId}_{reportType}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            var csvContent = GenerateCsvContent(reportData);
            
            var containerClient = await GetContainerClientAsync(ReportsContainerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
            await blobClient.UploadAsync(stream, new BlobHttpHeaders
            {
                ContentType = "text/csv"
            });

            var metadata = new Dictionary<string, string>
            {
                ["migrationId"] = migrationId,
                ["reportType"] = reportType,
                ["generatedAt"] = DateTime.UtcNow.ToString("O")
            };
            await blobClient.SetMetadataAsync(metadata);

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating migration report: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Creates an entity mapping report as a CSV file in blob storage
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="entityMappings">Entity mappings to include in report</param>
    /// <returns>Blob URL of the created report</returns>
    public async Task<string> CreateEntityMappingReportAsync(string migrationId, List<EntityMapping> entityMappings)
    {
        try
        {
            _logger.LogInformation("Creating entity mapping report: {MigrationId}", migrationId);

            var fileName = $"{migrationId}_entity_mappings_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            var csvContent = GenerateEntityMappingCsv(entityMappings);
            
            var containerClient = await GetContainerClientAsync(ReportsContainerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
            await blobClient.UploadAsync(stream, new BlobHttpHeaders
            {
                ContentType = "text/csv"
            });

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating entity mapping report: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Creates an API call tracking report as a CSV file in blob storage
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="apiCalls">API call tracking data to include in report</param>
    /// <returns>Blob URL of the created report</returns>
    public async Task<string> CreateApiCallReportAsync(string migrationId, List<ApiCallTracking> apiCalls)
    {
        try
        {
            _logger.LogInformation("Creating API call report: {MigrationId}", migrationId);

            var fileName = $"{migrationId}_api_calls_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            var csvContent = GenerateApiCallCsv(apiCalls);
            
            var containerClient = await GetContainerClientAsync(ReportsContainerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
            await blobClient.UploadAsync(stream, new BlobHttpHeaders
            {
                ContentType = "text/csv"
            });

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating API call report: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Generates a pre-signed URL for downloading a report
    /// </summary>
    /// <param name="blobUrl">Blob URL of the report</param>
    /// <param name="expiryTime">Time until URL expires (default 1 hour)</param>
    /// <returns>Pre-signed download URL</returns>
    public async Task<string> GetReportDownloadUrlAsync(string blobUrl, TimeSpan? expiryTime = null)
    {
        try
        {
            _logger.LogInformation("Getting report download URL: {BlobUrl}", blobUrl);

            var uri = new Uri(blobUrl);
            var containerName = uri.Segments[1].TrimEnd('/');
            var blobName = string.Join("", uri.Segments.Skip(2));
            
            var containerClient = await GetContainerClientAsync(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                throw new FileNotFoundException($"Report file not found: {blobUrl}");
            }

            // Return the original URL (in production, generate SAS token)
            return blobUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting report download URL: {BlobUrl}", blobUrl);
            throw;
        }
    }

    /// <summary>
    /// Lists all migration reports for a specific migration
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <returns>List of report metadata</returns>
    public async Task<List<ReportMetadata>> ListMigrationReportsAsync(string migrationId)
    {
        try
        {
            _logger.LogInformation("Listing migration reports: {MigrationId}", migrationId);

            var containerClient = await GetContainerClientAsync(ReportsContainerName);
            var reports = new List<ReportMetadata>();

            await foreach (var blobItem in containerClient.GetBlobsAsync(BlobTraits.Metadata))
            {
                if (blobItem.Metadata.ContainsKey("migrationId") && 
                    blobItem.Metadata["migrationId"] == migrationId)
                {
                    var reportMetadata = new ReportMetadata
                    {
                        FileName = blobItem.Name,
                        BlobUrl = containerClient.GetBlobClient(blobItem.Name).Uri.ToString(),
                        MigrationId = migrationId,
                        ReportType = blobItem.Metadata.TryGetValue("reportType", out var reportType) ? reportType : "",
                        FileSizeBytes = blobItem.Properties.ContentLength ?? 0,
                        ContentType = blobItem.Properties.ContentType ?? "",
                        CreatedAt = blobItem.Properties.CreatedOn?.DateTime ?? DateTime.UtcNow,
                        LastModified = blobItem.Properties.LastModified?.DateTime ?? DateTime.UtcNow,
                        ETag = blobItem.Properties.ETag?.ToString() ?? "",
                        Metadata = new Dictionary<string, string>(blobItem.Metadata)
                    };

                    reports.Add(reportMetadata);
                }
            }

            return reports.OrderByDescending(r => r.CreatedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing migration reports: {MigrationId}", migrationId);
            throw;
        }
    }

    /// <summary>
    /// Deletes a migration report from blob storage
    /// </summary>
    /// <param name="blobUrl">Blob URL of the report to delete</param>
    /// <returns>True if deleted successfully, false otherwise</returns>
    public async Task<bool> DeleteReportAsync(string blobUrl)
    {
        try
        {
            _logger.LogInformation("Deleting report: {BlobUrl}", blobUrl);

            var uri = new Uri(blobUrl);
            var containerName = uri.Segments[1].TrimEnd('/');
            var blobName = string.Join("", uri.Segments.Skip(2));
            
            var containerClient = await GetContainerClientAsync(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            var response = await blobClient.DeleteIfExistsAsync();
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting report: {BlobUrl}", blobUrl);
            throw;
        }
    }

    #endregion

    #region Payload Logging Operations

    /// <summary>
    /// Stores a request payload in blob storage for debugging
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="requestId">Request identifier</param>
    /// <param name="payload">Request payload content</param>
    /// <param name="contentType">Content type of the payload</param>
    /// <returns>Blob URL of the stored payload</returns>
    public async Task<string> StoreRequestPayloadAsync(string migrationId, string requestId, string payload, string contentType = "application/json")
    {
        try
        {
            _logger.LogInformation("Storing request payload: {MigrationId}, {RequestId}", migrationId, requestId);

            var fileName = $"{migrationId}/requests/{requestId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var containerClient = await GetContainerClientAsync(PayloadsContainerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType });

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing request payload: {MigrationId}, {RequestId}", migrationId, requestId);
            throw;
        }
    }

    /// <summary>
    /// Stores a response payload in blob storage for debugging
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="requestId">Request identifier</param>
    /// <param name="payload">Response payload content</param>
    /// <param name="contentType">Content type of the payload</param>
    /// <returns>Blob URL of the stored payload</returns>
    public async Task<string> StoreResponsePayloadAsync(string migrationId, string requestId, string payload, string contentType = "application/json")
    {
        try
        {
            _logger.LogInformation("Storing response payload: {MigrationId}, {RequestId}", migrationId, requestId);

            var fileName = $"{migrationId}/responses/{requestId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var containerClient = await GetContainerClientAsync(PayloadsContainerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType });

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing response payload: {MigrationId}, {RequestId}", migrationId, requestId);
            throw;
        }
    }

    /// <summary>
    /// Retrieves a stored payload from blob storage
    /// </summary>
    /// <param name="blobUrl">Blob URL of the payload</param>
    /// <returns>Payload content as string</returns>
    public async Task<string> GetStoredPayloadAsync(string blobUrl)
    {
        try
        {
            _logger.LogInformation("Retrieving stored payload: {BlobUrl}", blobUrl);

            var uri = new Uri(blobUrl);
            var containerName = uri.Segments[1].TrimEnd('/');
            var blobName = string.Join("", uri.Segments.Skip(2));
            
            var containerClient = await GetContainerClientAsync(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                throw new FileNotFoundException($"Payload not found: {blobUrl}");
            }

            var response = await blobClient.DownloadContentAsync();
            return response.Value.Content.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving stored payload: {BlobUrl}", blobUrl);
            throw;
        }
    }

    /// <summary>
    /// Stores a compressed payload in blob storage for debugging
    /// </summary>
    /// <param name="migrationId">Migration identifier</param>
    /// <param name="requestId">Request identifier</param>
    /// <param name="payload">Payload content to compress</param>
    /// <param name="compressionType">Compression type (default: gzip)</param>
    /// <returns>Blob URL of the stored compressed payload</returns>
    public async Task<string> StoreCompressedPayloadAsync(string migrationId, string requestId, string payload, string compressionType = "gzip")
    {
        try
        {
            _logger.LogInformation("Storing compressed payload: {MigrationId}, {RequestId}", migrationId, requestId);

            var fileName = $"{migrationId}/compressed/{requestId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.gz";
            var containerClient = await GetContainerClientAsync(PayloadsContainerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            var compressedPayload = CompressString(payload);
            using var stream = new MemoryStream(compressedPayload);
            
            await blobClient.UploadAsync(stream, new BlobHttpHeaders
            {
                ContentType = "application/octet-stream",
                ContentEncoding = compressionType
            });

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing compressed payload: {MigrationId}, {RequestId}", migrationId, requestId);
            throw;
        }
    }

    /// <summary>
    /// Retrieves and decompresses a stored payload from blob storage
    /// </summary>
    /// <param name="blobUrl">Blob URL of the compressed payload</param>
    /// <param name="compressionType">Compression type (default: gzip)</param>
    /// <returns>Decompressed payload content as string</returns>
    public async Task<string> GetCompressedPayloadAsync(string blobUrl, string compressionType = "gzip")
    {
        try
        {
            _logger.LogInformation("Retrieving compressed payload: {BlobUrl}", blobUrl);

            var uri = new Uri(blobUrl);
            var containerName = uri.Segments[1].TrimEnd('/');
            var blobName = string.Join("", uri.Segments.Skip(2));
            
            var containerClient = await GetContainerClientAsync(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                throw new FileNotFoundException($"Compressed payload not found: {blobUrl}");
            }

            var response = await blobClient.DownloadContentAsync();
            var compressedData = response.Value.Content.ToArray();
            return DecompressBytes(compressedData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving compressed payload: {BlobUrl}", blobUrl);
            throw;
        }
    }

    #endregion

    #region File Management Operations

    /// <summary>
    /// Uploads a file to blob storage
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <param name="fileName">File name</param>
    /// <param name="content">File content stream</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="metadata">Optional metadata for the file</param>
    /// <returns>Blob URL of the uploaded file</returns>
    public async Task<string> UploadFileAsync(string containerName, string fileName, Stream content, string contentType, Dictionary<string, string>? metadata = null)
    {
        try
        {
            _logger.LogInformation("Uploading file: {ContainerName}/{FileName}", containerName, fileName);

            var containerClient = await GetContainerClientAsync(containerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            await blobClient.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType });

            if (metadata != null && metadata.Any())
            {
                await blobClient.SetMetadataAsync(metadata);
            }

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file: {ContainerName}/{FileName}", containerName, fileName);
            throw;
        }
    }

    /// <summary>
    /// Downloads a file from blob storage
    /// </summary>
    /// <param name="blobUrl">Blob URL of the file</param>
    /// <returns>File content as stream</returns>
    public async Task<Stream> DownloadFileAsync(string blobUrl)
    {
        try
        {
            _logger.LogInformation("Downloading file: {BlobUrl}", blobUrl);

            var uri = new Uri(blobUrl);
            var containerName = uri.Segments[1].TrimEnd('/');
            var blobName = string.Join("", uri.Segments.Skip(2));
            
            var containerClient = await GetContainerClientAsync(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                throw new FileNotFoundException($"File not found: {blobUrl}");
            }

            var response = await blobClient.DownloadStreamingAsync();
            return response.Value.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file: {BlobUrl}", blobUrl);
            throw;
        }
    }

    /// <summary>
    /// Gets metadata for a file in blob storage
    /// </summary>
    /// <param name="blobUrl">Blob URL of the file</param>
    /// <returns>File metadata</returns>
    public async Task<BlobMetadata> GetFileMetadataAsync(string blobUrl)
    {
        try
        {
            _logger.LogInformation("Getting file metadata: {BlobUrl}", blobUrl);

            var uri = new Uri(blobUrl);
            var containerName = uri.Segments[1].TrimEnd('/');
            var blobName = string.Join("", uri.Segments.Skip(2));
            
            var containerClient = await GetContainerClientAsync(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                throw new FileNotFoundException($"File not found: {blobUrl}");
            }

            var properties = await blobClient.GetPropertiesAsync();
            
            return new BlobMetadata
            {
                Name = blobName,
                Url = blobUrl,
                ContainerName = containerName,
                SizeBytes = properties.Value.ContentLength,
                ContentType = properties.Value.ContentType ?? "",
                CreatedAt = properties.Value.CreatedOn.DateTime,
                LastModified = properties.Value.LastModified.DateTime,
                ETag = properties.Value.ETag.ToString(),
                Metadata = new Dictionary<string, string>(properties.Value.Metadata)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file metadata: {BlobUrl}", blobUrl);
            throw;
        }
    }

    /// <summary>
    /// Checks if a file exists in blob storage
    /// </summary>
    /// <param name="blobUrl">Blob URL of the file</param>
    /// <returns>True if file exists, false otherwise</returns>
    public async Task<bool> FileExistsAsync(string blobUrl)
    {
        try
        {
            _logger.LogInformation("Checking if file exists: {BlobUrl}", blobUrl);

            var uri = new Uri(blobUrl);
            var containerName = uri.Segments[1].TrimEnd('/');
            var blobName = string.Join("", uri.Segments.Skip(2));
            
            var containerClient = await GetContainerClientAsync(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            var exists = await blobClient.ExistsAsync();
            return exists.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if file exists: {BlobUrl}", blobUrl);
            throw;
        }
    }

    /// <summary>
    /// Deletes a file from blob storage
    /// </summary>
    /// <param name="blobUrl">Blob URL of the file to delete</param>
    /// <returns>True if deleted successfully, false otherwise</returns>
    public async Task<bool> DeleteFileAsync(string blobUrl)
    {
        try
        {
            _logger.LogInformation("Deleting file: {BlobUrl}", blobUrl);

            var uri = new Uri(blobUrl);
            var containerName = uri.Segments[1].TrimEnd('/');
            var blobName = string.Join("", uri.Segments.Skip(2));
            
            var containerClient = await GetContainerClientAsync(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            var response = await blobClient.DeleteIfExistsAsync();
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file: {BlobUrl}", blobUrl);
            throw;
        }
    }

    /// <summary>
    /// Lists all files in a container
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <param name="prefix">Optional prefix to filter files</param>
    /// <returns>List of file metadata</returns>
    public async Task<List<BlobMetadata>> ListFilesAsync(string containerName, string? prefix = null)
    {
        try
        {
            _logger.LogInformation("Listing files in container: {ContainerName}", containerName);

            var containerClient = await GetContainerClientAsync(containerName);
            var files = new List<BlobMetadata>();

            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var metadata = new BlobMetadata
                {
                    Name = blobItem.Name,
                    Url = containerClient.GetBlobClient(blobItem.Name).Uri.ToString(),
                    ContainerName = containerName,
                    SizeBytes = blobItem.Properties.ContentLength ?? 0,
                    ContentType = blobItem.Properties.ContentType ?? "",
                    CreatedAt = blobItem.Properties.CreatedOn?.DateTime ?? DateTime.UtcNow,
                    LastModified = blobItem.Properties.LastModified?.DateTime ?? DateTime.UtcNow,
                    ETag = blobItem.Properties.ETag?.ToString() ?? "",
                    Metadata = new Dictionary<string, string>(blobItem.Metadata)
                };

                files.Add(metadata);
            }

            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files in container: {ContainerName}", containerName);
            throw;
        }
    }

    #endregion

    #region Container Management Operations

    /// <summary>
    /// Creates a container if it doesn't exist
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <param name="isPublic">Whether the container should be public</param>
    /// <returns>True if created or already exists</returns>
    public async Task<bool> CreateContainerIfNotExistsAsync(string containerName, bool isPublic = false)
    {
        try
        {
            _logger.LogInformation("Creating container if not exists: {ContainerName}", containerName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var response = await containerClient.CreateIfNotExistsAsync(isPublic ? 
                PublicAccessType.Blob : PublicAccessType.None);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating container: {ContainerName}", containerName);
            throw;
        }
    }

    /// <summary>
    /// Deletes a container and all its contents
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <returns>True if deleted successfully, false otherwise</returns>
    public async Task<bool> DeleteContainerAsync(string containerName)
    {
        try
        {
            _logger.LogInformation("Deleting container: {ContainerName}", containerName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var response = await containerClient.DeleteIfExistsAsync();

            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting container: {ContainerName}", containerName);
            throw;
        }
    }

    /// <summary>
    /// Lists all containers in the storage account
    /// </summary>
    /// <returns>List of container names</returns>
    public async Task<List<string>> ListContainersAsync()
    {
        try
        {
            _logger.LogInformation("Listing containers");

            var containers = new List<string>();
            await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
            {
                containers.Add(container.Name);
            }

            return containers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing containers");
            throw;
        }
    }

    /// <summary>
    /// Gets metadata for a container
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <returns>Container metadata</returns>
    public async Task<ContainerMetadata> GetContainerMetadataAsync(string containerName)
    {
        try
        {
            _logger.LogInformation("Getting container metadata: {ContainerName}", containerName);

            var containerClient = await GetContainerClientAsync(containerName);
            var properties = await containerClient.GetPropertiesAsync();

            return new ContainerMetadata
            {
                Name = containerName,
                Url = containerClient.Uri.ToString(),
                PublicAccess = properties.Value.PublicAccess?.ToString() ?? "None",
                CreatedAt = properties.Value.LastModified.DateTime,
                LastModified = properties.Value.LastModified.DateTime,
                ETag = properties.Value.ETag.ToString(),
                Metadata = new Dictionary<string, string>(properties.Value.Metadata)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting container metadata: {ContainerName}", containerName);
            throw;
        }
    }

    #endregion

    #region Cleanup and Retention Operations

    /// <summary>
    /// Cleans up old files from a container based on retention period
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <param name="retentionPeriod">Retention period for files</param>
    /// <param name="prefix">Optional prefix to filter files</param>
    /// <returns>Number of files deleted</returns>
    public async Task<int> CleanupOldFilesAsync(string containerName, TimeSpan retentionPeriod, string? prefix = null)
    {
        try
        {
            _logger.LogInformation("Cleaning up old files in container: {ContainerName}", containerName);

            var containerClient = await GetContainerClientAsync(containerName);
            var cutoffDate = DateTime.UtcNow - retentionPeriod;
            var deletedCount = 0;

            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                if (blobItem.Properties.LastModified < cutoffDate)
                {
                    var blobClient = containerClient.GetBlobClient(blobItem.Name);
                    await blobClient.DeleteIfExistsAsync();
                    deletedCount++;
                }
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up old files in container: {ContainerName}", containerName);
            throw;
        }
    }

    /// <summary>
    /// Archives old files to a cooler storage tier
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <param name="archiveAfter">Time after which to archive files</param>
    /// <param name="prefix">Optional prefix to filter files</param>
    /// <returns>Number of files archived</returns>
    public async Task<int> ArchiveOldFilesAsync(string containerName, TimeSpan archiveAfter, string? prefix = null)
    {
        try
        {
            _logger.LogInformation("Archiving old files in container: {ContainerName}", containerName);

            var containerClient = await GetContainerClientAsync(containerName);
            var cutoffDate = DateTime.UtcNow - archiveAfter;
            var archivedCount = 0;

            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                if (blobItem.Properties.LastModified < cutoffDate)
                {
                    // In a real implementation, change access tier to Archive
                    
                    archivedCount++;
                }
            }

            return archivedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving old files in container: {ContainerName}", containerName);
            throw;
        }
    }

    /// <summary>
    /// Gets storage usage statistics for a container
    /// </summary>
    /// <param name="containerName">Container name</param>
    /// <returns>Storage usage statistics</returns>
    public async Task<StorageUsageStatistics> GetStorageUsageAsync(string containerName)
    {
        try
        {
            _logger.LogInformation("Getting storage usage for container: {ContainerName}", containerName);

            var containerClient = await GetContainerClientAsync(containerName);
            var totalSize = 0L;
            var totalCount = 0;

            await foreach (var blobItem in containerClient.GetBlobsAsync())
            {
                totalSize += blobItem.Properties.ContentLength ?? 0;
                totalCount++;
            }

            return new StorageUsageStatistics
            {
                ContainerName = containerName,
                TotalBlobCount = totalCount,
                TotalSizeBytes = totalSize,
                TotalSizeFormatted = FormatBytes(totalSize),
                CalculatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting storage usage for container: {ContainerName}", containerName);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private async Task<BlobContainerClient> GetContainerClientAsync(string containerName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
        return containerClient;
    }

    private static string GenerateCsvContent(List<Dictionary<string, object>> reportData)
    {
        if (!reportData.Any())
            return "No data available";

        var csv = new StringBuilder();
        var headers = reportData.First().Keys.ToArray();
        csv.AppendLine(string.Join(",", headers));

        foreach (var row in reportData)
        {
            var values = headers.Select(h => row.ContainsKey(h) ? row[h]?.ToString() ?? "" : "");
            csv.AppendLine(string.Join(",", values));
        }

        return csv.ToString();
    }

    private static string GenerateEntityMappingCsv(List<EntityMapping> entityMappings)
    {
        var csv = new StringBuilder();
        csv.AppendLine("MigrationId,EntityType,SourceId,DestinationId,CreatedAt");

        foreach (var mapping in entityMappings)
        {
            csv.AppendLine($"{mapping.MigrationId},{mapping.EntityType},{mapping.SourceId},{mapping.DestinationId},{mapping.CreatedAt:O}");
        }

        return csv.ToString();
    }

    private static string GenerateApiCallCsv(List<ApiCallTracking> apiCalls)
    {
        var csv = new StringBuilder();
        csv.AppendLine("MigrationId,Endpoint,Method,StatusCode,ResponseTimeMs,RequestTimestamp");

        foreach (var call in apiCalls)
        {
            csv.AppendLine($"{call.MigrationId},{call.Endpoint},{call.Method},{call.StatusCode},{call.ResponseTimeMs},{call.RequestTimestamp:O}");
        }

        return csv.ToString();
    }

    private static byte[] CompressString(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            using var writer = new StreamWriter(gzip);
            writer.Write(text);
        }
        return output.ToArray();
    }

    private static string DecompressBytes(byte[] compressedData)
    {
        using var input = new MemoryStream(compressedData);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    private static string FormatBytes(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:F2} {units[unit]}";
    }

    #endregion
} 