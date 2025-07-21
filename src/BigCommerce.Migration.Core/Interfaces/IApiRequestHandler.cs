using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for handling HTTP API requests with rate limiting and authentication
/// Follows Single Responsibility Principle - handles only HTTP request concerns
/// </summary>
public interface IApiRequestHandler
{
    /// <summary>
    /// Executes an HTTP request with rate limiting, authentication, and error handling
    /// </summary>
    /// <typeparam name="T">Expected response type</typeparam>
    /// <param name="request">API request configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deserialized response of type T</returns>
    Task<T> ExecuteRequestAsync<T>(ApiRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an HTTP request and returns raw string response
    /// </summary>
    /// <param name="request">API request configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Raw response content as string</returns>
    Task<string> ExecuteRequestAsync(ApiRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a GET request with rate limiting and authentication
    /// </summary>
    /// <typeparam name="T">Expected response type</typeparam>
    /// <param name="url">Request URL</param>
    /// <param name="storeConfig">Store configuration for authentication</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deserialized response of type T</returns>
    Task<T> ExecuteGetRequestAsync<T>(string url, StoreConfiguration storeConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a POST request with JSON content, rate limiting, and authentication
    /// </summary>
    /// <typeparam name="T">Expected response type</typeparam>
    /// <param name="url">Request URL</param>
    /// <param name="content">JSON content to send</param>
    /// <param name="storeConfig">Store configuration for authentication</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deserialized response of type T</returns>
    Task<T> ExecutePostRequestAsync<T>(string url, string content, StoreConfiguration storeConfig, CancellationToken cancellationToken = default);
} 