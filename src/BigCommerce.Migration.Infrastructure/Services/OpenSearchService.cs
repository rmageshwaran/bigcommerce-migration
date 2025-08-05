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
            _logger.LogInformation("Migration event logging was cancelled for {EventType} and entity {EntityId}", eventType, entityId);
            throw; // Infrastructure service - let caller handle cancellation appropriately
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
            _logger.LogInformation("Performance metrics logging was cancelled for operation {OperationName}", operationName);
            throw; // Infrastructure service - let caller handle cancellation appropriately
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
            string? entityType = null;
            
            // Try to extract migration ID and entityType from AdditionalData
            if (additionalData != null)
            {
                // Use reflection to get migrationId from the anonymous object
                var migrationIdProperty = additionalData.GetType().GetProperty("migrationId");
                if (migrationIdProperty != null)
                {
                    migrationId = migrationIdProperty.GetValue(additionalData)?.ToString();
                }
                
                // ✅ FIXED: Extract entityType from AdditionalData for top-level searching
                var entityTypeProperty = additionalData.GetType().GetProperty("entityType");
                if (entityTypeProperty != null)
                {
                    entityType = entityTypeProperty.GetValue(additionalData)?.ToString();
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
                EntityType = entityType, // ✅ FIXED: Add EntityType as top-level field for search queries
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
            
            _logger.LogDebug("Logging error to OpenSearch index '{IndexName}' for context '{Context}', migrationId '{MigrationId}'", 
                indexName, context, migrationId);
            
            var response = await _client.IndexAsync(document, i => i.Index(indexName), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("Failed to log error: {Error}", response.OriginalException?.Message);
                return false;
            }

            _logger.LogDebug("Error logged successfully: {Context} - {ExceptionType} to index '{IndexName}' with ID '{Id}'", 
                context, exception.GetType().Name, indexName, response.Id);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Error logging was cancelled for context '{Context}'", context);
            throw; // Infrastructure service - let caller handle cancellation appropriately
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
            var indexPattern = $"{_configuration.DefaultIndex}-*";
            _logger.LogDebug("OpenSearch: Searching logs with query: '{SearchQuery}'", searchQuery);
            _logger.LogDebug("OpenSearch: Index pattern: '{IndexPattern}'", indexPattern);
            _logger.LogDebug("OpenSearch: Date range: {FromDate:yyyy-MM-dd HH:mm:ss} to {ToDate:yyyy-MM-dd HH:mm:ss}", fromDate, toDate);
            
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
                _logger.LogDebug("OpenSearch: Search failed - {Error}", response.OriginalException?.Message);
                return new List<object>();
            }

            var results = response.Documents.ToList();
            _logger.LogDebug("OpenSearch search returned {Count} documents, Total hits: {Total}", results.Count, response.Total);
            
            return results;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Log search was cancelled for query '{SearchQuery}'", searchQuery);
            throw; // Infrastructure service - let caller handle cancellation appropriately
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching logs: {Query}", searchQuery);
            _logger.LogDebug("OpenSearch: Exception during search: {Error}", ex.Message);
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
            // Use specific error index pattern for error queries, broader pattern for others
            var indexPattern = !string.IsNullOrEmpty(queryRequest.Level) && queryRequest.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)
                ? $"{_configuration.DefaultIndex}-errors-*"  // ✅ FIX: Match actual error index pattern (remove extra "logs")
                : $"{_configuration.DefaultIndex}-*";        // Broader index for other queries
            
            // Build the actual query using descriptor pattern
            var queryDescriptor = BuildStructuredQueryDescriptor(new QueryContainerDescriptor<object>(), queryRequest);
            
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

            if (!response.IsValid)
            {
                _logger.LogError("Failed to execute optimized search: ServerError={ServerError}, Exception={Exception}, Debug={Debug}", 
                    response.ServerError, response.OriginalException?.Message, response.DebugInformation);
                return (new List<object>(), 0);
            }

            return (response.Documents ?? new List<object>(), response.Total);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Optimized search was cancelled for query request {QueryRequest}", queryRequest);
            throw; // Infrastructure service - let caller handle cancellation appropriately
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in optimized search");
            return (new List<object>(), 0);
        }
    }

    /// <summary>
    /// Builds optimized index pattern based on date range and query type to limit search scope
    /// </summary>
    private string BuildOptimizedIndexPattern(DateTime fromDate, DateTime toDate, OpenSearchQuery? queryRequest = null)
    {
        // For error searches, use the specific error index pattern
        if (queryRequest != null && !string.IsNullOrEmpty(queryRequest.Level) && 
            queryRequest.Level.Equals("Error", StringComparison.OrdinalIgnoreCase))
        {
            return $"{_configuration.DefaultIndex}-errors-*"; // ✅ FIX: Match actual error index pattern (remove extra "logs")
        }
        
        // For compatibility with existing data, use wildcard pattern like legacy search
        // This ensures we find data regardless of the specific index naming patterns
        return $"{_configuration.DefaultIndex}-*";
    }

    /// <summary>
    /// Builds structured query using OpenSearch query DSL descriptor pattern
    /// </summary>
    private QueryContainer BuildStructuredQueryDescriptor(QueryContainerDescriptor<object> q, OpenSearchQuery queryRequest)
    {
        if (!string.IsNullOrEmpty(queryRequest.MigrationId))
        {
            // Build the filter queries list using Bool.Filter (matches working OpenSearch query)
            var filterQueries = new List<Func<QueryContainerDescriptor<object>, QueryContainer>>();
            
            // Migration ID filter - use migrationId.keyword
            filterQueries.Add(f => f.Term(t => t.Field("migrationId.keyword").Value(queryRequest.MigrationId)));
            
            // Category filter - use category.keyword = "Error"
            if (!string.IsNullOrEmpty(queryRequest.Level) && queryRequest.Level.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                filterQueries.Add(f => f.Term(t => t.Field("category.keyword").Value("Error")));
            }
            
            // EntityType filter - use additionalData.entityType.keyword
            if (!string.IsNullOrEmpty(queryRequest.EntityType))
            {
                filterQueries.Add(f => f.Term(t => t.Field("additionalData.entityType.keyword").Value(queryRequest.EntityType)));
            }
            
            // Use Bool.Filter (like the working query) instead of Bool.Must
            return q.Bool(b => b.Filter(filterQueries.ToArray()));
        }

        // Fallback if no migration ID
        return q.MatchAll();
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
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Batch search was cancelled for migration IDs {MigrationIds}", string.Join(", ", migrationIds));
            throw; // Infrastructure service - let caller handle cancellation appropriately
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
            _logger.LogInformation("OpenSearch health check was cancelled");
            throw; // Infrastructure service - let caller handle cancellation appropriately
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
            _logger.LogInformation("Batch processing event logging was cancelled for migration ID {MigrationId}, entity type {EntityType}, batch {BatchNumber}", 
                migrationId, entityType, batchNumber);
            throw; // Infrastructure service - let caller handle cancellation appropriately
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