using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using OpenSearch.Net;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// OpenSearch service implementation for logging, performance tracking, and search operations
/// Provides real OpenSearch integration with connection management and error handling
/// </summary>
public class OpenSearchService : IOpenSearchService
{
    private readonly OpenSearchConfiguration _configuration;
    private readonly ILogger<OpenSearchService> _logger;
    private readonly OpenSearchClient _client;

    /// <summary>
    /// Initializes a new instance of the OpenSearchService
    /// </summary>
    /// <param name="configuration">OpenSearch configuration settings</param>
    /// <param name="logger">Logger instance for service operations</param>
    public OpenSearchService(OpenSearchConfiguration configuration, ILogger<OpenSearchService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!_configuration.IsValidEndpoint())
        {
            throw new ArgumentException("Invalid OpenSearch endpoint configuration", nameof(configuration));
        }

        _client = CreateOpenSearchClient();
    }

    /// <summary>
    /// Logs a migration event with structured data
    /// </summary>
    public async Task<bool> LogMigrationEventAsync(string eventType, string entityId, object eventData, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            _logger.LogWarning("Event type is null or empty. Skipping log operation.");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var document = new
            {
                EventType = eventType,
                EntityId = entityId,
                EventData = eventData,
                Timestamp = DateTime.UtcNow,
                IndexName = _configuration.DefaultIndex
            };

            var indexName = $"{_configuration.DefaultIndex}-{DateTime.UtcNow:yyyy-MM}";
            var response = await _client.IndexAsync(document, i => i.Index(indexName), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to log migration event: {Error}", response.OriginalException?.Message);
                return false;
            }

            _logger.LogDebug("Migration event logged successfully: {EventType} for {EntityId}", eventType, entityId);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging migration event: {EventType} for {EntityId}", eventType, entityId);
            return false;
        }
    }

    /// <summary>
    /// Logs performance metrics for migration operations
    /// </summary>
    public async Task<bool> LogPerformanceMetricsAsync(string operationName, TimeSpan duration, object metrics, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            _logger.LogWarning("Operation name is null or empty. Skipping performance metrics log.");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var document = new
            {
                OperationName = operationName,
                Duration = duration,
                DurationMs = duration.TotalMilliseconds,
                Metrics = metrics,
                Timestamp = DateTime.UtcNow,
                Category = "Performance"
            };

            var indexName = $"{_configuration.DefaultIndex}-performance-{DateTime.UtcNow:yyyy-MM}";
            var response = await _client.IndexAsync(document, i => i.Index(indexName), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to log performance metrics: {Error}", response.OriginalException?.Message);
                return false;
            }

            _logger.LogDebug("Performance metrics logged successfully: {OperationName} took {Duration}ms", operationName, duration.TotalMilliseconds);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging performance metrics: {OperationName}", operationName);
            return false;
        }
    }

    /// <summary>
    /// Logs error information with exception details
    /// </summary>
    public async Task<bool> LogErrorAsync(string context, Exception exception, object? additionalData = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            _logger.LogWarning("Context is null or empty. Skipping error log.");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var document = new
            {
                Context = context,
                Exception = new
                {
                    Message = exception.Message,
                    Type = exception.GetType().Name,
                    StackTrace = exception.StackTrace,
                    Source = exception.Source
                },
                AdditionalData = additionalData,
                Timestamp = DateTime.UtcNow,
                Category = "Error"
            };

            var indexName = $"{_configuration.DefaultIndex}-errors-{DateTime.UtcNow:yyyy-MM}";
            var response = await _client.IndexAsync(document, i => i.Index(indexName), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to log error: {Error}", response.OriginalException?.Message);
                return false;
            }

            _logger.LogDebug("Error logged successfully: {Context} - {ExceptionType}", context, exception.GetType().Name);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging error information: {Context}", context);
            return false;
        }
    }

    /// <summary>
    /// Searches logs based on query criteria
    /// </summary>
    public async Task<IEnumerable<object>> SearchLogsAsync(string searchQuery, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return new List<object>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var response = await _client.SearchAsync<object>(s => s
                .Index($"{_configuration.DefaultIndex}-*")
                .Query(q => q
                    .Bool(b => b
                        .Must(m => m
                            .QueryString(qs => qs.Query(searchQuery))
                        )
                        .Filter(f => f
                            .DateRange(dr => dr
                                .Field("timestamp")
                                .GreaterThanOrEquals(fromDate)
                                .LessThanOrEquals(toDate)
                            )
                        )
                    )
                )
                .Size(1000)
            , cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to search logs: {Error}", response.OriginalException?.Message);
                return new List<object>();
            }

            return response.Documents.ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching logs: {Query}", searchQuery);
            return new List<object>();
        }
    }

    /// <summary>
    /// Checks if the OpenSearch service is healthy and available
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var response = await _client.Cluster.HealthAsync(ct: cancellationToken);
            return response.IsValid && response.Status != Health.Red;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking OpenSearch health");
            return false;
        }
    }

    /// <summary>
    /// Logs entity batch processing events
    /// </summary>
    public async Task<bool> LogEntityBatchProcessingAsync(
        string migrationId,
        string entityType,
        int batchNumber,
        object batchData,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            _logger.LogWarning("Migration ID is null or empty. Skipping batch processing log.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(entityType))
        {
            _logger.LogWarning("Entity type is null or empty. Skipping batch processing log.");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var document = new
            {
                MigrationId = migrationId,
                EntityType = entityType,
                BatchNumber = batchNumber,
                BatchData = batchData,
                Timestamp = DateTime.UtcNow,
                Category = "BatchProcessing"
            };

            var indexName = $"{_configuration.DefaultIndex}-batch-{DateTime.UtcNow:yyyy-MM}";
            var response = await _client.IndexAsync(document, i => i.Index(indexName), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to log batch processing event: {Error}", response.OriginalException?.Message);
                return false;
            }

            _logger.LogDebug("Batch processing event logged successfully: {MigrationId} - {EntityType} batch {BatchNumber}", 
                migrationId, entityType, batchNumber);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging batch processing event: {MigrationId} - {EntityType} batch {BatchNumber}", 
                migrationId, entityType, batchNumber);
            return false;
        }
    }

    /// <summary>
    /// Creates OpenSearch client with proper configuration
    /// </summary>
    private OpenSearchClient CreateOpenSearchClient()
    {
        var uri = new Uri(_configuration.Endpoint!);
        var settings = new ConnectionSettings(uri)
            .DefaultIndex(_configuration.DefaultIndex)
            .RequestTimeout(_configuration.RequestTimeout)
            .MaximumRetries(_configuration.MaxRetries);

        if (_configuration.HasCredentials())
        {
            settings.BasicAuthentication(_configuration.Username!, _configuration.Password!);
        }

        if (_configuration.EnableDebugMode)
        {
            settings.EnableDebugMode();
        }

        return new OpenSearchClient(settings);
    }
} 