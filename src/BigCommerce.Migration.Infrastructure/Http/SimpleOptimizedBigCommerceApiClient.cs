using System.Net.Http;
using System.Text;
using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Http;

/// <summary>
/// Simplified Optimized BigCommerce API client with connection pooling
/// TDD GREEN Phase: Implements basic connection pooling to make ConnectionPoolingTests pass
/// </summary>
public class SimpleOptimizedBigCommerceApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SimpleOptimizedBigCommerceApiClient> _logger;
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the SimpleOptimizedBigCommerceApiClient with connection pooling
    /// </summary>
    /// <param name="logger">Logger instance for HTTP operations</param>
    public SimpleOptimizedBigCommerceApiClient(ILogger<SimpleOptimizedBigCommerceApiClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Create optimized HttpClient with connection pooling - this is the key optimization
        _httpClient = CreateOptimizedHttpClient();
    }

    /// <summary>
    /// Creates an optimized HttpClient with connection pooling for BigCommerce API
    /// This implements the performance optimizations tested in ConnectionPoolingTests
    /// </summary>
    private static HttpClient CreateOptimizedHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            // Connection pooling settings optimized for BigCommerce API
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),    // Reuse connections for 15 minutes
            MaxConnectionsPerServer = 10,                          // Allow up to 10 concurrent connections per host
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5), // Close idle connections after 5 minutes
            
            // Performance optimizations
            EnableMultipleHttp2Connections = false,                // Use HTTP/1.1 for simpler connection tracking
            ConnectTimeout = TimeSpan.FromSeconds(30),             // 30-second connection timeout
            ResponseDrainTimeout = TimeSpan.FromSeconds(10),       // 10-second response drain timeout
        };

        var httpClient = new HttpClient(handler);
        
        // Configure HTTP client timeouts
        httpClient.Timeout = TimeSpan.FromMinutes(2); // 2-minute overall timeout
        
        // Set standard headers for BigCommerce API
        httpClient.DefaultRequestHeaders.Add("User-Agent", "BigCommerce-Migration-Tool/1.0");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        
        return httpClient;
    }

    /// <summary>
    /// Simulates a BigCommerce API call with optimized connection pooling
    /// Used for testing connection reuse and efficiency
    /// </summary>
    public async Task<bool> MakeTestApiCallAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Test API call failed to {Url}", url);
            return false;
        }
    }

    /// <summary>
    /// Gets the underlying HttpClient for advanced testing scenarios
    /// This allows the ConnectionPoolingTests to verify connection pooling behavior
    /// </summary>
    public HttpClient GetHttpClient() => _httpClient;

    #region IDisposable Implementation

    /// <summary>
    /// Disposes the HTTP client and releases resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the HTTP client resources
    /// </summary>
    /// <param name="disposing">Whether to dispose managed resources</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
    }

    #endregion
} 