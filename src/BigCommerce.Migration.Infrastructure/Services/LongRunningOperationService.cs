using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BigCommerce.Migration.Infrastructure.Middleware;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Example service demonstrating how to use cancellation middleware for long-running operations
/// Shows integration patterns for Phase 2 activity-level cancellation
/// </summary>
public class LongRunningOperationService
{
    private readonly ILogger<LongRunningOperationService> _logger;
    private readonly CancellationMiddleware _cancellationMiddleware;

    /// <summary>
    /// Initializes a new instance of the LongRunningOperationService
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="cancellationMiddleware">Cancellation middleware</param>
    public LongRunningOperationService(
        ILogger<LongRunningOperationService> logger,
        CancellationMiddleware cancellationMiddleware)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cancellationMiddleware = cancellationMiddleware ?? throw new ArgumentNullException(nameof(cancellationMiddleware));
    }

    /// <summary>
    /// Example: Process large data export with automatic cancellation checking
    /// </summary>
    public async Task<string> ProcessLargeDataExportAsync(
        string migrationId,
        List<string> entityIds,
        CancellationToken cancellationToken = default)
    {
        return await _cancellationMiddleware.ExecuteWithCancellationAsync(
            async (ct) =>
            {
                _logger.LogInformation("Starting large data export for {EntityCount} entities", entityIds.Count);

                var exportPath = Path.GetTempFileName();
                var processedCount = 0;

                // Simulate processing large batches of data
                foreach (var batch in entityIds.Chunk(100))
                {
                    // The middleware will check for cancellation every 30 seconds by default
                    // Individual batch processing respects the cancellation token
                    ct.ThrowIfCancellationRequested();

                    await ProcessBatchAsync(batch, exportPath, ct);
                    processedCount += batch.Length;

                    _logger.LogDebug("Processed {ProcessedCount}/{TotalCount} entities", 
                        processedCount, entityIds.Count);

                    // Small delay to simulate real work
                    await Task.Delay(100, ct);
                }

                _logger.LogInformation("Large data export completed. File: {ExportPath}", exportPath);
                return exportPath;
            },
            migrationId,
            "LargeDataExport",
            TimeSpan.FromSeconds(15) // Check for cancellation every 15 seconds
        );
    }

    /// <summary>
    /// Example: Bulk data transformation with cancellation support
    /// </summary>
    public async Task<Dictionary<string, object>> TransformBulkDataAsync(
        string migrationId,
        List<Dictionary<string, object>> sourceData,
        CancellationToken cancellationToken = default)
    {
        return await _cancellationMiddleware.ExecuteWithCancellationAsync(
            async (ct) =>
            {
                var transformedData = new Dictionary<string, object>();
                var statistics = new Dictionary<string, int>
                {
                    ["processed"] = 0,
                    ["skipped"] = 0,
                    ["errors"] = 0
                };

                foreach (var item in sourceData)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        // Simulate complex transformation
                        var transformed = await TransformItemAsync(item, ct);
                        
                        if (transformed != null)
                        {
                            var key = transformed.GetValueOrDefault("id")?.ToString() ?? Guid.NewGuid().ToString();
                            transformedData[key] = transformed;
                            statistics["processed"]++;
                        }
                        else
                        {
                            statistics["skipped"]++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to transform item");
                        statistics["errors"]++;
                    }
                }

                _logger.LogInformation("Bulk transformation completed. Processed: {Processed}, Skipped: {Skipped}, Errors: {Errors}",
                    statistics["processed"], statistics["skipped"], statistics["errors"]);

                return new Dictionary<string, object>
                {
                    ["data"] = transformedData,
                    ["statistics"] = statistics
                };
            },
            migrationId,
            "BulkDataTransformation",
            TimeSpan.FromSeconds(20) // Check for cancellation every 20 seconds
        );
    }

    /// <summary>
    /// Example: File processing operation with cancellation support
    /// </summary>
    public async Task ProcessMigrationFilesAsync(
        string migrationId,
        string[] filePaths,
        CancellationToken cancellationToken = default)
    {
        await _cancellationMiddleware.ExecuteWithCancellationAsync(
            async (ct) =>
            {
                foreach (var filePath in filePaths)
                {
                    ct.ThrowIfCancellationRequested();

                    _logger.LogInformation("Processing file: {FilePath}", filePath);

                    // Simulate file processing
                    await ProcessFileAsync(filePath, ct);

                    _logger.LogDebug("Completed processing file: {FilePath}", filePath);
                }

                _logger.LogInformation("All migration files processed successfully");
            },
            migrationId,
            "MigrationFileProcessing",
            TimeSpan.FromSeconds(10) // More frequent checks for file operations
        );
    }

    /// <summary>
    /// Example: Using the extension method for simpler syntax
    /// </summary>
    public async Task<int> CountEntitiesWithCancellationAsync(
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        // Using extension method for cleaner syntax
        return await _cancellationMiddleware.WithCancellationAsync(
            async () =>
            {
                // Simulate long-running counting operation
                var count = 0;
                for (int i = 0; i < 1000; i++)
                {
                    await Task.Delay(10); // Simulate work
                    count++;
                }
                return count;
            },
            migrationId,
            "EntityCounting"
        );
    }

    #region Helper Methods

    private async Task ProcessBatchAsync(string[] batch, string exportPath, CancellationToken cancellationToken)
    {
        // Simulate batch processing
        await Task.Delay(50, cancellationToken);
        
        // In real implementation, this would write to the export file
        _logger.LogDebug("Processed batch of {BatchSize} items", batch.Length);
    }

    private async Task<Dictionary<string, object>?> TransformItemAsync(
        Dictionary<string, object> item,
        CancellationToken cancellationToken)
    {
        // Simulate transformation work
        await Task.Delay(5, cancellationToken);
        
        // Simple transformation example
        var transformed = new Dictionary<string, object>(item);
        transformed["transformed_at"] = DateTime.UtcNow;
        transformed["version"] = "2.0";
        
        return transformed;
    }

    private async Task ProcessFileAsync(string filePath, CancellationToken cancellationToken)
    {
        // Simulate file processing
        await Task.Delay(100, cancellationToken);
        
        _logger.LogDebug("File processed: {FilePath}", filePath);
    }

    #endregion
}

/// <summary>
/// Configuration extension methods for setting up cancellation middleware
/// </summary>
public static class CancellationMiddlewareServiceExtensions
{
    /// <summary>
    /// Adds cancellation middleware to the service collection
    /// </summary>
    public static IServiceCollection AddCancellationMiddleware(
        this IServiceCollection services,
        Action<CancellationMiddlewareOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddTransient<CancellationMiddleware>();
        services.AddTransient<LongRunningOperationService>();

        return services;
    }
} 