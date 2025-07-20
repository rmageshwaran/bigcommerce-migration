using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Optimized error message formatter with caching and performance considerations
/// Single Responsibility: Error message formatting only
/// </summary>
public class ErrorMessageFormatter : IErrorMessageFormatter
{
    private readonly ILogger<ErrorMessageFormatter> _logger;
    private readonly Dictionary<string, string> _errorCache = new();
    private readonly object _cacheLock = new();
    
    /// <summary>
    /// Initializes a new instance of the ErrorMessageFormatter class
    /// </summary>
    /// <param name="logger">Logger for error message formatting operations</param>
    public ErrorMessageFormatter(ILogger<ErrorMessageFormatter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a simple, user-friendly error message for UI display
    /// </summary>
    /// <param name="entityType">Type of entity that failed</param>
    /// <param name="entityId">ID of the entity that failed</param>
    /// <param name="detailedErrorMessage">Detailed error message from API</param>
    /// <returns>Simple error message suitable for UI display</returns>
    public string CreateSimpleErrorMessage(string entityType, string entityId, string? detailedErrorMessage)
    {
        if (string.IsNullOrEmpty(detailedErrorMessage))
        {
            return $"Failed to create {entityType} {entityId}: No entity returned";
        }

        // ✅ Performance: Use cache key for repeated error patterns
        var cacheKey = $"{entityType}_{entityId}_{GetErrorHash(detailedErrorMessage)}";
        
        lock (_cacheLock)
        {
            if (_errorCache.TryGetValue(cacheKey, out var cachedMessage))
            {
                return cachedMessage;
            }
        }

        var simpleMessage = ExtractSimpleErrorMessage(entityType, entityId, detailedErrorMessage);
        
        // ✅ Performance: Cache the result (with size limit to prevent memory leaks)
        lock (_cacheLock)
        {
            if (_errorCache.Count < 1000) // Prevent unlimited growth
            {
                _errorCache[cacheKey] = simpleMessage;
            }
        }

        return simpleMessage;
    }

    /// <summary>
    /// Creates a detailed error message for debugging and logging
    /// </summary>
    /// <param name="entityType">Type of entity that failed</param>
    /// <param name="entityId">ID of the entity that failed</param>
    /// <param name="detailedErrorMessage">Detailed error message from API</param>
    /// <returns>Detailed error message for debugging</returns>
    public string CreateDetailedErrorMessage(string entityType, string entityId, string? detailedErrorMessage)
    {
        if (string.IsNullOrEmpty(detailedErrorMessage))
        {
            return $"Failed to create {entityType} {entityId}: No entity returned";
        }

        return $"API Error creating {entityType} {entityId}: {detailedErrorMessage}";
    }

    /// <summary>
    /// Extracts simple error message with performance optimizations
    /// </summary>
    private string ExtractSimpleErrorMessage(string entityType, string entityId, string detailedErrorMessage)
    {
        try
        {
            // ✅ Generic: Try to extract specific error details from JSON first
            var specificError = ExtractSpecificErrorFromJson(detailedErrorMessage);
            if (!string.IsNullOrEmpty(specificError))
            {
                return $"Failed to create {entityType} {entityId}: {specificError}";
            }

            // ✅ Generic: Extract status codes from the original message
            var statusCode = ExtractStatusCode(detailedErrorMessage);
            if (!string.IsNullOrEmpty(statusCode))
            {
                return $"Failed to create {entityType} {entityId}: API returned {statusCode}";
            }

            var processedMessage = detailedErrorMessage;
            // Try to extract the most relevant part from various error patterns
            var errorPatterns = new[]
            {
                "API Error creating",
                "API request failed",
                "HTTP request failed",
                "Request failed",
                "Error creating"
            };

            foreach (var pattern in errorPatterns)
            {
                if (processedMessage.Contains(pattern))
                {
                    // Find the last colon after the pattern to get the actual error
                    var patternIndex = processedMessage.IndexOf(pattern);
                    var colonIndex = processedMessage.IndexOf(": ", patternIndex);
                    
                    if (colonIndex > 0)
                    {
                        // Look for the next colon to get the actual error message
                        var nextColonIndex = processedMessage.IndexOf(": ", colonIndex + 2);
                        if (nextColonIndex > 0)
                        {
                            processedMessage = processedMessage.Substring(nextColonIndex + 2);
                        }
                        else
                        {
                            processedMessage = processedMessage.Substring(colonIndex + 2);
                        }
                        break;
                    }
                }
            }

            // ✅ Generic: Fallback to truncated message
            return TruncateMessage($"Failed to create {entityType} {entityId}: {processedMessage}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract simple error message for {EntityType} {EntityId}", entityType, entityId);
            return $"Failed to create {entityType} {entityId}: Error occurred";
        }
    }

    /// <summary>
    /// Extracts specific error from JSON with performance optimizations
    /// </summary>
    private string? ExtractSpecificErrorFromJson(string apiErrorPart)
    {
        try
        {
            // ✅ Performance: Use efficient JSON parsing
            var jsonStart = apiErrorPart.IndexOf('{');
            var jsonEnd = apiErrorPart.LastIndexOf('}');
            
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                return null;
            }

            var jsonContent = apiErrorPart.Substring(jsonStart, jsonEnd - jsonStart + 1);
            
            // ✅ Performance: Use JsonDocument for efficient parsing
            using var jsonDoc = JsonDocument.Parse(jsonContent);
            
            // ✅ Generic: Try multiple JSON error structures
            var errorStructures = new (string Root, string? Nested, string Type)[]
            {
                // BigCommerce API structure with nested errors (most specific)
                ("errors", "errors", "object"),
                // BigCommerce API structure with title
                ("errors", "title", "string"),
                // Generic API structure
                ("error", "message", "string"),
                // Simple error structure
                ("message", null, "string"),
                // Validation errors structure
                ("validationErrors", null, "array")
            };

            foreach (var (root, nested, type) in errorStructures)
            {
                if (jsonDoc.RootElement.TryGetProperty(root, out var rootElement))
                {
                    if (nested != null)
                    {
                        // Try nested structure (e.g., errors.errors)
                        if (rootElement.TryGetProperty(nested, out var nestedElement))
                        {
                            if (type == "string" && nestedElement.ValueKind == JsonValueKind.String)
                            {
                                return nestedElement.GetString();
                            }
                            else if (type == "array" && nestedElement.ValueKind == JsonValueKind.Array)
                            {
                                // Get first array element
                                var firstElement = nestedElement.EnumerateArray().FirstOrDefault();
                                if (firstElement.ValueKind == JsonValueKind.String)
                                {
                                    return firstElement.GetString();
                                }
                            }
                            else if (type == "object" && nestedElement.ValueKind == JsonValueKind.Object)
                            {
                                // For BigCommerce nested errors object, look for the first string value
                                foreach (var property in nestedElement.EnumerateObject())
                                {
                                    if (property.Value.ValueKind == JsonValueKind.String)
                                    {
                                        var value = property.Value.GetString();
                                        if (!string.IsNullOrEmpty(value))
                                        {
                                            return value;
                                        }
                                    }
                                }
                                
                                // Special handling for BigCommerce nested error structure (e.g., "0.parent_id", "0.name")
                                foreach (var property in nestedElement.EnumerateObject())
                                {
                                    if (property.Value.ValueKind == JsonValueKind.String)
                                    {
                                        var value = property.Value.GetString();
                                        if (!string.IsNullOrEmpty(value) && 
                                            (property.Name.Contains("parent_id") || 
                                             property.Name.Contains("name") || 
                                             property.Name.Contains("tree_id")))
                                        {
                                            return value;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Direct property (e.g., message)
                        if (type == "string" && rootElement.ValueKind == JsonValueKind.String)
                        {
                            return rootElement.GetString();
                        }
                    }
                }
            }
            
            // ✅ Generic: Fallback - look for any string property that might be an error message
            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrEmpty(value) && 
                        (property.Name.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                         property.Name.Contains("message", StringComparison.OrdinalIgnoreCase) ||
                         property.Name.Contains("detail", StringComparison.OrdinalIgnoreCase)))
                    {
                        return value;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse JSON error response");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unexpected error parsing JSON error response");
        }

        return null;
    }

    /// <summary>
    /// Extracts status code from various error message formats
    /// </summary>
    private string? ExtractStatusCode(string errorMessage)
    {
        // ✅ Generic: Handle various status code patterns
        var statusPatterns = new[]
        {
            "API request failed with status ",
            "HTTP request failed with status ",
            "Request failed with status ",
            "Status code: ",
            "HTTP ",
            "Status: ",
            "failed with status "
        };

        foreach (var pattern in statusPatterns)
        {
            var patternIndex = errorMessage.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (patternIndex >= 0)
            {
                var startIndex = patternIndex + pattern.Length;
                int endIndex = errorMessage.IndexOf(" for ", startIndex);
                if (endIndex == -1)
                    endIndex = errorMessage.IndexOf(" ", startIndex);

                if (endIndex > startIndex)
                {
                    var statusCode = errorMessage.Substring(startIndex, endIndex - startIndex);
                    if (statusCode.All(char.IsDigit) ||
                        (statusCode.Length == 3 && statusCode.All(char.IsDigit)) ||
                        statusCode.Contains("UnprocessableEntity", StringComparison.OrdinalIgnoreCase) ||
                        statusCode.Contains("BadRequest", StringComparison.OrdinalIgnoreCase) ||
                        statusCode.Contains("NotFound", StringComparison.OrdinalIgnoreCase) ||
                        statusCode.Contains("Conflict", StringComparison.OrdinalIgnoreCase))
                    {
                        return statusCode;
                    }
                }
                else if (startIndex < errorMessage.Length)
                {
                    var statusCode = errorMessage.Substring(startIndex);
                    if (statusCode.All(char.IsDigit) ||
                        (statusCode.Length == 3 && statusCode.All(char.IsDigit)) ||
                        statusCode.Contains("UnprocessableEntity", StringComparison.OrdinalIgnoreCase) ||
                        statusCode.Contains("BadRequest", StringComparison.OrdinalIgnoreCase) ||
                        statusCode.Contains("NotFound", StringComparison.OrdinalIgnoreCase) ||
                        statusCode.Contains("Conflict", StringComparison.OrdinalIgnoreCase))
                    {
                        return statusCode;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Truncates message for performance and readability
    /// </summary>
    private static string TruncateMessage(string message)
    {
        const int maxLength = 100;
        return message.Length > maxLength ? message.Substring(0, maxLength) + "..." : message;
    }

    /// <summary>
    /// Creates a simple hash for caching (performance optimization)
    /// </summary>
    private static string GetErrorHash(string errorMessage)
    {
        // ✅ Performance: Simple hash for caching, not cryptographic
        var hash = 0;
        foreach (var c in errorMessage)
        {
            hash = (hash * 31 + c) % 1000000;
        }
        return hash.ToString();
    }
} 