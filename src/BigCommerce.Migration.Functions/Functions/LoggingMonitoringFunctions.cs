using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Functions.Middleware;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace BigCommerce.Migration.Functions.Functions;

/// <summary>
/// Specialized monitoring endpoints for logging and performance analytics
/// </summary>
public class LoggingMonitoringFunctions
{
    private readonly ILogger<LoggingMonitoringFunctions> _logger;
    private readonly IOpenSearchService _openSearchService;
    private readonly IMigrationStorageService _storageService;
    private readonly IBlobService _blobService;

    public LoggingMonitoringFunctions(
        ILogger<LoggingMonitoringFunctions> logger,
        IOpenSearchService openSearchService,
        IMigrationStorageService storageService,
        IBlobService blobService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
    }

    /// <summary>
    /// Get recent log entries with filtering and pagination
    /// </summary>
            [Function("GetLogs")]
        public async Task<HttpResponseData> GetLogs(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "logs")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");

            try
            {
                // Parse query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var level = query["level"];
                var search = query["search"];
                var from = DateTime.TryParse(query["from"], out var fromDate)
                    ? fromDate : DateTime.UtcNow.AddDays(-1);
                var to = DateTime.TryParse(query["to"], out var toDate)
                    ? toDate : DateTime.UtcNow;
                var page = int.TryParse(query["page"], out var pageNum) && pageNum > 0 ? pageNum : 1;
                var pageSize = int.TryParse(query["size"], out var size) && size > 0 && size <= 500 ? size : 50;

                _logger.LogDebug("GetLogs called with parameters: level={Level}, search={Search}, from={From}, to={To}, page={Page}, pageSize={PageSize}",
                    level, search, from, to, page, pageSize);

                // Create optimized query request
                var queryRequest = new Core.Models.OpenSearchQuery
                {
                    FromDate = from,
                    ToDate = to,
                    Level = level,
                    SearchTerm = search,
                    Size = pageSize,
                    From = (page - 1) * pageSize,
                    SortField = "timestamp",
                    SortOrder = Core.Models.SortOrder.Descending,
                    IncludeFields = null, // Include all fields like legacy search
                    EnableHighlighting = !string.IsNullOrEmpty(search)
                };

                // Check if search term is a GUID (migration ID) for optimized filtering
                if (!string.IsNullOrEmpty(search) && Guid.TryParse(search, out _))
                {
                    queryRequest.MigrationId = search;
                    queryRequest.SearchTerm = null; // Use exact matching instead of text search
                }

                _logger.LogDebug("Executing optimized search with query: Level={Level}, MigrationId={MigrationId}, SearchTerm={SearchTerm}, from={From}, to={To}, page={Page}, pageSize={PageSize}", 
                    queryRequest.Level, queryRequest.MigrationId, queryRequest.SearchTerm, from, to, page, pageSize);
                
                // Execute optimized search
                var (results, totalCount) = await _openSearchService.SearchLogsOptimizedAsync(queryRequest);
                
                _logger.LogInformation("Optimized search returned {ResultCount} results, totalCount: {TotalCount}", results.Count(), totalCount);
                
                // Convert OpenSearch results to proper format - handle JsonElement results
                var entries = new List<object>();
                foreach (var result in results)
                {
                    if (result is JsonElement jsonElement)
                    {
                        entries.Add(ParseJsonElementToDictionary(jsonElement));
                    }
                    else if (result is Dictionary<string, object> dict)
                    {
                        entries.Add(dict);
                    }
                    else
                    {
                        _logger.LogWarning("Unexpected result type from OpenSearch: {Type}", result?.GetType()?.Name ?? "null");
                    }
                }
                
                _logger.LogInformation("Converted {EntryCount} entries from optimized search", entries.Count);

                // Calculate pagination
                var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

                var responseData = new
                {
                    entries = entries,
                    pagination = new
                    {
                        page = page,
                        pageSize = pageSize,
                        totalItems = totalCount,
                        totalPages = totalPages
                    }
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting logs");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = "An error occurred while retrieving logs"
                }));
                return response;
            }
        }

    /// <summary>
    /// Get log statistics and analytics
    /// </summary>
    [Function("GetLogStats")]
    [OpenApiOperation(operationId: "GetLogStats", tags: new[] { "Logging" },
        Summary = "Get log statistics",
        Description = "Returns log statistics including counts by level, time distribution, and error patterns.")]
    [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiParameter(name: "hours", In = ParameterLocation.Query, Required = false, Type = typeof(int),
        Description = "Time range in hours (default: 24)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Log statistics retrieved successfully")]
    public async Task<HttpResponseData> GetLogStatsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "logs/stats")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            // Check authentication
            var context = req.FunctionContext;
            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;

            if (!isAuthenticated)
            {
                response.StatusCode = HttpStatusCode.Unauthorized;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = "UNAUTHORIZED",
                        message = "Authentication required to view log statistics"
                    }
                }));
                return response;
            }

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var hoursParam = query["hours"];
            var hours = int.TryParse(hoursParam, out var h) ? Math.Max(1, Math.Min(168, h)) : 24; // Max 1 week

            var stats = await GetLogStatisticsAsync(hours);

            await response.WriteStringAsync(JsonSerializer.Serialize(stats, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving log statistics");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "LOG_STATS_ERROR",
                    message = "An error occurred while retrieving log statistics"
                }
            }));
            return response;
        }
    }

    /// <summary>
    /// Get performance analytics and trends
    /// </summary>
    [Function("GetPerformanceAnalytics")]
    [OpenApiOperation(operationId: "GetPerformanceAnalytics", tags: new[] { "Performance" },
        Summary = "Get performance analytics",
        Description = "Returns performance analytics including response times, throughput, and trend analysis.")]
    [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiParameter(name: "hours", In = ParameterLocation.Query, Required = false, Type = typeof(int),
        Description = "Time range in hours (default: 24)")]
    [OpenApiParameter(name: "granularity", In = ParameterLocation.Query, Required = false, Type = typeof(string),
        Description = "Data granularity (hour, day, default: hour)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Performance analytics retrieved successfully")]
    public async Task<HttpResponseData> GetPerformanceAnalyticsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/performance")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            // Check authentication
            var context = req.FunctionContext;
            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;

            if (!isAuthenticated)
            {
                response.StatusCode = HttpStatusCode.Unauthorized;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = "UNAUTHORIZED",
                        message = "Authentication required to view performance analytics"
                    }
                }));
                return response;
            }

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var hoursParam = query["hours"];
            var granularity = query["granularity"];
            var hours = int.TryParse(hoursParam, out var h) ? Math.Max(1, Math.Min(168, h)) : 24; // Max 1 week

            var analytics = await GetPerformanceAnalyticsDataAsync(hours, granularity);

            await response.WriteStringAsync(JsonSerializer.Serialize(analytics, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving performance analytics");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "PERFORMANCE_ANALYTICS_ERROR",
                    message = "An error occurred while retrieving performance analytics"
                }
            }));
            return response;
        }
    }

    /// <summary>
    /// Get API usage analytics
    /// </summary>
    [Function("GetApiUsageAnalytics")]
    [OpenApiOperation(operationId: "GetApiUsageAnalytics", tags: new[] { "Analytics" },
        Summary = "Get API usage analytics",
        Description = "Returns API usage analytics including endpoint usage, response times, and error rates.")]
    [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiParameter(name: "hours", In = ParameterLocation.Query, Required = false, Type = typeof(int),
        Description = "Time range in hours (default: 24)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "API usage analytics retrieved successfully")]
    public async Task<HttpResponseData> GetApiUsageAnalyticsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/api-usage")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            // Check authentication
            var context = req.FunctionContext;
            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;

            if (!isAuthenticated)
            {
                response.StatusCode = HttpStatusCode.Unauthorized;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = "UNAUTHORIZED",
                        message = "Authentication required to view API usage analytics"
                    }
                }));
                return response;
            }

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var hoursParam = query["hours"];
            var hours = int.TryParse(hoursParam, out var h) ? Math.Max(1, Math.Min(168, h)) : 24; // Max 1 week

            var analytics = await GetApiUsageAnalyticsDataAsync(hours);

            await response.WriteStringAsync(JsonSerializer.Serialize(analytics, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving API usage analytics");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "API_USAGE_ANALYTICS_ERROR",
                    message = "An error occurred while retrieving API usage analytics"
                }
            }));
            return response;
        }
    }

    /// <summary>
    /// Get error payload from blob storage
    /// </summary>
    [Function("GetErrorPayload")]
    [OpenApiOperation(operationId: "GetErrorPayload", tags: new[] { "Logging" },
        Summary = "Get error payload from blob storage",
        Description = "Retrieves the full error payload (request or response) from blob storage.")]
    [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiParameter(name: "migrationId", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Migration ID")]
    [OpenApiParameter(name: "requestId", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Request ID")]
    [OpenApiParameter(name: "payloadType", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Payload type (request or response)")]
    [OpenApiParameter(name: "entityName", In = ParameterLocation.Path, Required = true, Type = typeof(string),
        Description = "Entity name")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Error payload retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(object),
        Summary = "Authentication required")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object),
        Summary = "Payload not found")]
    public async Task<HttpResponseData> GetErrorPayloadAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "logs/payload/{migrationId}/{requestId}/{payloadType}/{entityName}")] HttpRequestData req,
        string migrationId, string requestId, string payloadType, string entityName)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            // Check authentication
            var context = req.FunctionContext;
            var isAuthenticated = context.Items.TryGetValue("IsAuthenticated", out var isAuthenticatedValue) && 
                                 isAuthenticatedValue is bool authenticated && authenticated;

            if (!isAuthenticated)
            {
                response.StatusCode = HttpStatusCode.Unauthorized;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = "UNAUTHORIZED",
                        message = "Authentication required to access error payloads"
                    }
                }));
                return response;
            }

            // Validate parameters
            if (string.IsNullOrWhiteSpace(migrationId) || string.IsNullOrWhiteSpace(requestId) ||
                string.IsNullOrWhiteSpace(payloadType) || string.IsNullOrWhiteSpace(entityName))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = "INVALID_PARAMETERS",
                        message = "Migration ID, Request ID, Payload Type, and Entity Name are required"
                    }
                }));
                return response;
            }

            // Validate payload type
            if (payloadType != "request" && payloadType != "response")
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = "INVALID_PAYLOAD_TYPE",
                        message = "Payload type must be 'request' or 'response'"
                    }
                }));
                return response;
            }

            // Implement blob storage retrieval
            try
            {
                // Construct the blob path based on the parameters
                var blobPath = $"migration-payloads/{migrationId}/{payloadType}s/{requestId}_*.json";
                
                _logger.LogInformation("Retrieving payload from blob storage: {BlobPath}", blobPath);
                
                // List files in the migration-payloads container for this migration and payload type
                var files = await _blobService.ListFilesAsync("migration-payloads", $"{migrationId}/{payloadType}s/");
                
                // Find the file that matches our requestId
                var targetFile = files.FirstOrDefault(f => f.Name.Contains(requestId));
                
                if (targetFile == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        error = new
                        {
                            code = "PAYLOAD_NOT_FOUND",
                            message = $"Payload not found for migration {migrationId}, request {requestId}, type {payloadType}"
                        }
                    }));
                    return response;
                }
                
                // Retrieve the payload content
                var payloadContent = await _blobService.GetStoredPayloadAsync(targetFile.Url);
                
                if (string.IsNullOrEmpty(payloadContent))
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        error = new
                        {
                            code = "PAYLOAD_EMPTY",
                            message = "Payload content is empty"
                        }
                    }));
                    return response;
                }
                
                // Return the payload content
                response.StatusCode = HttpStatusCode.OK;
                response.Headers.Add("Content-Type", "application/json");
                response.Headers.Add("Content-Disposition", $"attachment; filename=\"{targetFile.Name}\"");
                
                await response.WriteStringAsync(payloadContent);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving payload from blob storage for migration {MigrationId}, request {RequestId}, type {PayloadType}", 
                    migrationId, requestId, payloadType);
                
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = "BLOB_RETRIEVAL_ERROR",
                        message = "An error occurred while retrieving the payload from blob storage"
                    }
                }));
                return response;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving error payload for migration {MigrationId}, request {RequestId}, type {PayloadType}, entity {EntityName}", 
                migrationId, requestId, payloadType, entityName);
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "PAYLOAD_RETRIEVAL_ERROR",
                    message = "An error occurred while retrieving the error payload"
                }
            }));
            return response;
        }
    }

    // Helper methods for data retrieval



    /// <summary>
    /// Retrieves error logs from blob storage when OpenSearch is not available
    /// </summary>
    private async Task<IEnumerable<object>> GetErrorLogsFromBlobStorageAsync(string? search, DateTime from, DateTime to)
    {
        try
        {
            var errorLogs = new List<object>();
            
            // If search contains a migration ID, look for error payloads for that migration
            if (!string.IsNullOrEmpty(search) && Guid.TryParse(search, out var migrationId))
            {
                // List files in the error-logs container for this migration
                var errorFiles = await _blobService.ListFilesAsync("error-logs", $"{migrationId}/");
                
                foreach (var fileMetadata in errorFiles)
                {
                    try
                    {
                        // Get the blob URL from metadata
                        var blobUrl = fileMetadata.Url;
                        if (!string.IsNullOrEmpty(blobUrl))
                        {
                            var blobContent = await _blobService.GetStoredPayloadAsync(blobUrl);
                            if (!string.IsNullOrEmpty(blobContent))
                            {
                                var errorData = JsonSerializer.Deserialize<object>(blobContent);
                                if (errorData != null)
                                {
                                    errorLogs.Add(new
                                    {
                                        timestamp = DateTime.UtcNow,
                                        level = "Error",
                                        message = $"Error payload from blob: {fileMetadata.Name}",
                                        source = "BigCommerce.Migration.BlobStorage",
                                        migrationId = migrationId.ToString(),
                                        blobName = fileMetadata.Name,
                                        errorData = errorData
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read error blob: {BlobName}", fileMetadata.Name);
                    }
                }
            }
            
            return errorLogs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve error logs from blob storage");
            return Enumerable.Empty<object>();
        }
    }

    /// <summary>
    /// Builds OpenSearch query based on filters
    /// </summary>
    private string BuildLogSearchQuery(string? level, string? search)
    {
        var queryParts = new List<string>();
        
        // Add level filter - handle different field names and log types
        if (!string.IsNullOrWhiteSpace(level))
        {
            if (level.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                // For Error level, search for errors in OpenSearch service format
                // Based on the actual document structure: category field with value "Error" (lowercase 'c')
                queryParts.Add("category:\"Error\"");
            }
            else
            {
                // For other levels, search in standard log fields
                queryParts.Add($"(Level:{level} OR level:{level})");
            }
        }
        
        // Add search filter for migration ID or other text
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Search in multiple fields where migration ID might be stored
            // Now we have MigrationId field in error documents, so search there too
            queryParts.Add($"(MigrationId:{search} OR migrationId:{search} OR Context:*{search}* OR context:*{search}*)");
        }
        
        // Combine all parts with AND
        return string.Join(" AND ", queryParts);
    }

    /// <summary>
    /// Fallback method when OpenSearch is unavailable
    /// </summary>
    private Task<(List<object> Entries, int TotalCount)> GetFallbackLogEntriesAsync(
        string? level, DateTime from, DateTime to, string? search, int page, int pageSize)
    {
        _logger.LogWarning("Using fallback log data - OpenSearch service unavailable");
        
        var entries = new List<object>
        {
            new
            {
                timestamp = DateTime.UtcNow.AddMinutes(-30),
                level = "Warning",
                message = "OpenSearch service is currently unavailable. Showing fallback data.",
                source = "BigCommerce.Migration.Logs",
                requestId = Guid.NewGuid().ToString(),
                properties = new
                {
                    machineId = Environment.MachineName,
                    processId = Environment.ProcessId,
                    fallback = true
                }
            }
        };

        return Task.FromResult((entries, 1));
    }

    private Task<object> GetLogStatisticsAsync(int hours)
    {
        // In a real implementation, this would aggregate log data from OpenSearch
        var random = new Random();
        var from = DateTime.UtcNow.AddHours(-hours);

        var result = new
        {
            timestamp = DateTime.UtcNow,
            timeRange = new
            {
                from = from,
                to = DateTime.UtcNow,
                hours = hours
            },
            summary = new
            {
                totalLogs = random.Next(1000, 10000),
                errorRate = Math.Round(random.NextDouble() * 5, 2),
                averageLogsPerHour = random.Next(50, 500)
            },
            byLevel = new
            {
                debug = random.Next(100, 1000),
                information = random.Next(500, 5000),
                warning = random.Next(10, 100),
                error = random.Next(1, 50),
                critical = random.Next(0, 5)
            },
            timeline = Enumerable.Range(0, Math.Min(hours, 24)).Select(h => new
            {
                hour = from.AddHours(h),
                count = random.Next(10, 100),
                errors = random.Next(0, 10)
            }),
            topErrors = new[]
            {
                new { message = "Connection timeout", count = random.Next(1, 20) },
                new { message = "Rate limit exceeded", count = random.Next(1, 15) },
                new { message = "Invalid configuration", count = random.Next(1, 10) }
            }
        };

        return Task.FromResult((object)result);
    }

    /// <summary>
    /// Parses a JsonElement into a Dictionary<string, object>
    /// </summary>
    /// <param name="jsonElement">The JsonElement to parse</param>
    /// <returns>A Dictionary<string, object> representation of the JSON</returns>
    private Dictionary<string, object> ParseJsonElementToDictionary(JsonElement jsonElement)
    {
        var dictionary = new Dictionary<string, object>();
        foreach (var property in jsonElement.EnumerateObject())
        {
            var value = ParseJsonElement(property.Value);
            if (value != null)
            {
                dictionary[property.Name] = value;
            }
        }
        return dictionary;
    }

    /// <summary>
    /// Recursively parses JsonElement values into a Dictionary<string, object>
    /// </summary>
    /// <param name="jsonElement">The JsonElement to parse</param>
    /// <returns>A Dictionary<string, object> representation of the JSON</returns>
    private object? ParseJsonElement(JsonElement jsonElement)
    {
        switch (jsonElement.ValueKind)
        {
            case JsonValueKind.Object:
                return ParseJsonElementToDictionary(jsonElement);
            case JsonValueKind.Array:
                return jsonElement.EnumerateArray().Select(ParseJsonElement).ToList();
            case JsonValueKind.String:
                return jsonElement.GetString() ?? string.Empty;
            case JsonValueKind.Number:
                if (jsonElement.TryGetInt32(out int intValue)) return intValue;
                if (jsonElement.TryGetInt64(out long longValue)) return longValue;
                if (jsonElement.TryGetDouble(out double doubleValue)) return doubleValue;
                return jsonElement.GetDecimal();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            default:
                return jsonElement.ToString();
        }
    }

    private Task<object> GetPerformanceAnalyticsDataAsync(int hours, string? granularity)
    {
        // In a real implementation, this would analyze performance data
        var random = new Random();
        var from = DateTime.UtcNow.AddHours(-hours);

        var result = new
        {
            timestamp = DateTime.UtcNow,
            timeRange = new
            {
                from = from,
                to = DateTime.UtcNow,
                hours = hours,
                granularity = granularity ?? "hour"
            },
            summary = new
            {
                averageResponseTime = random.Next(100, 500),
                p95ResponseTime = random.Next(500, 1000),
                p99ResponseTime = random.Next(1000, 2000),
                throughput = random.Next(100, 1000),
                errorRate = Math.Round(random.NextDouble() * 2, 2)
            },
            timeline = Enumerable.Range(0, Math.Min(hours, 24)).Select(h => new
            {
                timestamp = from.AddHours(h),
                responseTime = random.Next(50, 300),
                throughput = random.Next(50, 200),
                errors = random.Next(0, 5)
            }),
            slowestEndpoints = new[]
            {
                new { endpoint = "/api/migrations", averageTime = random.Next(200, 800) },
                new { endpoint = "/api/dashboard/data", averageTime = random.Next(150, 600) },
                new { endpoint = "/api/health/detailed", averageTime = random.Next(100, 400) }
            }
        };

        return Task.FromResult((object)result);
    }

    private Task<object> GetApiUsageAnalyticsDataAsync(int hours)
    {
        // In a real implementation, this would analyze API usage patterns
        var random = new Random();
        var from = DateTime.UtcNow.AddHours(-hours);

        var result = new
        {
            timestamp = DateTime.UtcNow,
            timeRange = new
            {
                from = from,
                to = DateTime.UtcNow,
                hours = hours
            },
            summary = new
            {
                totalRequests = random.Next(1000, 10000),
                uniqueUsers = random.Next(10, 100),
                averageRequestsPerUser = random.Next(10, 200),
                mostActiveHour = random.Next(9, 17)
            },
            byEndpoint = new[]
            {
                new { endpoint = "/api/health", requests = random.Next(100, 1000), averageTime = random.Next(10, 50) },
                new { endpoint = "/api/migrations", requests = random.Next(50, 500), averageTime = random.Next(200, 800) },
                new { endpoint = "/api/dashboard/data", requests = random.Next(200, 800), averageTime = random.Next(100, 400) },
                new { endpoint = "/api/auth/status", requests = random.Next(100, 600), averageTime = random.Next(20, 100) }
            },
            byUserRole = new
            {
                admin = new { requests = random.Next(100, 500), users = random.Next(1, 5) },
                user = new { requests = random.Next(300, 1000), users = random.Next(5, 20) },
                readOnly = new { requests = random.Next(50, 200), users = random.Next(10, 50) }
            },
            timeline = Enumerable.Range(0, Math.Min(hours, 24)).Select(h => new
            {
                hour = from.AddHours(h),
                requests = random.Next(20, 200),
                users = random.Next(1, 20)
            })
        };

        return Task.FromResult((object)result);
    }
} 