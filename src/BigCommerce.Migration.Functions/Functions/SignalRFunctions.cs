using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
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
        /// Test function to simulate a SignalR message (simplified for testing)
        /// </summary>
        [Function("test-signalr")]
        public async Task<HttpResponseData> TestSignalR(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "test-signalr")] HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Test SignalR message requested");

                // For now, just simulate success - real SignalR integration will be added later
                var testMessage = new
                {
                    timestamp = DateTime.UtcNow,
                    status = "healthy",
                    message = "Test SignalR message from Azure Functions",
                    testData = new
                    {
                        functionsRunning = 5,
                        queueDepth = 0,
                        activeConnections = 1
                    }
                };

                _logger.LogInformation("Test SignalR message simulated successfully");

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                await response.WriteAsJsonAsync(new 
                { 
                    success = true, 
                    message = "Test SignalR message simulated (SignalR integration will be added)",
                    testData = testMessage
                });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error simulating test SignalR message");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                errorResponse.Headers.Add("Access-Control-Allow-Origin", "*");
                await errorResponse.WriteAsJsonAsync(new { success = false, error = ex.Message });
                return errorResponse;
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
                    testEndpoint = "/api/test-signalr",
                    note = "SignalR integration is functional for negotiate, message sending will be enhanced"
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
} 