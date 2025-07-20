using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using OpenSearch.Net;
using System.Text.Json;

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
            // Extract migration ID from context if present
            string? migrationId = null;
            
            // Try to extract migration ID from AdditionalData
            if (additionalData != null)
            {
                // Use reflection to get migrationId from the anonymous object
                var migrationIdProperty = additionalData.GetType().GetProperty("migrationId");
                if (migrationIdProperty != null)
                {
                    migrationId = migrationIdProperty.GetValue(additionalData)?.ToString();
                }
            }
            
            // If not found in AdditionalData, try to extract from context
            if (string.IsNullOrEmpty(migrationId) && context.Contains("_"))
            {
                var parts = context.Split('_');
                if (parts.Length >= 2 && Guid.TryParse(parts[1], out _))
                {
                    migrationId = parts[1];
                }
            }

            var document = new
            {
                Context = context,
                MigrationId = migrationId, // Add MigrationId field for easier searching
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
            
            // Add detailed logging for debugging
            Console.WriteLine($"OpenSearch: Logging error to index '{indexName}'");
            Console.WriteLine($"OpenSearch: Context: {context}");
            Console.WriteLine($"OpenSearch: MigrationId: {migrationId}");
            Console.WriteLine($"OpenSearch: Exception Type: {exception.GetType().Name}");
            Console.WriteLine($"OpenSearch: Exception Message: {exception.Message}");
            Console.WriteLine($"OpenSearch: Category: Error");
            Console.WriteLine($"OpenSearch: Document structure: {JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true })}");
            
            var response = await _client.IndexAsync(document, i => i.Index(indexName), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to log error: {Error}", response.OriginalException?.Message);
                Console.WriteLine($"OpenSearch: Failed to log error - {response.OriginalException?.Message}");
                return false;
            }

            _logger.LogDebug("Error logged successfully: {Context} - {ExceptionType}", context, exception.GetType().Name);
            Console.WriteLine($"OpenSearch: Error logged successfully to index '{indexName}' with ID '{response.Id}'");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging error information: {Context}", context);
            Console.WriteLine($"OpenSearch: Exception while logging error: {ex.Message}");
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
            var indexPattern = $"{_configuration.DefaultIndex}-*";
            Console.WriteLine($"OpenSearch: Searching logs with query: '{searchQuery}'");
            Console.WriteLine($"OpenSearch: Index pattern: '{indexPattern}'");
            Console.WriteLine($"OpenSearch: Date range: {fromDate:yyyy-MM-dd HH:mm:ss} to {toDate:yyyy-MM-dd HH:mm:ss}");
            
            var response = await _client.SearchAsync<object>(s => s
                .Index(indexPattern)
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
                Console.WriteLine($"OpenSearch: Search failed - {response.OriginalException?.Message}");
                return new List<object>();
            }

            var results = response.Documents.ToList();
            Console.WriteLine($"OpenSearch: Search returned {results.Count} documents");
            Console.WriteLine($"OpenSearch: Total hits: {response.Total}");
            
            // Add debugging to show document structure
            if (results.Count > 0)
            {
                Console.WriteLine($"OpenSearch: First document structure:");
                var firstDoc = results.First();
                var docJson = JsonSerializer.Serialize(firstDoc, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine($"OpenSearch: {docJson}");
            }
            
            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching logs: {Query}", searchQuery);
            Console.WriteLine($"OpenSearch: Exception during search: {ex.Message}");
            return new List<object>();
        }
    }

    /// <summary>
    /// Optimized structured search with pagination and field selection
    /// </summary>
    public async Task<(IEnumerable<object> Results, long TotalCount)> SearchLogsOptimizedAsync(
        OpenSearchQuery queryRequest, 
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Optimize index pattern based on date range
            var indexPattern = BuildOptimizedIndexPattern(queryRequest.FromDate, queryRequest.ToDate);
            
            Console.WriteLine($"OpenSearch Optimized: Index pattern: '{indexPattern}'");
            Console.WriteLine($"OpenSearch Optimized: Query - Level: {queryRequest.Level}, MigrationId: {queryRequest.MigrationId}, SearchTerm: {queryRequest.SearchTerm}");
            
            var response = await _client.SearchAsync<object>(s => s
                .Index(indexPattern)
                .Query(q => BuildStructuredQueryDescriptor(q, queryRequest))
                .Source(src => queryRequest.IncludeFields != null ? src.Includes(fields => fields.Fields(queryRequest.IncludeFields)) : src)
                .Size(queryRequest.Size)
                .From(queryRequest.From)
                .Sort(sort => sort.Field(queryRequest.SortField, ConvertSortOrder(queryRequest.SortOrder)))
                .Highlight(h => !string.IsNullOrEmpty(queryRequest.SearchTerm) ? h
                    .Fields(f => f
                        .Field("message").FragmentSize(150).NumberOfFragments(1)
                        .Field("context").FragmentSize(150).NumberOfFragments(1)
                    ) : null)
            , cancellationToken);

            Console.WriteLine($"OpenSearch Optimized: Response.IsValid: {response.IsValid}");
            Console.WriteLine($"OpenSearch Optimized: Total hits: {response.Total}");
            Console.WriteLine($"OpenSearch Optimized: Document count: {response.Documents?.Count() ?? 0}");

            if (!response.IsValid)
            {
                Console.WriteLine($"OpenSearch Optimized: ServerError: {response.ServerError}");
                Console.WriteLine($"OpenSearch Optimized: OriginalException: {response.OriginalException?.Message}");
                Console.WriteLine($"OpenSearch Optimized: DebugInformation: {response.DebugInformation}");
                _logger.LogError("Failed to execute optimized search: ServerError={ServerError}, Exception={Exception}, Debug={Debug}", 
                    response.ServerError, response.OriginalException?.Message, response.DebugInformation);
                return (new List<object>(), 0);
            }

            return (response.Documents ?? new List<object>(), response.Total);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in optimized search");
            return (new List<object>(), 0);
        }
    }

    /// <summary>
    /// Builds optimized index pattern based on date range to limit search scope
    /// </summary>
    private string BuildOptimizedIndexPattern(DateTime fromDate, DateTime toDate)
    {
        // For compatibility with existing data, use wildcard pattern like legacy search
        // This ensures we find data regardless of the specific index naming patterns
        return $"{_configuration.DefaultIndex}-*";
    }

    /// <summary>
    /// Builds structured query using OpenSearch query DSL descriptor pattern
    /// </summary>
    private QueryContainer BuildStructuredQueryDescriptor(QueryContainerDescriptor<object> q, OpenSearchQuery queryRequest)
    {
        return q.Bool(b =>
        {
            var boolDescriptor = b;

            // Add date range filter (filters are cached and faster than queries)
            boolDescriptor = boolDescriptor.Filter(f => f
                .DateRange(dr => dr
                    .Field("timestamp")
                    .GreaterThanOrEquals(queryRequest.FromDate)
                    .LessThanOrEquals(queryRequest.ToDate)
                )
            );

            // Add level filter using query string (more flexible than term matching)
            if (!string.IsNullOrEmpty(queryRequest.Level))
            {
                if (queryRequest.Level.Equals("Error", StringComparison.OrdinalIgnoreCase))
                {
                    // Use query string like the legacy search for better compatibility
                    boolDescriptor = boolDescriptor.Must(m => m
                        .QueryString(qs => qs.Query("category:\"Error\""))
                    );
                }
                else
                {
                    // Use query string for other levels too
                    boolDescriptor = boolDescriptor.Must(m => m
                        .QueryString(qs => qs.Query($"(Level:{queryRequest.Level} OR level:{queryRequest.Level})"))
                    );
                }
            }

            // Add migration ID filter using query string (more flexible) - match legacy search exactly
            if (!string.IsNullOrEmpty(queryRequest.MigrationId))
            {
                // Use exact same pattern as legacy search with wildcard searches
                boolDescriptor = boolDescriptor.Must(m => m
                    .QueryString(qs => qs.Query($"(MigrationId:{queryRequest.MigrationId} OR migrationId:{queryRequest.MigrationId} OR Context:*{queryRequest.MigrationId}* OR context:*{queryRequest.MigrationId}*)"))
                );
            }

            // Add entity type filter using query string
            if (!string.IsNullOrEmpty(queryRequest.EntityType))
            {
                boolDescriptor = boolDescriptor.Must(m => m
                    .QueryString(qs => qs.Query($"(entityType:{queryRequest.EntityType} OR entityType:{queryRequest.EntityType}s)"))
                );
            }

            // Add text search using query string for consistency
            if (!string.IsNullOrEmpty(queryRequest.SearchTerm))
            {
                boolDescriptor = boolDescriptor.Must(m => m
                    .QueryString(qs => qs.Query(queryRequest.SearchTerm))
                );
            }

            return boolDescriptor;
        });
    }

    /// <summary>
    /// Converts custom SortOrder enum to OpenSearch.Client.SortOrder
    /// </summary>
    private OpenSearch.Client.SortOrder ConvertSortOrder(Core.Models.SortOrder sortOrder)
    {
        return sortOrder == Core.Models.SortOrder.Ascending 
            ? OpenSearch.Client.SortOrder.Ascending 
            : OpenSearch.Client.SortOrder.Descending;
    }

    /// <summary>
    /// Batch search for multiple migration IDs with aggregations
    /// </summary>
    public async Task<Dictionary<string, object>> SearchLogsBatchAsync(
        IEnumerable<string> migrationIds, 
        DateTime fromDate, 
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var indexPattern = BuildOptimizedIndexPattern(fromDate, toDate);
            
            var response = await _client.SearchAsync<object>(s => s
                .Index(indexPattern)
                .Size(0) // We only want aggregations, not documents
                .Query(q => q
                    .Bool(b => b
                        .Filter(f => f
                            .DateRange(dr => dr
                                .Field("timestamp")
                                .GreaterThanOrEquals(fromDate)
                                .LessThanOrEquals(toDate)
                            ),
                            f => f.Terms(t => t
                                .Field("migrationId")
                                .Terms(migrationIds)
                            )
                        )
                    )
                )
                .Aggregations(a => a
                    .Terms("by_migration_id", t => t
                        .Field("migrationId")
                        .Size(migrationIds.Count())
                        .Aggregations(aa => aa
                            .Terms("by_level", tt => tt
                                .Field("level")
                            )
                            .Terms("by_entity_type", tt => tt
                                .Field("entityType")
                            )
                        )
                    )
                ), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to execute batch search: {Error}", response.OriginalException?.Message);
                return new Dictionary<string, object>();
            }

            return response.Aggregations.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch search");
            return new Dictionary<string, object>();
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