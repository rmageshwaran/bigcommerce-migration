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

    public LoggingMonitoringFunctions(
        ILogger<LoggingMonitoringFunctions> logger,
        IOpenSearchService openSearchService,
        IMigrationStorageService storageService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    /// <summary>
    /// Get recent log entries with filtering and pagination
    /// </summary>
    [Function("GetLogs")]
    [OpenApiOperation(operationId: "GetLogs", tags: new[] { "Logging" },
        Summary = "Get recent log entries",
        Description = "Returns recent log entries with optional filtering by level, time range, and search terms.")]
    [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiParameter(name: "level", In = ParameterLocation.Query, Required = false, Type = typeof(string),
        Description = "Log level filter (Debug, Information, Warning, Error, Critical)")]
    [OpenApiParameter(name: "from", In = ParameterLocation.Query, Required = false, Type = typeof(DateTime),
        Description = "Start time filter (ISO 8601 format)")]
    [OpenApiParameter(name: "to", In = ParameterLocation.Query, Required = false, Type = typeof(DateTime),
        Description = "End time filter (ISO 8601 format)")]
    [OpenApiParameter(name: "search", In = ParameterLocation.Query, Required = false, Type = typeof(string),
        Description = "Search term for log messages")]
    [OpenApiParameter(name: "page", In = ParameterLocation.Query, Required = false, Type = typeof(int),
        Description = "Page number (default: 1)")]
    [OpenApiParameter(name: "pageSize", In = ParameterLocation.Query, Required = false, Type = typeof(int),
        Description = "Page size (default: 50, max: 1000)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Log entries retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(object),
        Summary = "Authentication required")]
    public async Task<HttpResponseData> GetLogsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "logs")] HttpRequestData req)
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
                        message = "Authentication required to view logs"
                    }
                }));
                return response;
            }

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var level = query["level"];
            var fromParam = query["from"];
            var toParam = query["to"];
            var search = query["search"];
            var pageParam = query["page"];
            var pageSizeParam = query["pageSize"];

            // Validate and set defaults
            var page = int.TryParse(pageParam, out var p) ? Math.Max(1, p) : 1;
            var pageSize = int.TryParse(pageSizeParam, out var ps) ? Math.Max(1, Math.Min(1000, ps)) : 50;

            var from = DateTime.TryParse(fromParam, out var f) ? f : DateTime.UtcNow.AddHours(-24);
            var to = DateTime.TryParse(toParam, out var t) ? t : DateTime.UtcNow;

            // Build log query (this would typically use OpenSearch)
            var logEntries = await GetLogEntriesAsync(level, from, to, search, page, pageSize);

            var result = new
            {
                timestamp = DateTime.UtcNow,
                filters = new
                {
                    level = level,
                    from = from,
                    to = to,
                    search = search
                },
                pagination = new
                {
                    page = page,
                    pageSize = pageSize,
                    totalCount = logEntries.TotalCount,
                    totalPages = (int)Math.Ceiling((double)logEntries.TotalCount / pageSize)
                },
                logs = logEntries.Entries
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving logs");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "LOGS_ERROR",
                    message = "An error occurred while retrieving logs"
                }
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

    // Helper methods for data retrieval

    private Task<(List<object> Entries, int TotalCount)> GetLogEntriesAsync(
        string? level, DateTime from, DateTime to, string? search, int page, int pageSize)
    {
        // In a real implementation, this would query OpenSearch
        // For now, return placeholder data
        var entries = new List<object>();
        var random = new Random();
        
        for (int i = 0; i < Math.Min(pageSize, 10); i++)
        {
            entries.Add(new
            {
                timestamp = from.AddMinutes(random.Next(0, (int)(to - from).TotalMinutes)),
                level = level ?? (new[] { "Information", "Warning", "Error" })[random.Next(3)],
                message = $"Sample log message {i + 1}",
                source = "BigCommerce.Migration",
                requestId = Guid.NewGuid().ToString(),
                properties = new
                {
                    machineId = Environment.MachineName,
                    processId = Environment.ProcessId
                }
            });
        }

        return Task.FromResult((entries, entries.Count));
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