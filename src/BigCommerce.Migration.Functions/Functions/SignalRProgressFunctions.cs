using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.SignalRService;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Functions.Functions
{
    /// <summary>
    /// Azure Functions for processing progress events from queues and broadcasting via SignalR
    /// SOLID: Single Responsibility - handles only queue-to-SignalR broadcasting
    /// Simple approach: Direct SignalR output bindings, no complex HTTP services
    /// </summary>
    public class SignalRProgressFunctions
    {
        private readonly ILogger<SignalRProgressFunctions> _logger;

        public SignalRProgressFunctions(ILogger<SignalRProgressFunctions> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Processes progress events from the queue and broadcasts them via SignalR
        /// SOLID: Single Responsibility - handles progress event broadcasting only
        /// </summary>
        [Function("ProcessProgressEvents")]
        [SignalROutput(HubName = "migrationhub", ConnectionStringSetting = "AzureSignalR")]
        public SignalRMessageAction ProcessProgressEvents(
            [QueueTrigger("signalr-progress-events", Connection = "AzureWebJobsStorage")] string queueMessage)
        {
            try
            {
                _logger.LogInformation("🎯 [SIGNALR-FUNC] === QUEUE MESSAGE RECEIVED ===");
                _logger.LogInformation("🎯 [SIGNALR-FUNC] Raw queue message length: {MessageLength}", queueMessage?.Length ?? 0);
                _logger.LogInformation("🎯 [SIGNALR-FUNC] Raw message preview (first 200 chars): {MessagePreview}", 
                    queueMessage?.Length > 200 ? queueMessage.Substring(0, 200) + "..." : queueMessage ?? "null");

                // Check for empty or whitespace-only messages first
                if (string.IsNullOrWhiteSpace(queueMessage))
                {
                    _logger.LogWarning("❌ [SIGNALR-FUNC] Received empty or whitespace-only queue message");
                    return new SignalRMessageAction("error", new object[] { "Invalid JSON" });
                }

                // Try to detect if message is Base64 encoded (like migration-start queue)
                string actualJsonMessage;
                bool wasBase64Encoded = false;
                
                try
                {
                    _logger.LogInformation("🔍 [SIGNALR-FUNC] Attempting Base64 decode...");
                    var decodedBytes = Convert.FromBase64String(queueMessage);
                    actualJsonMessage = System.Text.Encoding.UTF8.GetString(decodedBytes);
                    wasBase64Encoded = true;
                    _logger.LogInformation("✅ [SIGNALR-FUNC] Successfully Base64 decoded message! Length: {DecodedLength}", actualJsonMessage.Length);
                    _logger.LogInformation("🔍 [SIGNALR-FUNC] Decoded JSON preview: {JsonPreview}", 
                        actualJsonMessage.Length > 300 ? actualJsonMessage.Substring(0, 300) + "..." : actualJsonMessage);
                }
                catch (FormatException)
                {
                    _logger.LogInformation("ℹ️  [SIGNALR-FUNC] Message is not Base64 encoded, treating as raw JSON");
                    actualJsonMessage = queueMessage;
                }

                _logger.LogInformation("📝 [SIGNALR-FUNC] Using message for deserialization (Base64: {WasBase64}): {MessageLength} chars", 
                    wasBase64Encoded, actualJsonMessage.Length);

                // Deserialize the progress event using type discrimination
                _logger.LogInformation("🔄 [SIGNALR-FUNC] Starting JSON deserialization...");
                ProgressEvent? progressEvent;
                try
                {
                    progressEvent = DeserializeProgressEvent(actualJsonMessage);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "❌ [SIGNALR-FUNC] Failed to parse progress event JSON from queue. Base64Decoded: {WasBase64}, JSON: {Message}", 
                        wasBase64Encoded, actualJsonMessage);
                    return new SignalRMessageAction("error", new object[] { "Invalid JSON" });
                }

                if (progressEvent == null)
                {
                    _logger.LogWarning("❌ [SIGNALR-FUNC] Failed to deserialize progress event, returning error");
                    _logger.LogWarning("❌ [SIGNALR-FUNC] Problematic JSON (Base64: {WasBase64}): {Json}", wasBase64Encoded, actualJsonMessage);
                    return new SignalRMessageAction("error", new object[] { "Invalid message" });
                }

                _logger.LogInformation("✅ [SIGNALR-FUNC] Successfully deserialized progress event!");
                _logger.LogInformation("📊 [SIGNALR-FUNC] Event details - MigrationId: {MigrationId}, EventType: {EventType}, HubMethod: {HubMethod}", 
                    progressEvent.MigrationId, progressEvent.EventType, progressEvent.HubMethod);

                // Log the complete deserialized event for debugging
                try
                {
                    var eventJson = JsonSerializer.Serialize(progressEvent, new JsonSerializerOptions { WriteIndented = true });
                    _logger.LogInformation("📋 [SIGNALR-FUNC] Complete deserialized event:\n{EventJson}", eventJson);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ [SIGNALR-FUNC] Failed to serialize event for logging");
                }

                // Create SignalR message action based on event type
                _logger.LogInformation("📡 [SIGNALR-FUNC] Creating SignalR message for broadcast...");
                var signalRMessage = CreateSignalRMessage(progressEvent);
                
                _logger.LogInformation("🚀 [SIGNALR-FUNC] Successfully created SignalR message! About to broadcast via SignalR...");
                _logger.LogInformation("🚀 [SIGNALR-FUNC] SignalR broadcast details - Target: {HubMethod}, MigrationId: {MigrationId}, EventType: {EventType}",
                    progressEvent.HubMethod, progressEvent.MigrationId, progressEvent.EventType);

                _logger.LogInformation("✅ [SIGNALR-FUNC] === RETURNING SIGNALR MESSAGE FOR BROADCAST ===");
                return signalRMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 [SIGNALR-FUNC] FATAL: Failed to process progress event from queue. Raw message: {Message}", queueMessage);
                // Don't rethrow - this would cause infinite retries
                return new SignalRMessageAction("error", new object[] { "Processing failed" });
            }
        }

        /// <summary>
        /// Deserializes a progress event from JSON by determining the correct concrete type
        /// SOLID: Single Responsibility - handles only type-specific deserialization
        /// </summary>
        private ProgressEvent? DeserializeProgressEvent(string queueMessage)
        {
            _logger.LogInformation("🔄 [DESERIALIZE] Starting progress event deserialization...");
            _logger.LogInformation("🔄 [DESERIALIZE] Input JSON length: {JsonLength}", queueMessage?.Length ?? 0);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            try
            {
                // First, deserialize as a JsonDocument to inspect the eventType
                _logger.LogInformation("🔄 [DESERIALIZE] Parsing JSON document...");
                using var document = JsonDocument.Parse(queueMessage ?? string.Empty);
                
                // Handle null or empty JSON
                if (document.RootElement.ValueKind == JsonValueKind.Null)
                {
                    _logger.LogWarning("❌ [DESERIALIZE] JSON root element is null");
                    return null;
                }

                _logger.LogInformation("🔄 [DESERIALIZE] JSON parsed successfully, looking for eventType property...");

                if (!document.RootElement.TryGetProperty("eventType", out var eventTypeElement))
                {
                    _logger.LogWarning("❌ [DESERIALIZE] No eventType property found in progress event JSON");
                    _logger.LogWarning("❌ [DESERIALIZE] Available properties: {Properties}", 
                        string.Join(", ", document.RootElement.EnumerateObject().Select(p => p.Name)));
                    return null;
                }

                var eventType = eventTypeElement.GetString();
                _logger.LogInformation("🔄 [DESERIALIZE] Found eventType: {EventType}", eventType);

                ProgressEvent? result = eventType switch
                {
                    "progress" => JsonSerializer.Deserialize<MigrationProgressEvent>(queueMessage ?? string.Empty, options),
                    "batch" => JsonSerializer.Deserialize<BatchProgressEvent>(queueMessage ?? string.Empty, options),
                    "entity" => JsonSerializer.Deserialize<EntityProgressEvent>(queueMessage ?? string.Empty, options),
                    "error" => JsonSerializer.Deserialize<ErrorProgressEvent>(queueMessage ?? string.Empty, options),
                    "status" => JsonSerializer.Deserialize<StatusProgressEvent>(queueMessage ?? string.Empty, options),
                    _ => null
                };

                if (result != null)
                {
                    _logger.LogInformation("✅ [DESERIALIZE] Successfully deserialized to {EventTypeName}", result.GetType().Name);
                    _logger.LogInformation("✅ [DESERIALIZE] Deserialized event - MigrationId: {MigrationId}, HubMethod: {HubMethod}", 
                        result.MigrationId, result.HubMethod);
                }
                else
                {
                    _logger.LogWarning("❌ [DESERIALIZE] Deserialization returned null for eventType: {EventType}", eventType);
                }

                return result;
            }
            catch (Exception ex) when (!(ex is JsonException))
            {
                _logger.LogError(ex, "💥 [DESERIALIZE] Unexpected error during progress event deserialization");
                return null;
            }
        }

        /// <summary>
        /// Creates a SignalR message action from a progress event
        /// SOLID: Single Responsibility - handles only SignalR message creation
        /// </summary>
        private SignalRMessageAction CreateSignalRMessage(ProgressEvent progressEvent)
        {
            try
            {
                _logger.LogInformation("🔨 [SIGNALR-CREATE] Creating SignalR message...");
                _logger.LogInformation("🔨 [SIGNALR-CREATE] Target hub method: {HubMethod}", progressEvent.HubMethod);
                _logger.LogInformation("🔨 [SIGNALR-CREATE] Message payload type: {PayloadType}", progressEvent.GetType().Name);

                // Log the payload that will be sent to SignalR clients
                try
                {
                    var payloadJson = JsonSerializer.Serialize(progressEvent, new JsonSerializerOptions { WriteIndented = true });
                    _logger.LogInformation("🔨 [SIGNALR-CREATE] SignalR payload:\n{PayloadJson}", payloadJson);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ [SIGNALR-CREATE] Failed to serialize payload for logging");
                }

                // Create SignalR message with proper constructor
                var signalRMessage = new SignalRMessageAction(progressEvent.HubMethod, new object[] { progressEvent });
                
                _logger.LogInformation("✅ [SIGNALR-CREATE] Successfully created SignalR message action!");
                return signalRMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "💥 [SIGNALR-CREATE] Failed to create SignalR message from progress event. MigrationId: {MigrationId}, EventType: {EventType}",
                    progressEvent.MigrationId,
                    progressEvent.EventType);
                return new SignalRMessageAction("error", new object[] { "Failed to create message" });
            }
        }

        /// <summary>
        /// SignalR negotiate endpoint accessible via /api/SignalRNegotiation
        /// Required by SignalR client SDK - clients will call this as: hubUrl + "/negotiate"
        /// </summary>
        [Function("SignalRNegotiation")]
        public async Task<HttpResponseData> GetSignalRConnectionInfo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options")] HttpRequestData req,
            [SignalRConnectionInfoInput(HubName = "migrationhub", ConnectionStringSetting = "AzureSignalR")] 
            SignalRConnectionInfo connectionInfo)
        {
            // Handle CORS preflight request (OPTIONS)
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Handling CORS preflight request for SignalR negotiate");
                var corsResponse = req.CreateResponse(HttpStatusCode.OK);
                
                // Add CORS headers for preflight response
                corsResponse.Headers.Add("Access-Control-Allow-Origin", "*");
                corsResponse.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                corsResponse.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-ms-signalr-connectionid, x-signalr-user-agent, x-functions-key");
                corsResponse.Headers.Add("Access-Control-Max-Age", "86400"); // 24 hours
                
                return corsResponse;
            }

            // Handle actual negotiate request (POST)
            _logger.LogDebug("Providing SignalR connection info for client via /api/SignalRNegotiation");
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            
            // Add CORS headers to actual response
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            // Don't manually set Content-Type - WriteAsJsonAsync will handle it
            
            // Write SignalR connection info as JSON
            await response.WriteAsJsonAsync(connectionInfo);
            
            return response;
        }

        /// <summary>
        /// Handles client connections to SignalR hub
        /// Simple approach: Log connections for monitoring
        /// </summary>
        [Function("OnSignalRConnected")]
        public Task OnConnected(
            [SignalRTrigger("migrationHub", "connections", "connected", ConnectionStringSetting = "AzureSignalR")] 
            SignalRInvocationContext invocationContext)
        {
            _logger.LogInformation("Client connected to SignalR hub. ConnectionId: {ConnectionId}", 
                invocationContext.ConnectionId);
            
            // Optional: Add connection to a default group for broadcasting
            // This can be enhanced later for user-specific groups
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles client disconnections from SignalR hub
        /// Simple approach: Log disconnections for monitoring
        /// </summary>
        [Function("OnSignalRDisconnected")]
        public Task OnDisconnected(
            [SignalRTrigger("migrationHub", "connections", "disconnected", ConnectionStringSetting = "AzureSignalR")] 
            SignalRInvocationContext invocationContext)
        {
            _logger.LogInformation("Client disconnected from SignalR hub. ConnectionId: {ConnectionId}", 
                invocationContext.ConnectionId);
                
            return Task.CompletedTask;
        }
    }
} 