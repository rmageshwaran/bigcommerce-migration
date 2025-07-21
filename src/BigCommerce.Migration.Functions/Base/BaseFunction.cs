using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace BigCommerce.Migration.Functions.Base;

/// <summary>
/// Base class for Azure Function classes providing common functionality
/// Implements Single Responsibility Principle by extracting shared concerns:
/// - HTTP response creation (success/error)
/// - JSON serialization/deserialization
/// - Request body reading
/// - Consistent error handling patterns
/// </summary>
public abstract class BaseFunction
{
    /// <summary>
    /// Logger instance for this function class
    /// </summary>
    protected readonly ILogger Logger;

    /// <summary>
    /// Default JSON serialization options used across all functions
    /// </summary>
    private static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of the BaseFunction class
    /// </summary>
    /// <param name="logger">Logger instance for this function class</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null</exception>
    protected BaseFunction(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the default JSON serialization options
    /// </summary>
    /// <returns>JsonSerializerOptions with consistent settings</returns>
    protected JsonSerializerOptions GetDefaultJsonOptions()
    {
        return DefaultJsonSerializerOptions;
    }

    /// <summary>
    /// Creates a standardized error response with consistent format
    /// Replaces duplicate CreateErrorResponse methods across function classes
    /// </summary>
    /// <param name="request">HTTP request data</param>
    /// <param name="statusCode">HTTP status code for the error</param>
    /// <param name="message">Error message</param>
    /// <param name="contextId">Context identifier (migration ID, request ID, etc.)</param>
    /// <returns>HTTP response with standardized error format</returns>
    protected async Task<HttpResponseData> CreateErrorResponseAsync(
        HttpRequestData request, 
        HttpStatusCode statusCode, 
        string message, 
        string contextId)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        
        var errorResponse = new
        {
            error = message,
            contextId = contextId,
            timestamp = DateTime.UtcNow
        };

        await response.WriteStringAsync(SerializeObject(errorResponse));
        return response;
    }

    /// <summary>
    /// Creates a standardized success response with data
    /// Provides consistent response format across all functions
    /// </summary>
    /// <typeparam name="T">Type of data to serialize</typeparam>
    /// <param name="request">HTTP request data</param>
    /// <param name="data">Data to include in response</param>
    /// <param name="statusCode">HTTP status code (defaults to OK)</param>
    /// <returns>HTTP response with serialized data</returns>
    protected async Task<HttpResponseData> CreateSuccessResponseAsync<T>(
        HttpRequestData request, 
        T data, 
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        
        await response.WriteStringAsync(SerializeObject(data));
        return response;
    }

    /// <summary>
    /// Reads the complete request body as a string
    /// Provides consistent request body reading across functions
    /// </summary>
    /// <param name="request">HTTP request data</param>
    /// <returns>Request body as string</returns>
    protected async Task<string> ReadRequestBodyAsync(HttpRequestData request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Deserializes HTTP request body to specified type
    /// Provides consistent JSON deserialization with error handling
    /// </summary>
    /// <typeparam name="T">Type to deserialize to</typeparam>
    /// <param name="request">HTTP request data</param>
    /// <returns>Deserialized object</returns>
    /// <exception cref="ArgumentException">Thrown when request body is empty</exception>
    /// <exception cref="JsonException">Thrown when JSON is invalid</exception>
    protected async Task<T> DeserializeRequestAsync<T>(HttpRequestData request)
    {
        var requestBody = await ReadRequestBodyAsync(request);
        
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            throw new ArgumentException("Request body cannot be empty", nameof(request));
        }

        return JsonSerializer.Deserialize<T>(requestBody, GetDefaultJsonOptions()) 
               ?? throw new JsonException("Failed to deserialize request body");
    }

    /// <summary>
    /// Serializes an object to JSON string using default options
    /// Provides consistent JSON serialization across functions
    /// </summary>
    /// <typeparam name="T">Type of object to serialize</typeparam>
    /// <param name="obj">Object to serialize</param>
    /// <returns>JSON string representation</returns>
    protected string SerializeObject<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, GetDefaultJsonOptions());
    }

    /// <summary>
    /// Generates a unique context identifier for tracking requests
    /// Provides consistent ID generation across functions
    /// </summary>
    /// <returns>Unique identifier string</returns>
    protected string GenerateContextId()
    {
        return Guid.NewGuid().ToString();
    }
} 