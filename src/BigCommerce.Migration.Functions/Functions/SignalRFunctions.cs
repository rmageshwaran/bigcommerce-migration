using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker.Extensions.SignalRService;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Functions.Functions
{
    /// <summary>
    /// SignalR functions for managing real-time connections using Azure Functions SignalR bindings
    /// </summary>
    public class SignalRFunctions
    {
        private readonly ILogger<SignalRFunctions> _logger;

        public SignalRFunctions(ILogger<SignalRFunctions> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// SignalR negotiate function for client connections
        /// This function provides connection info to SignalR clients
        /// </summary>
        [Function("negotiate")]
        public async Task<HttpResponseData> Negotiate(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "negotiate")] HttpRequestData req,
            [SignalRConnectionInfoInput(HubName = "migration")] string connectionInfo)
        {
            try
            {
                // Get origin from request headers
                var origin = GetOriginFromRequest(req);
                _logger.LogInformation("SignalR negotiate request - Method: {Method}, Origin: {Origin}", req.Method, origin ?? "null");
                
                // Handle CORS preflight requests
                if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    var optionsResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
                    
                    // Always set CORS headers for allowed origins, fallback to localhost:5173 for dev
                    var corsOrigin = origin ?? "http://localhost:5173";
                    optionsResponse.Headers.Add("Access-Control-Allow-Origin", corsOrigin);
                    optionsResponse.Headers.Add("Access-Control-Allow-Credentials", "true");
                    optionsResponse.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                    optionsResponse.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-requested-with, x-signalr-user-agent, x-ms-signalr-connectionid");
                    
                    _logger.LogInformation("CORS preflight response sent with origin: {Origin}", corsOrigin);
                    return optionsResponse;
                }

                _logger.LogInformation("SignalR negotiate request processed successfully");
                
                var response = req.CreateResponse();
                
                // Always set CORS headers for allowed origins, fallback to localhost:5173 for dev
                var responseOrigin = origin ?? "http://localhost:5173";
                response.Headers.Add("Access-Control-Allow-Origin", responseOrigin);
                response.Headers.Add("Access-Control-Allow-Credentials", "true");
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(connectionInfo);
                
                _logger.LogInformation("SignalR negotiate response sent with origin: {Origin}", responseOrigin);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SignalR negotiate request");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                
                var origin = GetOriginFromRequest(req);
                var errorOrigin = origin ?? "http://localhost:5173";
                errorResponse.Headers.Add("Access-Control-Allow-Origin", errorOrigin);
                errorResponse.Headers.Add("Access-Control-Allow-Credentials", "true");
                await errorResponse.WriteStringAsync("Failed to negotiate SignalR connection");
                return errorResponse;
            }
        }

        /// <summary>
        /// Send migration progress update via SignalR
        /// </summary>
        [Function("send-migration-progress")]
        public async Task<MultiResponse> SendMigrationProgress(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "signalr/migration-progress")] HttpRequestData req)
        {
            try
            {
                var requestBody = await req.ReadAsStringAsync();
                var progressData = System.Text.Json.JsonSerializer.Deserialize<object>(requestBody ?? "{}") ?? new { message = "Empty progress data" };

                _logger.LogInformation("Sending migration progress SignalR message to all connected clients");

                // Create HTTP response
                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new { success = true, message = "Migration progress sent via SignalR" }));

                // Create SignalR message
                var signalRMessage = new SignalRMessageAction("migrationProgress")
                {
                    Arguments = new object[] { progressData }
                };

                _logger.LogInformation("Migration progress SignalR message sent successfully");

                return new MultiResponse
                {
                    HttpResponse = response,
                    SignalRMessage = signalRMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending migration progress SignalR message");
                throw;
            }
        }

        /// <summary>
        /// Send system health update via SignalR
        /// </summary>
        [Function("send-system-health")]
        public async Task<SignalRMessageAction> SendSystemHealth(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "signalr/system-health")] HttpRequestData req)
        {
            try
            {
                var requestBody = await req.ReadAsStringAsync();
                var healthData = System.Text.Json.JsonSerializer.Deserialize<object>(requestBody ?? "{}") ?? new { message = "Empty health data" };

                _logger.LogInformation("Sending system health SignalR message to all connected clients");

                // Create SignalR message
                var signalRMessage = new SignalRMessageAction("systemHealth")
                {
                    Arguments = new object[] { healthData }
                };

                _logger.LogInformation("System health SignalR message sent successfully");

                return signalRMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending system health SignalR message");
                throw;
            }
        }

        /// <summary>
        /// Send migration status update via SignalR (used for completed, failed, cancelled, started)
        /// </summary>
        [Function("send-migration-status")]
        public async Task<MultiResponse> SendMigrationStatus(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "send-migration-status")] HttpRequestData req)
        {
            try
            {
                // Get origin from request headers
                var origin = GetOriginFromRequest(req);
                _logger.LogInformation("Send migration status request - Method: {Method}, Origin: {Origin}", req.Method, origin ?? "null");
                
                // Handle CORS preflight requests
                if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    var optionsResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
                    
                    // Always set CORS headers for allowed origins, fallback to localhost:5173 for dev
                    var statusOptionsCorsOrigin = origin ?? "http://localhost:5173";
                    optionsResponse.Headers.Add("Access-Control-Allow-Origin", statusOptionsCorsOrigin);
                    optionsResponse.Headers.Add("Access-Control-Allow-Credentials", "true");
                    optionsResponse.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                    optionsResponse.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-requested-with");
                    
                    return new MultiResponse
                    {
                        HttpResponse = optionsResponse
                    };
                }

                var requestBody = await req.ReadAsStringAsync();
                var statusData = System.Text.Json.JsonSerializer.Deserialize<object>(requestBody ?? "{}") ?? new { message = "Empty status data" };

                _logger.LogInformation("Sending migration status SignalR message to all connected clients");

                // Create HTTP response
                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                
                // Add CORS headers to response
                var statusResponseCorsOrigin = origin ?? "http://localhost:5173";
                response.Headers.Add("Access-Control-Allow-Origin", statusResponseCorsOrigin);
                response.Headers.Add("Access-Control-Allow-Credentials", "true");
                
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new { success = true, message = "Migration status sent via SignalR" }));

                // Create SignalR message with "MigrationStatus" as the method name
                var signalRMessage = new SignalRMessageAction("MigrationStatus")
                {
                    Arguments = new object[] { statusData }
                };

                _logger.LogInformation("Migration status SignalR message sent successfully");

                return new MultiResponse
                {
                    HttpResponse = response,
                    SignalRMessage = signalRMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending migration status SignalR message");
                
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                errorResponse.Headers.Add("Content-Type", "application/json");
                
                // Add CORS headers to error response too
                var statusErrorOrigin = GetOriginFromRequest(req);
                var statusErrorCorsOrigin = statusErrorOrigin ?? "http://localhost:5173";
                errorResponse.Headers.Add("Access-Control-Allow-Origin", statusErrorCorsOrigin);
                errorResponse.Headers.Add("Access-Control-Allow-Credentials", "true");
                
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message }));
                
                return new MultiResponse
                {
                    HttpResponse = errorResponse
                };
            }
        }

        /// <summary>
        /// SignalR hub info endpoint for diagnostics
        /// </summary>
        [Function("signalr-info")]
        public async Task<HttpResponseData> GetSignalRInfo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "signalr/info")] HttpRequestData req)
        {
            try
            {
                var info = new
                {
                    hubName = "migration",
                    connectionState = "Available",
                    timestamp = DateTime.UtcNow,
                    negotiateEndpoint = "/api/negotiate",
                    progressEndpoint = "/api/signalr/migration-progress",
                    healthEndpoint = "/api/signalr/system-health",
                    note = "Real SignalR message broadcasting is now enabled"
                };

                var response = req.CreateResponse();
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                await response.WriteAsJsonAsync(info);
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting SignalR info");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                errorResponse.Headers.Add("Access-Control-Allow-Origin", "*");
                await errorResponse.WriteStringAsync("Failed to get SignalR info");
                return errorResponse;
            }
        }

        /// <summary>
        /// Get the origin from the request headers if it's in the allowed list
        /// </summary>
        private string? GetOriginFromRequest(HttpRequestData req)
        {
            var allowedOrigins = new[]
            {
                "http://localhost:3000",
                "http://localhost:5173", 
                "https://localhost:3000",
                "https://localhost:5173"
            };

            try
            {
                // Log all headers for debugging
                _logger.LogDebug("Request headers: {Headers}", string.Join(", ", req.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")));

                if (req.Headers.TryGetValues("Origin", out var origins))
                {
                    var origin = origins.FirstOrDefault();
                    _logger.LogInformation("Found Origin header: {Origin}", origin);
                    
                    if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
                    {
                        _logger.LogInformation("Origin {Origin} is allowed", origin);
                        return origin;
                    }
                    else
                    {
                        _logger.LogWarning("Origin {Origin} is not in allowed origins list", origin);
                    }
                }
                else
                {
                    _logger.LogWarning("No Origin header found in request");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting origin from request headers");
            }

            return null;
        }
    }

    /// <summary>
    /// Multiple response class for functions that need to return both HTTP and SignalR responses
    /// </summary>
    public class MultiResponse
    {
        public HttpResponseData? HttpResponse { get; set; }

        [SignalROutput(HubName = "migration")]
        public SignalRMessageAction? SignalRMessage { get; set; }
    }
} 