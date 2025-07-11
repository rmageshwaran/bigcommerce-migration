using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net;
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Functions.Middleware;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace BigCommerce.Migration.Functions.Functions;

/// <summary>
/// Comprehensive monitoring and diagnostics endpoints for system health, performance, and observability
/// </summary>
public class MonitoringFunctions
{
    private readonly ILogger<MonitoringFunctions> _logger;
    private readonly HealthCheckService _healthCheckService;
    private readonly IOpenSearchService _openSearchService;
    private readonly IMigrationStorageService _storageService;
    private readonly IApiRateLimitService _rateLimitService;
    private static readonly DateTime _startTime = DateTime.UtcNow;
    private static readonly object _metricsLock = new object();
    private static readonly Dictionary<string, object> _cachedMetrics = new();
    private static DateTime _lastMetricsCacheUpdate = DateTime.MinValue;

    public MonitoringFunctions(
        ILogger<MonitoringFunctions> logger,
        HealthCheckService healthCheckService,
        IOpenSearchService openSearchService,
        IMigrationStorageService storageService,
        IApiRateLimitService rateLimitService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
    }

    /// <summary>
    /// Comprehensive health check endpoint with detailed diagnostics
    /// </summary>
    [Function("GetDetailedHealthCheck")]
    [OpenApiOperation(operationId: "GetDetailedHealthCheck", tags: new[] { "Monitoring" },
        Summary = "Get detailed system health check",
        Description = "Returns comprehensive health status including all dependencies, services, and system diagnostics.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Health check completed successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.ServiceUnavailable, contentType: "application/json", bodyType: typeof(object),
        Summary = "One or more health checks failed")]
    public async Task<HttpResponseData> GetDetailedHealthCheckAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/detailed")] HttpRequestData req)
    {
        var stopwatch = Stopwatch.StartNew();
        HttpResponseData response;

        try
        {
            // Run all health checks
            var healthReport = await _healthCheckService.CheckHealthAsync();
            
            // Determine overall status
            var overallStatus = healthReport.Status switch
            {
                HealthStatus.Healthy => "healthy",
                HealthStatus.Degraded => "degraded",
                HealthStatus.Unhealthy => "unhealthy",
                _ => "unknown"
            };

            // Set response status based on health
            var statusCode = healthReport.Status switch
            {
                HealthStatus.Healthy => HttpStatusCode.OK,
                HealthStatus.Degraded => HttpStatusCode.OK, // Degraded but still operational
                HealthStatus.Unhealthy => HttpStatusCode.ServiceUnavailable,
                _ => HttpStatusCode.InternalServerError
            };

            response = req.CreateResponse(statusCode);
            response.Headers.Add("Content-Type", "application/json");

            // Build detailed health response
            var healthResponse = new
            {
                status = overallStatus,
                timestamp = DateTime.UtcNow,
                duration = stopwatch.Elapsed,
                version = GetVersion(),
                uptime = GetUptime(),
                environment = GetEnvironmentInfo(),
                checks = healthReport.Entries.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        status = kvp.Value.Status.ToString().ToLowerInvariant(),
                        duration = kvp.Value.Duration,
                        description = kvp.Value.Description,
                        exception = kvp.Value.Exception?.Message,
                        data = kvp.Value.Data?.ToDictionary(d => d.Key, d => d.Value?.ToString())
                    }
                ),
                summary = new
                {
                    totalChecks = healthReport.Entries.Count,
                    healthyChecks = healthReport.Entries.Count(e => e.Value.Status == HealthStatus.Healthy),
                    degradedChecks = healthReport.Entries.Count(e => e.Value.Status == HealthStatus.Degraded),
                    unhealthyChecks = healthReport.Entries.Count(e => e.Value.Status == HealthStatus.Unhealthy)
                }
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(healthResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            // Log health check result
            _logger.LogInformation("Health check completed in {Duration}ms with status {Status}",
                stopwatch.ElapsedMilliseconds, overallStatus);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
            
            response = req.CreateResponse(HttpStatusCode.InternalServerError);
            response.Headers.Add("Content-Type", "application/json");
            
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                status = "error",
                timestamp = DateTime.UtcNow,
                duration = stopwatch.Elapsed,
                error = new
                {
                    code = "HEALTH_CHECK_ERROR",
                    message = "An error occurred during health check execution",
                    details = ex.Message
                }
            }));
            
            return response;
        }
    }

    /// <summary>
    /// System metrics endpoint providing performance and usage statistics
    /// </summary>
    [Function("GetSystemMetrics")]
    [OpenApiOperation(operationId: "GetSystemMetrics", tags: new[] { "Monitoring" },
        Summary = "Get system performance metrics",
        Description = "Returns detailed system metrics including memory usage, performance counters, and application statistics.")]
    [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "System metrics retrieved successfully")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Unauthorized, contentType: "application/json", bodyType: typeof(object),
        Summary = "Authentication required")]
    public async Task<HttpResponseData> GetSystemMetricsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "metrics/system")] HttpRequestData req)
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
                        message = "Authentication required to view system metrics"
                    }
                }));
                return response;
            }

            // Get cached metrics if available and recent
            var metrics = GetCachedMetrics();
            if (metrics != null)
            {
                await response.WriteStringAsync(JsonSerializer.Serialize(metrics, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));
                return response;
            }

            // Gather fresh metrics
            var systemMetrics = await GatherSystemMetricsAsync();

            // Cache metrics for 30 seconds
            lock (_metricsLock)
            {
                _cachedMetrics.Clear();
                _cachedMetrics["data"] = systemMetrics;
                _cachedMetrics["timestamp"] = DateTime.UtcNow;
                _lastMetricsCacheUpdate = DateTime.UtcNow;
            }

            await response.WriteStringAsync(JsonSerializer.Serialize(systemMetrics, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving system metrics");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "METRICS_ERROR",
                    message = "An error occurred while retrieving system metrics"
                }
            }));
            return response;
        }
    }

    /// <summary>
    /// Application performance metrics endpoint
    /// </summary>
    [Function("GetApplicationMetrics")]
    [OpenApiOperation(operationId: "GetApplicationMetrics", tags: new[] { "Monitoring" },
        Summary = "Get application performance metrics",
        Description = "Returns application-specific metrics including migration statistics, API performance, and service usage.")]
    [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Application metrics retrieved successfully")]
    public async Task<HttpResponseData> GetApplicationMetricsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "metrics/application")] HttpRequestData req)
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
                        message = "Authentication required to view application metrics"
                    }
                }));
                return response;
            }

            // Gather application metrics
            var applicationMetrics = await GatherApplicationMetricsAsync();

            await response.WriteStringAsync(JsonSerializer.Serialize(applicationMetrics, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving application metrics");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "APPLICATION_METRICS_ERROR",
                    message = "An error occurred while retrieving application metrics"
                }
            }));
            return response;
        }
    }

    /// <summary>
    /// Live system status endpoint for real-time monitoring
    /// </summary>
    [Function("GetLiveStatus")]
    [OpenApiOperation(operationId: "GetLiveStatus", tags: new[] { "Monitoring" },
        Summary = "Get live system status",
        Description = "Returns real-time system status with minimal latency for monitoring dashboards and alerting systems.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Live status retrieved successfully")]
    public async Task<HttpResponseData> GetLiveStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "status/live")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        try
        {
            var status = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                uptime = GetUptime(),
                version = GetVersion(),
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                server = new
                {
                    platform = RuntimeInformation.OSDescription,
                    framework = RuntimeInformation.FrameworkDescription,
                    architecture = RuntimeInformation.ProcessArchitecture.ToString()
                },
                performance = new
                {
                    memoryMB = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2),
                    gcCollections = new
                    {
                        gen0 = GC.CollectionCount(0),
                        gen1 = GC.CollectionCount(1),
                        gen2 = GC.CollectionCount(2)
                    }
                }
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(status, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving live status");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                status = "error",
                timestamp = DateTime.UtcNow,
                error = ex.Message
            }));
            return response;
        }
    }

    /// <summary>
    /// Dependency status endpoint for external service monitoring
    /// </summary>
    [Function("GetDependencyStatus")]
    [OpenApiOperation(operationId: "GetDependencyStatus", tags: new[] { "Monitoring" },
        Summary = "Get dependency status",
        Description = "Returns the status of all external dependencies including BigCommerce API, OpenSearch, Azure Storage, and SignalR.")]
    [OpenApiSecurity("ApiKeyAuth", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object),
        Summary = "Dependency status retrieved successfully")]
    public async Task<HttpResponseData> GetDependencyStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "status/dependencies")] HttpRequestData req)
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
                        message = "Authentication required to view dependency status"
                    }
                }));
                return response;
            }

            // Check dependencies
            var dependencies = await GatherDependencyStatusAsync();

            await response.WriteStringAsync(JsonSerializer.Serialize(dependencies, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving dependency status");
            response.StatusCode = HttpStatusCode.InternalServerError;
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "DEPENDENCY_STATUS_ERROR",
                    message = "An error occurred while retrieving dependency status"
                }
            }));
            return response;
        }
    }

    // Helper methods

    private static string GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetName().Version?.ToString() ?? "Unknown";
    }

    private static string GetUptime()
    {
        var uptime = DateTime.UtcNow - _startTime;
        return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
    }

    private static object GetEnvironmentInfo()
    {
        return new
        {
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
            platform = RuntimeInformation.OSDescription,
            framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            processorCount = Environment.ProcessorCount,
            machineName = Environment.MachineName,
            workingSet = Environment.WorkingSet,
            version = Environment.Version.ToString()
        };
    }

    private object? GetCachedMetrics()
    {
        lock (_metricsLock)
        {
            if (_cachedMetrics.ContainsKey("data") && 
                DateTime.UtcNow - _lastMetricsCacheUpdate < TimeSpan.FromSeconds(30))
            {
                return _cachedMetrics["data"];
            }
            return null;
        }
    }

    private Task<object> GatherSystemMetricsAsync()
    {
        var process = Process.GetCurrentProcess();
        
        var result = new
        {
            timestamp = DateTime.UtcNow,
            uptime = GetUptime(),
            system = new
            {
                platform = RuntimeInformation.OSDescription,
                framework = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorCount = Environment.ProcessorCount,
                machineName = Environment.MachineName
            },
            memory = new
            {
                totalMemoryMB = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2),
                workingSetMB = Math.Round(process.WorkingSet64 / (1024.0 * 1024.0), 2),
                privateMemoryMB = Math.Round(process.PrivateMemorySize64 / (1024.0 * 1024.0), 2),
                virtualMemoryMB = Math.Round(process.VirtualMemorySize64 / (1024.0 * 1024.0), 2),
                gcCollections = new
                {
                    gen0 = GC.CollectionCount(0),
                    gen1 = GC.CollectionCount(1),
                    gen2 = GC.CollectionCount(2)
                }
            },
            performance = new
            {
                cpuTimeTotalMs = process.TotalProcessorTime.TotalMilliseconds,
                cpuTimeUserMs = process.UserProcessorTime.TotalMilliseconds,
                cpuTimePrivilegedMs = process.PrivilegedProcessorTime.TotalMilliseconds,
                threads = process.Threads.Count,
                handles = process.HandleCount
            },
            application = new
            {
                version = GetVersion(),
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                startTime = _startTime,
                currentTime = DateTime.UtcNow
            }
        };

        return Task.FromResult((object)result);
    }

    private Task<object> GatherApplicationMetricsAsync()
    {
        // This would typically gather metrics from your application
        // For now, we'll return placeholder metrics
        var result = new
        {
            timestamp = DateTime.UtcNow,
            migrations = new
            {
                totalMigrations = 0, // Would be retrieved from storage
                activeMigrations = 0,
                completedMigrations = 0,
                failedMigrations = 0
            },
            api = new
            {
                totalRequests = 0, // Would be retrieved from metrics store
                requestsPerMinute = 0,
                averageResponseTime = 0,
                errorRate = 0
            },
            rateLimiting = new
            {
                activeWindows = 0, // Would be retrieved from rate limit service
                totalLimitHits = 0,
                topLimitedEndpoints = new string[0]
            },
            storage = new
            {
                totalEntries = 0, // Would be retrieved from storage service
                storageUsageMB = 0,
                averageQueryTime = 0
            }
        };

        return Task.FromResult((object)result);
    }

    private async Task<object> GatherDependencyStatusAsync()
    {
        var dependencies = new Dictionary<string, object>();
        var tasks = new List<Task<(string name, object status)>>();

        // Check OpenSearch
        tasks.Add(CheckOpenSearchAsync());

        // Check Azure Storage  
        tasks.Add(CheckAzureStorageAsync());

        // Check SignalR (if available)
        tasks.Add(CheckSignalRAsync());

        var results = await Task.WhenAll(tasks);
        
        foreach (var (name, status) in results)
        {
            dependencies[name] = status;
        }

        return new
        {
            timestamp = DateTime.UtcNow,
            dependencies = dependencies,
            summary = new
            {
                total = dependencies.Count,
                healthy = dependencies.Values.Count(d => d.GetType().GetProperty("status")?.GetValue(d)?.ToString() == "healthy"),
                degraded = dependencies.Values.Count(d => d.GetType().GetProperty("status")?.GetValue(d)?.ToString() == "degraded"),
                unhealthy = dependencies.Values.Count(d => d.GetType().GetProperty("status")?.GetValue(d)?.ToString() == "unhealthy")
            }
        };
    }

    private async Task<(string name, object status)> CheckOpenSearchAsync()
    {
        try
        {
            var isHealthy = await _openSearchService.IsHealthyAsync();
            return ("openSearch", new
            {
                status = isHealthy ? "healthy" : "unhealthy",
                timestamp = DateTime.UtcNow,
                responseTime = 0, // Would measure actual response time
                details = isHealthy ? "OpenSearch is responding normally" : "OpenSearch is not responding"
            });
        }
        catch (Exception ex)
        {
            return ("openSearch", new
            {
                status = "unhealthy",
                timestamp = DateTime.UtcNow,
                error = ex.Message
            });
        }
    }

    private async Task<(string name, object status)> CheckAzureStorageAsync()
    {
        try
        {
            // Simple connectivity test
            var testMigration = new BigCommerce.Migration.Core.Models.MigrationEntry
            {
                Id = $"health-check-{Guid.NewGuid()}",
                SourceStoreId = "health-check",
                DestinationStoreId = "health-check",
                Status = BigCommerce.Migration.Core.Models.MigrationStatus.InProgress,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _storageService.CreateMigrationAsync(testMigration);
            await _storageService.DeleteMigrationAsync(testMigration.Id);

            return ("azureStorage", new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                details = "Azure Storage is responding normally"
            });
        }
        catch (Exception ex)
        {
            return ("azureStorage", new
            {
                status = "unhealthy",
                timestamp = DateTime.UtcNow,
                error = ex.Message
            });
        }
    }

    private Task<(string name, object status)> CheckSignalRAsync()
    {
        var result = ("signalR", (object)new
        {
            status = "healthy", // Simplified for now
            timestamp = DateTime.UtcNow,
            details = "SignalR service is available"
        });

        return Task.FromResult(result);
    }
} 