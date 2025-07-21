using System.Net;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Abstraction for HTTP client operations to enforce Dependency Inversion Principle
/// Provides testable interface without direct dependency on System.Net.Http.HttpClient
/// </summary>
public interface IHttpClientWrapper
{
    /// <summary>
    /// Gets or sets the timeout for HTTP requests
    /// </summary>
    TimeSpan Timeout { get; set; }
    
    /// <summary>
    /// Gets the default request headers
    /// </summary>
    IHttpRequestHeaders DefaultRequestHeaders { get; }
    
    /// <summary>
    /// Sends an HTTP GET request
    /// </summary>
    /// <param name="requestUri">Request URI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response message</returns>
    Task<IHttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends an HTTP GET request
    /// </summary>
    /// <param name="requestUri">Request URI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response message</returns>
    Task<IHttpResponseMessage> GetAsync(Uri requestUri, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends an HTTP POST request with JSON content
    /// </summary>
    /// <param name="requestUri">Request URI</param>
    /// <param name="content">HTTP content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response message</returns>
    Task<IHttpResponseMessage> PostAsync(string requestUri, IHttpContent content, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends an HTTP POST request with JSON content
    /// </summary>
    /// <param name="requestUri">Request URI</param>
    /// <param name="content">HTTP content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response message</returns>
    Task<IHttpResponseMessage> PostAsync(Uri requestUri, IHttpContent content, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends an HTTP PUT request
    /// </summary>
    /// <param name="requestUri">Request URI</param>
    /// <param name="content">HTTP content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response message</returns>
    Task<IHttpResponseMessage> PutAsync(string requestUri, IHttpContent content, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends an HTTP DELETE request
    /// </summary>
    /// <param name="requestUri">Request URI</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response message</returns>
    Task<IHttpResponseMessage> DeleteAsync(string requestUri, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends an HTTP request
    /// </summary>
    /// <param name="request">HTTP request message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response message</returns>
    Task<IHttpResponseMessage> SendAsync(IHttpRequestMessage request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstraction for HTTP request headers
/// </summary>
public interface IHttpRequestHeaders
{
    /// <summary>
    /// Adds a header value
    /// </summary>
    /// <param name="name">Header name</param>
    /// <param name="value">Header value</param>
    void Add(string name, string value);
    
    /// <summary>
    /// Removes a header
    /// </summary>
    /// <param name="name">Header name</param>
    /// <returns>True if removed, false otherwise</returns>
    bool Remove(string name);
    
    /// <summary>
    /// Checks if a header exists
    /// </summary>
    /// <param name="name">Header name</param>
    /// <returns>True if exists, false otherwise</returns>
    bool Contains(string name);
}

/// <summary>
/// Abstraction for HTTP content
/// </summary>
public interface IHttpContent : IDisposable
{
    /// <summary>
    /// Gets the content headers
    /// </summary>
    IHttpContentHeaders Headers { get; }
    
    /// <summary>
    /// Reads the content as a string
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Content as string</returns>
    Task<string> ReadAsStringAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Reads the content as a byte array
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Content as byte array</returns>
    Task<byte[]> ReadAsByteArrayAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstraction for HTTP content headers
/// </summary>
public interface IHttpContentHeaders
{
    /// <summary>
    /// Gets or sets the Content-Type header
    /// </summary>
    string ContentType { get; set; }
    
    /// <summary>
    /// Gets or sets the Content-Length header
    /// </summary>
    long? ContentLength { get; set; }
}

/// <summary>
/// Abstraction for HTTP response messages
/// </summary>
public interface IHttpResponseMessage : IDisposable
{
    /// <summary>
    /// Gets the status code
    /// </summary>
    HttpStatusCode StatusCode { get; }
    
    /// <summary>
    /// Gets a value indicating whether the request was successful
    /// </summary>
    bool IsSuccessStatusCode { get; }
    
    /// <summary>
    /// Gets the reason phrase
    /// </summary>
    string ReasonPhrase { get; }
    
    /// <summary>
    /// Gets the response headers
    /// </summary>
    IHttpResponseHeaders Headers { get; }
    
    /// <summary>
    /// Gets the response content
    /// </summary>
    IHttpContent Content { get; }
    
    /// <summary>
    /// Throws an exception if the response was not successful
    /// </summary>
    void EnsureSuccessStatusCode();
}

/// <summary>
/// Abstraction for HTTP response headers
/// </summary>
public interface IHttpResponseHeaders
{
    /// <summary>
    /// Gets a header value
    /// </summary>
    /// <param name="name">Header name</param>
    /// <returns>Header values</returns>
    IEnumerable<string> GetValues(string name);
    
    /// <summary>
    /// Checks if a header exists
    /// </summary>
    /// <param name="name">Header name</param>
    /// <returns>True if exists, false otherwise</returns>
    bool Contains(string name);
}

/// <summary>
/// Abstraction for HTTP request messages
/// </summary>
public interface IHttpRequestMessage : IDisposable
{
    /// <summary>
    /// Gets or sets the HTTP method
    /// </summary>
    HttpMethod Method { get; set; }
    
    /// <summary>
    /// Gets or sets the request URI
    /// </summary>
    Uri RequestUri { get; set; }
    
    /// <summary>
    /// Gets the request headers
    /// </summary>
    IHttpRequestHeaders Headers { get; }
    
    /// <summary>
    /// Gets or sets the request content
    /// </summary>
    IHttpContent Content { get; set; }
} 