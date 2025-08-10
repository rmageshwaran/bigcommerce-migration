using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Services
{
    /// <summary>
    /// 🎯 CENTRALIZED SIGNALR MESSAGE CONVERTER
    /// Handles consistent naming between backend (PascalCase) and frontend (camelCase).
    /// Eliminates the need for complex transformation functions in the frontend.
    /// 
    /// ✅ SOLVES:
    /// - PascalCase vs camelCase property mismatches
    /// - Complex frontend transformation functions
    /// - Inconsistent property fallback logic
    /// - Missing property mapping
    /// </summary>
    public interface ISignalRMessageConverter
    {
        /// <summary>
        /// Converts a ProgressEvent to a frontend-compatible JSON string with camelCase properties
        /// </summary>
        string ConvertToFrontendJson(ProgressEvent progressEvent);
        
        /// <summary>
        /// Converts a ProgressEvent to a frontend-compatible object with camelCase properties
        /// </summary>
        object ConvertToFrontendObject(ProgressEvent progressEvent);
        
        /// <summary>
        /// Creates a SignalR message action with consistent frontend formatting
        /// </summary>
        object CreateSignalRMessage(ProgressEvent progressEvent);
    }

    /// <summary>
    /// SignalR Message Converter implementation
    /// </summary>
    public class SignalRMessageConverter : ISignalRMessageConverter
    {
        private static readonly JsonSerializerOptions _frontendJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = 
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        /// <summary>
        /// Converts a ProgressEvent to frontend-compatible JSON with camelCase properties
        /// </summary>
        public string ConvertToFrontendJson(ProgressEvent progressEvent)
        {
            if (progressEvent == null)
                throw new ArgumentNullException(nameof(progressEvent));

            try
            {
                return JsonSerializer.Serialize(progressEvent, _frontendJsonOptions);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert ProgressEvent to frontend JSON: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Converts a ProgressEvent to frontend-compatible object with camelCase properties
        /// </summary>
        public object ConvertToFrontendObject(ProgressEvent progressEvent)
        {
            if (progressEvent == null)
                throw new ArgumentNullException(nameof(progressEvent));

            try
            {
                // Serialize to JSON with camelCase, then deserialize to dynamic object
                var json = ConvertToFrontendJson(progressEvent);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json, _frontendJsonOptions) 
                       ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert ProgressEvent to frontend object: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Creates a complete SignalR message with consistent frontend formatting
        /// </summary>
        public object CreateSignalRMessage(ProgressEvent progressEvent)
        {
            if (progressEvent == null)
                throw new ArgumentNullException(nameof(progressEvent));

            try
            {
                var frontendObject = ConvertToFrontendObject(progressEvent);
                
                return new
                {
                    target = progressEvent.HubMethod,
                    arguments = new[] { frontendObject },
                    metadata = new
                    {
                        eventType = progressEvent.EventType,
                        migrationId = progressEvent.MigrationId,
                        timestamp = progressEvent.Timestamp,
                        hubMethod = progressEvent.HubMethod
                    }
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create SignalR message: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// 🎯 SIGNALR CONSISTENCY CONTRACT
    /// Defines the standardized structure that all SignalR events must follow.
    /// This ensures consistency between backend and frontend regardless of the specific event type.
    /// </summary>
    public static class SignalRConsistencyContract
    {
        /// <summary>
        /// All SignalR events MUST have these base properties
        /// </summary>
        public static readonly string[] RequiredBaseProperties = 
        {
            "migrationId",
            "eventType", 
            "timestamp",
            "hubMethod"
        };

        /// <summary>
        /// Optional base properties that should be consistently handled
        /// </summary>
        public static readonly string[] OptionalBaseProperties = 
        {
            "isCancelled",
            "cancellationReason", 
            "cancelledAt",
            "connectionId",
            "groupName"
        };

        /// <summary>
        /// Standardized HubMethod names (consistent naming convention)
        /// </summary>
        public static readonly Dictionary<string, string> StandardizedHubMethods = new Dictionary<string, string>
        {
            ["progress"] = "MigrationProgressUpdated",
            ["batch"] = "BatchProgressUpdated", 
            ["entity"] = "EntityProgressUpdated",
            ["error"] = "ErrorOccurred",
            ["status"] = "MigrationStatusChanged",
            ["subbatch-started"] = "SubBatchStarted",
            ["subbatch-completed"] = "SubBatchCompleted", 
            ["subbatch-progress"] = "SubBatchMigrationProgress"
        };

        /// <summary>
        /// Validates that a ProgressEvent follows the consistency contract
        /// </summary>
        public static ValidationResult ValidateEvent(ProgressEvent progressEvent)
        {
            var result = new ValidationResult();

            if (progressEvent == null)
            {
                result.AddError("ProgressEvent cannot be null");
                return result;
            }

            // Validate required base properties
            if (string.IsNullOrWhiteSpace(progressEvent.MigrationId))
                result.AddError("MigrationId is required");

            if (string.IsNullOrWhiteSpace(progressEvent.EventType))
                result.AddError("EventType is required");

            if (string.IsNullOrWhiteSpace(progressEvent.HubMethod))
                result.AddError("HubMethod is required");

            if (progressEvent.Timestamp == default)
                result.AddError("Timestamp is required");

            // Validate HubMethod consistency
            if (StandardizedHubMethods.ContainsKey(progressEvent.EventType))
            {
                var expectedHubMethod = StandardizedHubMethods[progressEvent.EventType];
                if (progressEvent.HubMethod != expectedHubMethod)
                {
                    result.AddWarning($"HubMethod '{progressEvent.HubMethod}' does not match expected '{expectedHubMethod}' for EventType '{progressEvent.EventType}'");
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Validation result for SignalR event consistency
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Collection of validation errors
        /// </summary>
        public List<string> Errors { get; } = new List<string>();
        
        /// <summary>
        /// Collection of validation warnings
        /// </summary>
        public List<string> Warnings { get; } = new List<string>();
        
        /// <summary>
        /// Gets whether the validation result is valid (no errors)
        /// </summary>
        public bool IsValid => Errors.Count == 0;
        
        /// <summary>
        /// Gets whether the validation result has warnings
        /// </summary>
        public bool HasWarnings => Warnings.Count > 0;

        /// <summary>
        /// Adds an error to the validation result
        /// </summary>
        /// <param name="error">Error message to add</param>
        public void AddError(string error) => Errors.Add(error);
        
        /// <summary>
        /// Adds a warning to the validation result
        /// </summary>
        /// <param name="warning">Warning message to add</param>
        public void AddWarning(string warning) => Warnings.Add(warning);

        /// <summary>
        /// Returns a string representation of the validation result
        /// </summary>
        /// <returns>String containing errors and warnings</returns>
        public override string ToString()
        {
            var messages = new List<string>();
            
            if (Errors.Count > 0)
                messages.Add($"Errors: {string.Join(", ", Errors)}");
                
            if (Warnings.Count > 0)
                messages.Add($"Warnings: {string.Join(", ", Warnings)}");
                
            return messages.Count > 0 ? string.Join("; ", messages) : "Valid";
        }
    }

    /// <summary>
    /// 🎯 ENHANCED PROGRESS EVENT PUBLISHER
    /// Enhanced version of the existing publisher that uses the centralized factory and converter
    /// </summary>
    public interface IEnhancedProgressEventPublisher
    {
        /// <summary>
        /// Publishes a migration progress event using the centralized factory
        /// </summary>
        Task PublishMigrationProgressAsync(string migrationId, MigrationProgressOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes a batch progress event using the centralized factory
        /// </summary>
        Task PublishBatchProgressAsync(string migrationId, BatchProgressOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes an entity progress event using the centralized factory
        /// </summary>
        Task PublishEntityProgressAsync(string migrationId, EntityProgressOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes an error progress event using the centralized factory
        /// </summary>
        Task PublishErrorProgressAsync(string migrationId, ErrorProgressOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes a status progress event using the centralized factory
        /// </summary>
        Task PublishStatusProgressAsync(string migrationId, StatusProgressOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes a sub-batch started event using the centralized factory
        /// </summary>
        Task PublishSubBatchStartedAsync(string migrationId, SubBatchStartedOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes a sub-batch completed event using the centralized factory
        /// </summary>
        Task PublishSubBatchCompletedAsync(string migrationId, SubBatchCompletedOptions options, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Publishes a sub-batch progress event using the centralized factory
        /// </summary>
        Task PublishSubBatchProgressAsync(string migrationId, SubBatchProgressOptions options, CancellationToken cancellationToken = default);
    }
} 