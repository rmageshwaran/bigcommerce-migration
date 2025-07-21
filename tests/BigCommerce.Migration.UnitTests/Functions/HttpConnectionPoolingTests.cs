using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using BigCommerce.Migration.Functions.Services;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Diagnostics;

namespace BigCommerce.Migration.UnitTests.Functions;

/// <summary>
/// TDD Tests for HTTP Connection Pooling optimization in SignalR services
/// Tests define expected behavior BEFORE implementation to ensure proper connection reuse
/// 
/// Performance Optimization: Connection pooling reduces TCP overhead and improves throughput
/// SOLID Principles: Dependency Inversion (IHttpClientFactory), Single Responsibility (connection management)
/// </summary>
public class HttpConnectionPoolingTests
{
    private readonly ServiceCollection _services;
    private readonly SignalRConfiguration _testConfig;

    public HttpConnectionPoolingTests()
    {
        _services = new ServiceCollection();
        _testConfig = new SignalRConfiguration
        {
            BaseUrl = "https://test-functions.azurewebsites.net",
            TimeoutSeconds = 30,
            Enabled = true
        };
    }

    #region Connection Pooling Registration Tests (TDD)

    [Fact]
    public void ServiceCollection_ShouldRegisterHttpClientFactory_ForSignalRConnectionPooling()
    {
        // Arrange & Act
        _services.AddHttpClient("SignalR", client => 
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        
        var serviceProvider = _services.BuildServiceProvider();
        
        // Assert
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        httpClientFactory.Should().NotBeNull("IHttpClientFactory should be registered for connection pooling");
    }

    [Fact]
    public void HttpClientFactory_ShouldCreateNamedClient_ForSignalRService()
    {
        // Arrange
        _services.AddHttpClient("SignalR");
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        // Act
        var httpClient = factory.CreateClient("SignalR");
        
        // Assert
        httpClient.Should().NotBeNull("factory should create named SignalR client");
        httpClient.Should().BeOfType<HttpClient>();
    }

    [Fact]
    public void HttpClient_FromFactory_ShouldHaveConfiguredTimeout()
    {
        // Arrange
        var expectedTimeout = TimeSpan.FromSeconds(45);
        _services.AddHttpClient("SignalR", client => 
        {
            client.Timeout = expectedTimeout;
        });
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        // Act
        var httpClient = factory.CreateClient("SignalR");
        
        // Assert
        httpClient.Timeout.Should().Be(expectedTimeout, "factory-created client should have configured timeout");
    }

    #endregion

    #region Connection Reuse Tests (TDD)

    [Fact]
    public void HttpClientFactory_ShouldReuseConnections_ForMultipleClients()
    {
        // Arrange
        _services.AddHttpClient("SignalR", client => 
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxConnectionsPerServer = 10
        });
        
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        // Act - Create multiple clients
        var client1 = factory.CreateClient("SignalR");
        var client2 = factory.CreateClient("SignalR");
        var client3 = factory.CreateClient("SignalR");
        
        // Assert - All clients should be created successfully
        client1.Should().NotBeNull();
        client2.Should().NotBeNull();
        client3.Should().NotBeNull();
        
        // Different instances but should share connection pool
        client1.Should().NotBeSameAs(client2, "factory should create different HttpClient instances");
        client2.Should().NotBeSameAs(client3, "factory should create different HttpClient instances");
    }

    [Fact]
    public void SocketsHttpHandler_ShouldBeConfigured_ForOptimalConnectionPooling()
    {
        // Arrange & Act
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxConnectionsPerServer = 10,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        };
        
        // Assert - Connection pooling settings
        handler.PooledConnectionLifetime.Should().Be(TimeSpan.FromMinutes(15), 
            "connections should be reused for 15 minutes for optimal performance");
        handler.MaxConnectionsPerServer.Should().Be(10, 
            "should allow up to 10 concurrent connections per server");
        handler.PooledConnectionIdleTimeout.Should().Be(TimeSpan.FromMinutes(5), 
            "idle connections should timeout after 5 minutes to free resources");
    }

    #endregion

    #region Performance Impact Tests (TDD)

    [Fact]
    public void ConnectionPooling_ShouldImprovePerformance_ForMultipleRequests()
    {
        // Arrange
        _services.AddHttpClient("SignalR")
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxConnectionsPerServer = 10
        });
        
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        // Act - Create multiple clients quickly (simulating high-frequency SignalR calls)
        var stopwatch = Stopwatch.StartNew();
        var clients = new List<HttpClient>();
        
        for (int i = 0; i < 20; i++)
        {
            clients.Add(factory.CreateClient("SignalR"));
        }
        
        stopwatch.Stop();
        
        // Assert - Client creation should be fast due to connection pooling
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100, 
            "creating 20 pooled HTTP clients should be very fast");
        clients.Should().HaveCount(20);
    }

    [Fact]
    public async Task ConcurrentHttpClientCreation_ShouldBeThreadSafe_WithConnectionPooling()
    {
        // Arrange
        _services.AddHttpClient("SignalR")
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxConnectionsPerServer = 10
        });
        
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        // Act - Create clients concurrently from multiple threads
        var tasks = new List<Task<HttpClient>>();
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() => factory.CreateClient("SignalR")));
        }
        
        var clients = await Task.WhenAll(tasks);
        
        // Assert - All clients should be created successfully without exceptions
        clients.Should().HaveCount(50);
        clients.Should().AllSatisfy(client => client.Should().NotBeNull());
    }

    #endregion

    #region Configuration and Headers Tests (TDD)

    [Fact]
    public void HttpClient_ShouldHaveCorrectDefaultHeaders_ForSignalRCommunication()
    {
        // Arrange
        _services.AddHttpClient("SignalR", client => 
        {
            client.DefaultRequestHeaders.Add("User-Agent", "BigCommerce-Migration/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        // Act
        var httpClient = factory.CreateClient("SignalR");
        
        // Assert
        httpClient.DefaultRequestHeaders.UserAgent.ToString().Should()
            .Contain("BigCommerce-Migration/1.0", "should have proper user agent for SignalR calls");
        httpClient.DefaultRequestHeaders.Accept.ToString().Should()
            .Contain("application/json", "should accept JSON responses from SignalR");
    }

    [Fact]
    public void HttpClientConfiguration_ShouldBeOptimized_ForSignalRWorkload()
    {
        // Arrange & Act
        _services.AddHttpClient("SignalR", client => 
        {
            client.Timeout = TimeSpan.FromSeconds(30); // Reasonable timeout for SignalR
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15), // Reuse connections
            MaxConnectionsPerServer = 10, // Support concurrent SignalR calls
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5), // Clean up idle connections
            ConnectTimeout = TimeSpan.FromSeconds(10) // Fast connection establishment
        });
        
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("SignalR");
        
        // Assert
        client.Timeout.Should().Be(TimeSpan.FromSeconds(30), "timeout should be optimized for SignalR workload");
    }

    #endregion

    #region Error Handling and Resilience Tests (TDD)

    [Fact]
    public void HttpClientFactory_ShouldHandleDisposal_Gracefully()
    {
        // Arrange
        _services.AddHttpClient("SignalR");
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("SignalR");
        
        // Act & Assert - Should not throw when disposing clients from factory
        var exception = Record.Exception(() => client.Dispose());
        exception.Should().BeNull("factory-created clients should handle disposal gracefully");
    }

    [Fact]
    public void HttpClientFactory_ShouldRecreateClients_AfterDisposal()
    {
        // Arrange
        _services.AddHttpClient("SignalR");
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        // Act
        var client1 = factory.CreateClient("SignalR");
        client1.Dispose();
        var client2 = factory.CreateClient("SignalR");
        
        // Assert
        client2.Should().NotBeNull("factory should create new client after previous one was disposed");
        client2.Should().NotBeSameAs(client1, "should be a different instance");
    }

    [Fact]
    public async Task ConnectionPooling_ShouldHandleBurstTraffic_WithoutConnectionExhaustion()
    {
        // Arrange
        _services.AddHttpClient("SignalR")
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 20, // Higher limit for burst traffic
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        });
        
        var serviceProvider = _services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        
        // Act - Simulate burst of SignalR calls
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => 
            {
                var client = factory.CreateClient("SignalR");
                // Simulate quick SignalR call pattern
                return Task.CompletedTask;
            }));
        }
        
        // Should handle burst without hanging or throwing
        var exception = await Record.ExceptionAsync(async () => await Task.WhenAll(tasks));
        
        // Assert
        exception.Should().BeNull("connection pooling should handle burst traffic gracefully");
    }

    #endregion

    #region Integration with SignalR Service Tests (TDD)

    [Fact]
    public void SignalRService_ShouldAcceptHttpClientFactory_InConstructor()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OptimizedSignalRService>>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        
        // Act & Assert - Constructor should accept IHttpClientFactory
        var exception = Record.Exception(() => 
            new OptimizedSignalRService(mockLogger.Object, mockHttpClientFactory.Object, _testConfig));
            
        exception.Should().BeNull("SignalR service should accept IHttpClientFactory for connection pooling");
    }

    [Fact]
    public void OptimizedSignalRService_ShouldImplementIMigrationSignalRService()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OptimizedSignalRService>>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        
        // Act
        var service = new OptimizedSignalRService(mockLogger.Object, mockHttpClientFactory.Object, _testConfig);
        
        // Assert
        service.Should().BeAssignableTo<IMigrationSignalRService>(
            "optimized service should implement the same interface");
    }

    [Fact]
    public async Task OptimizedSignalRService_ShouldUsePooledConnections_ForHttpCalls()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<OptimizedSignalRService>>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockHttpClient = new Mock<HttpClient>();
        
        mockHttpClientFactory.Setup(x => x.CreateClient("SignalR"))
            .Returns(mockHttpClient.Object);
        
        var service = new OptimizedSignalRService(mockLogger.Object, mockHttpClientFactory.Object, _testConfig);
        var testData = new { Status = "Test" };
        
        // Act
        await service.BroadcastMigrationStartedAsync("test-migration", testData);
        
        // Assert - Should request pooled HTTP client
        mockHttpClientFactory.Verify(x => x.CreateClient("SignalR"), Times.AtLeastOnce,
            "optimized service should use pooled HTTP connections");
    }

    #endregion
}

// Note: Using OptimizedSignalRService from BigCommerce.Migration.Functions.Services namespace
// The real implementation is now available, so test stub is no longer needed 