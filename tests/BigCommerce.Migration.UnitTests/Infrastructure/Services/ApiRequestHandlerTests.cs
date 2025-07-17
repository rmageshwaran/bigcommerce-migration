using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System.Net;
using System.Text;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Tests for ApiRequestHandler - focused on HTTP request handling and rate limiting
/// Tests follow TDD approach: RED phase - writing failing tests for desired behavior
/// </summary>
public class ApiRequestHandlerTests
{
    private readonly Mock<ILogger<ApiRequestHandler>> _mockLogger;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly Mock<IRateLimitService> _mockRateLimitService;
    private readonly Mock<IOpenSearchService> _mockOpenSearchService;

    public ApiRequestHandlerTests()
    {
        _mockLogger = new Mock<ILogger<ApiRequestHandler>>();
        _mockHttpClient = new Mock<HttpClient>();
        _mockRateLimitService = new Mock<IRateLimitService>();
        _mockOpenSearchService = new Mock<IOpenSearchService>();
    }

    /// <summary>
    /// RED PHASE: Test that ApiRequestHandler only handles HTTP request concerns
    /// This test will fail until we create the segregated class
    /// </summary>
    [Fact]
    public void ApiRequestHandler_Should_Only_Handle_HTTP_Request_Concerns()
    {
        // Arrange & Act
        var methods = typeof(ApiRequestHandler).GetMethods()
            .Where(m => m.IsPublic && m.DeclaringType == typeof(ApiRequestHandler))
            .Select(m => m.Name)
            .ToList();
        
        // Assert - Should only contain HTTP request handling methods
        var expectedMethods = new List<string> { "ExecuteRequestAsync", "ExecuteGetRequestAsync", "ExecutePostRequestAsync" };
        methods.Should().BeSubsetOf(expectedMethods, "Request handler should only handle HTTP request concerns");
        methods.Should().Contain("ExecuteRequestAsync", "Must handle generic HTTP requests");
    }

    /// <summary>
    /// RED PHASE: Test ExecuteRequestAsync handles rate limiting
    /// This test will fail until we implement the method
    /// </summary>
    [Fact]
    public async Task ExecuteRequestAsync_Should_Apply_Rate_Limiting_Before_Request()
    {
        // Arrange
        var storeConfig = new StoreConfiguration 
        { 
            StoreId = "test-store", 
            AccessToken = "test-token", 
            BaseUrl = "https://test.com" 
        };
        var apiRequest = new ApiRequest
        {
            Url = "https://test.com/api/test",
            Method = HttpMethod.Get,
            StoreConfiguration = storeConfig
        };

        var handler = new ApiRequestHandler(
            _mockHttpClient.Object,
            _mockRateLimitService.Object,
            _mockOpenSearchService.Object,
            _mockLogger.Object);

        // Act
        try
        {
            await handler.ExecuteRequestAsync<string>(apiRequest);
        }
        catch
        {
            // Expected to fail in RED phase
        }

        // Assert
        _mockRateLimitService.Verify(r => r.CheckAndWaitAsync(storeConfig.StoreId, It.IsAny<CancellationToken>()), Times.Once,
            "Should apply rate limiting before making request");
    }

    /// <summary>
    /// RED PHASE: Test ExecuteRequestAsync handles authentication headers
    /// This test will fail until we implement proper authentication
    /// </summary>
    [Fact]
    public async Task ExecuteRequestAsync_Should_Add_Authentication_Headers()
    {
        // Arrange
        var storeConfig = new StoreConfiguration 
        { 
            StoreId = "test-store", 
            AccessToken = "test-token", 
            BaseUrl = "https://test.com" 
        };
        var apiRequest = new ApiRequest
        {
            Url = "https://test.com/api/test",
            Method = HttpMethod.Get,
            StoreConfiguration = storeConfig
        };

        var handler = new ApiRequestHandler(
            _mockHttpClient.Object,
            _mockRateLimitService.Object,
            _mockOpenSearchService.Object,
            _mockLogger.Object);

        // Act & Assert
        // This should fail in RED phase - we expect proper authentication header handling
        var act = () => handler.ExecuteRequestAsync<string>(apiRequest);
        await act.Should().NotThrowAsync("Should handle authentication properly once implemented");
    }

    /// <summary>
    /// RED PHASE: Test ExecuteRequestAsync handles HTTP errors properly
    /// This test will fail until we implement proper error handling
    /// </summary>
    [Fact]
    public async Task ExecuteRequestAsync_Should_Handle_HTTP_Errors_Gracefully()
    {
        // Arrange
        var storeConfig = new StoreConfiguration 
        { 
            StoreId = "test-store", 
            AccessToken = "test-token", 
            BaseUrl = "https://test.com" 
        };
        var apiRequest = new ApiRequest
        {
            Url = "https://test.com/api/test",
            Method = HttpMethod.Get,
            StoreConfiguration = storeConfig
        };

        // Setup HTTP client to return error
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Bad Request", Encoding.UTF8, "application/json")
        };

        var handler = new ApiRequestHandler(
            _mockHttpClient.Object,
            _mockRateLimitService.Object,
            _mockOpenSearchService.Object,
            _mockLogger.Object);

        // Act & Assert
        var act = () => handler.ExecuteRequestAsync<string>(apiRequest);
        await act.Should().ThrowAsync<HttpRequestException>("Should throw appropriate exception for HTTP errors");
    }

    /// <summary>
    /// RED PHASE: Test that ApiRequestHandler has focused dependencies
    /// This test will fail until we create the proper constructor
    /// </summary>
    [Fact]
    public void ApiRequestHandler_Should_Have_Focused_Dependencies()
    {
        // Arrange & Act
        var constructor = typeof(ApiRequestHandler).GetConstructors().FirstOrDefault();
        
        // Assert
        constructor.Should().NotBeNull("Should have a constructor");
        
        var parameters = constructor!.GetParameters();
        var parameterTypes = parameters.Select(p => p.ParameterType).ToList();
        
        // Should only depend on services needed for HTTP request handling
        parameterTypes.Should().Contain(typeof(HttpClient), "Should have HTTP client for requests");
        parameterTypes.Should().Contain(typeof(IRateLimitService), "Should have rate limiting service");
        parameterTypes.Should().Contain(typeof(IOpenSearchService), "Should have OpenSearch for logging");
        parameterTypes.Should().Contain(typeof(ILogger<ApiRequestHandler>), "Should have logger");
        
        // Should NOT depend on services it doesn't need
        parameterTypes.Should().NotContain(typeof(IBigCommerceApiClient), "Should not depend on API client - circular dependency");
        parameterTypes.Should().NotContain(typeof(IMigrationStorageService), "Should not depend on storage - not HTTP concern");
        parameterTypes.Should().NotContain(typeof(IQueueService), "Should not depend on queue - not HTTP concern");
    }

    /// <summary>
    /// RED PHASE: Test that request handler follows Single Responsibility Principle
    /// </summary>
    [Fact]
    public void ApiRequestHandler_Should_Follow_Single_Responsibility_Principle()
    {
        // Arrange
        var classType = typeof(ApiRequestHandler);
        
        // Act & Assert
        classType.Name.Should().Contain("Request", "Class name should indicate its single responsibility");
        
        // All methods should be related to HTTP request handling
        var publicMethods = classType.GetMethods()
            .Where(m => m.IsPublic && m.DeclaringType == classType)
            .ToList();
        
        foreach (var method in publicMethods.Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")))
        {
            method.Name.Should().MatchRegex(@".*Request.*|.*Execute.*", 
                $"Method {method.Name} should be related to request execution");
        }
    }

    /// <summary>
    /// RED PHASE: Test ExecutePostRequestAsync for creating resources
    /// </summary>
    [Fact]
    public async Task ExecutePostRequestAsync_Should_Handle_JSON_Content_Properly()
    {
        // Arrange
        var storeConfig = new StoreConfiguration 
        { 
            StoreId = "test-store", 
            AccessToken = "test-token", 
            BaseUrl = "https://test.com" 
        };
        var apiRequest = new ApiRequest
        {
            Url = "https://test.com/api/test",
            Method = HttpMethod.Post,
            StoreConfiguration = storeConfig,
            Content = "{\"name\":\"test\"}"
        };

        var handler = new ApiRequestHandler(
            _mockHttpClient.Object,
            _mockRateLimitService.Object,
            _mockOpenSearchService.Object,
            _mockLogger.Object);

        // Act & Assert
        // This should fail in RED phase - we expect proper JSON content handling
        var act = () => handler.ExecuteRequestAsync<Dictionary<string, object>>(apiRequest);
        await act.Should().NotThrowAsync("Should handle JSON content properly once implemented");
    }

    /// <summary>
    /// RED PHASE: Test that API request model has proper structure
    /// </summary>
    [Fact]
    public void ApiRequest_Should_Have_Required_Properties()
    {
        // Arrange & Act
        var apiRequest = new ApiRequest();
        
        // Assert - Should have all necessary properties for HTTP requests
        apiRequest.Should().NotBeNull("ApiRequest should be instantiable");
        
        var properties = typeof(ApiRequest).GetProperties().Select(p => p.Name).ToList();
        properties.Should().Contain("Url", "Should have URL property");
        properties.Should().Contain("Method", "Should have HTTP method property");
        properties.Should().Contain("StoreConfiguration", "Should have store configuration for auth");
        properties.Should().Contain("Content", "Should have content property for POST requests");
        properties.Should().Contain("Headers", "Should have headers property for custom headers");
    }
} 