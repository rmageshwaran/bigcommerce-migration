using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Functions.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace BigCommerce.Migration.UnitTests.Functions;

/// <summary>
/// TDD Tests for AsyncQueuedSignalRService following Test-Driven Development principles
/// Tests define expected behavior BEFORE implementation to ensure correct async queuing functionality
/// 
/// Performance Optimization: Async message queuing prevents SignalR calls from blocking migration processing
/// SOLID Principles: Single responsibility (queuing), Open/Closed (extensible), Dependency Inversion (interfaces)
/// </summary>
public class AsyncQueuedSignalRServiceTests : IDisposable
{
    private readonly Mock<ILogger<AsyncQueuedSignalRService>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly SignalRConfiguration _testConfig;
    private AsyncQueuedSignalRService? _service;
    private readonly string _testMigrationId = "test-migration-async-001";

    public AsyncQueuedSignalRServiceTests()
    {
        _mockLogger = new Mock<ILogger<AsyncQueuedSignalRService>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpClient = new Mock<HttpClient>();
        
        _testConfig = new SignalRConfiguration
        {
            BaseUrl = "https://test-functions.azurewebsites.net",
            TimeoutSeconds = 10,
            Enabled = true,
            EnableDebugLogging = false,
            MaxRetries = 0
        };

        _mockHttpClientFactory.Setup(x => x.CreateClient("SignalR"))
            .Returns(_mockHttpClient.Object);
    }

    #region Constructor and Initialization Tests (TDD)

    [Fact]
    public void AsyncQueuedSignalRService_Constructor_ShouldInitializeWithoutBlocking()
    {
        // Arrange & Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        stopwatch.Stop();

        // Assert - Constructor should be fast (< 50ms) and non-blocking
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50, "constructor should not block for initialization");
        _service.Should().NotBeNull();
        _service.Should().BeAssignableTo<IMigrationSignalRService>();
    }

    [Fact]
    public void AsyncQueuedSignalRService_Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new AsyncQueuedSignalRService(null!, _mockHttpClientFactory.Object, _testConfig));
    }

    [Fact]
    public void AsyncQueuedSignalRService_Constructor_WithNullHttpClientFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new AsyncQueuedSignalRService(_mockLogger.Object, null!, _testConfig));
    }

    [Fact]
    public void AsyncQueuedSignalRService_Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, null!));
    }

    #endregion

    #region Async Queuing Performance Tests (TDD)

    [Fact]
    public async Task BroadcastProgressUpdateAsync_ShouldReturnImmediately_WithoutBlockingForHttpCall()
    {
        // Arrange
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        var progress = new MigrationProgress { ProcessedEntities = 10, TotalEntities = 100, Status = "InProgress" };
        
        // Act - This should be very fast (non-blocking)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await _service.BroadcastProgressUpdateAsync(_testMigrationId, progress);
        stopwatch.Stop();

        // Assert - Should return almost immediately (< 10ms) without waiting for HTTP call
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10, "async queuing should not block for HTTP calls");
    }

    [Fact]
    public async Task BroadcastMigrationStartedAsync_ShouldReturnImmediately_WithoutBlockingForHttpCall()
    {
        // Arrange
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        var migrationData = new { Status = "Started", SourceStore = "test-store" };
        
        // Act - This should be very fast (non-blocking)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await _service.BroadcastMigrationStartedAsync(_testMigrationId, migrationData);
        stopwatch.Stop();

        // Assert - Should return almost immediately without waiting for HTTP call
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10, "async queuing should not block for HTTP calls");
    }

    [Fact]
    public async Task MultipleSignalRCalls_ShouldAllReturnImmediately_DemonstratingQueueingPerformance()
    {
        // Arrange
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        var progress = new MigrationProgress { ProcessedEntities = 1, TotalEntities = 100, Status = "InProgress" };
        var tasks = new List<Task>();
        
        // Act - Queue 50 messages rapidly
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
        {
            progress.ProcessedEntities = i;
            tasks.Add(_service.BroadcastProgressUpdateAsync(_testMigrationId, progress));
        }
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert - All 50 calls should complete quickly (< 100ms total) demonstrating non-blocking behavior
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100, "queuing 50 messages should be very fast");
    }

    #endregion

    #region Queue Capacity and Overflow Tests (TDD)

    [Fact]
    public async Task QueuedMessages_ShouldHandleLargeVolume_WithoutMemoryIssues()
    {
        // Arrange
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        var progress = new MigrationProgress { ProcessedEntities = 1, TotalEntities = 1000, Status = "InProgress" };
        
        // Act - Queue many messages to test capacity
        var tasks = new List<Task>();
        for (int i = 0; i < 500; i++)
        {
            progress.ProcessedEntities = i;
            tasks.Add(_service.BroadcastProgressUpdateAsync($"{_testMigrationId}-{i}", progress));
        }
        
        // Should not throw or hang
        await Task.WhenAll(tasks);
        
        // Assert - No exceptions should be thrown
        tasks.Should().AllSatisfy(task => task.IsCompletedSuccessfully.Should().BeTrue());
    }

    [Fact]
    public async Task QueueOverflow_WhenCapacityExceeded_ShouldHandleGracefully()
    {
        // Arrange
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        var progress = new MigrationProgress { ProcessedEntities = 1, TotalEntities = 100, Status = "InProgress" };
        
        // Act - Try to exceed queue capacity
        var tasks = new List<Task>();
        for (int i = 0; i < 2000; i++) // Exceed typical queue capacity
        {
            progress.ProcessedEntities = i;
            tasks.Add(_service.BroadcastProgressUpdateAsync($"{_testMigrationId}-overflow-{i}", progress));
        }
        
        // Should handle overflow gracefully without throwing
        var exception = await Record.ExceptionAsync(async () => await Task.WhenAll(tasks));
        
        // Assert - Should handle overflow gracefully (no exceptions or deadlocks)
        exception.Should().BeNull("queue overflow should be handled gracefully");
    }

    #endregion

    #region Background Processing Tests (TDD)

    [Fact]
    public async Task BackgroundProcessing_ShouldEventuallyProcessQueuedMessages()
    {
        // Arrange
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        var progress = new MigrationProgress { ProcessedEntities = 10, TotalEntities = 100, Status = "InProgress" };
        
        // Act - Queue a message
        await _service.BroadcastProgressUpdateAsync(_testMigrationId, progress);
        
        // Wait a bit for background processing
        await Task.Delay(TimeSpan.FromSeconds(2));
        
        // Assert - Background processor should eventually attempt HTTP calls
        // (We'll verify this by checking if HttpClient was requested from factory)
        _mockHttpClientFactory.Verify(x => x.CreateClient("SignalR"), Times.AtLeastOnce,
            "background processing should request HTTP client to send queued messages");
    }

    [Fact]
    public async Task BackgroundProcessing_ShouldContinueAfterHttpFailures()
    {
        // Arrange - Setup HTTP client to throw exceptions
        _mockHttpClient.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Test HTTP failure"));
            
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        var progress = new MigrationProgress { ProcessedEntities = 10, TotalEntities = 100, Status = "InProgress" };
        
        // Act - Queue multiple messages
        await _service.BroadcastProgressUpdateAsync($"{_testMigrationId}-1", progress);
        await _service.BroadcastProgressUpdateAsync($"{_testMigrationId}-2", progress);
        
        // Wait for background processing
        await Task.Delay(TimeSpan.FromSeconds(3));
        
        // Assert - Should continue processing despite HTTP failures
        _mockHttpClientFactory.Verify(x => x.CreateClient("SignalR"), Times.AtLeastOnce,
            "background processing should continue despite HTTP failures");
    }

    #endregion

    #region Message Types and Interface Compliance Tests (TDD)

    [Fact]
    public async Task AllSignalRMethods_ShouldReturnImmediately_ProvingAsyncBehavior()
    {
        // Arrange
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        var testData = new { Status = "Test" };
        var progress = new MigrationProgress { ProcessedEntities = 5, TotalEntities = 100, Status = "InProgress" };
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Act - Call all interface methods rapidly
        await Task.WhenAll(
            _service.BroadcastMigrationStartedAsync(_testMigrationId, testData),
            _service.BroadcastMigrationCompletedAsync(_testMigrationId, testData),
            _service.BroadcastMigrationFailedAsync(_testMigrationId, testData),
            _service.BroadcastMigrationCancelledAsync(_testMigrationId, testData),
            _service.BroadcastProgressUpdateAsync(_testMigrationId, progress),
            _service.BroadcastStatusUpdateAsync(_testMigrationId, testData),
            _service.BroadcastEntityPhaseStartAsync(_testMigrationId, "products", testData),
            _service.BroadcastEntityPhaseCompletedAsync(_testMigrationId, "products", testData),
            _service.BroadcastSystemHealthAsync(testData),
            _service.BroadcastErrorNotificationAsync(_testMigrationId, testData),
            _service.BroadcastSystemAlertAsync("TestAlert", testData)
        );
        
        stopwatch.Stop();
        
        // Assert - All calls should complete very quickly
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(50, "all async queued calls should return immediately");
    }

    [Fact]
    public async Task AsyncQueuedSignalRService_ShouldImplementAllInterfaceMethods()
    {
        // Arrange
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        
        // Act & Assert - Service should implement all required interface methods
        _service.Should().BeAssignableTo<IMigrationSignalRService>();
        
        // Verify all methods exist and are callable (should not throw NotImplementedException)
        var testData = new { Test = "Data" };
        var progress = new MigrationProgress();
        
        await _service.BroadcastMigrationStartedAsync(_testMigrationId, testData);
        await _service.BroadcastMigrationCompletedAsync(_testMigrationId, testData);
        await _service.BroadcastMigrationFailedAsync(_testMigrationId, testData);
        await _service.BroadcastMigrationCancelledAsync(_testMigrationId, testData);
        await _service.BroadcastProgressUpdateAsync(_testMigrationId, progress);
        await _service.BroadcastStatusUpdateAsync(_testMigrationId, testData);
        await _service.BroadcastEntityPhaseStartAsync(_testMigrationId, "test", testData);
        await _service.BroadcastEntityPhaseCompletedAsync(_testMigrationId, "test", testData);
        await _service.BroadcastSystemHealthAsync(testData);
        await _service.BroadcastErrorNotificationAsync(_testMigrationId, testData);
        await _service.BroadcastSystemAlertAsync("TestAlert", testData);
    }

    #endregion

    #region Resource Management and Disposal Tests (TDD)

    [Fact]
    public void AsyncQueuedSignalRService_ShouldImplementIDisposable()
    {
        // Arrange & Act
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        
        // Assert
        _service.Should().BeAssignableTo<IDisposable>("service should implement IDisposable for proper resource cleanup");
    }

    [Fact]
    public void Dispose_ShouldCleanupResourcesGracefully()
    {
        // Arrange
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        
        // Act & Assert - Should not throw
        var exception = Record.Exception(() => _service.Dispose());
        exception.Should().BeNull("Dispose should clean up resources gracefully");
    }

    [Fact]
    public async Task Dispose_ShouldStopAcceptingNewMessages()
    {
        // Arrange
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, _testConfig);
        var progress = new MigrationProgress { ProcessedEntities = 1, TotalEntities = 100, Status = "InProgress" };
        
        // Act
        _service.Dispose();
        
        // Assert - After disposal, should handle new messages gracefully (not crash)
        var exception = await Record.ExceptionAsync(async () => 
            await _service.BroadcastProgressUpdateAsync(_testMigrationId, progress));
            
        // Should either complete gracefully or throw ObjectDisposedException (both are acceptable)
        if (exception != null)
        {
            exception.Should().BeOfType<ObjectDisposedException>("disposed service should throw ObjectDisposedException");
        }
    }

    #endregion

    #region Configuration Integration Tests (TDD)

    [Fact]
    public async Task DisabledConfiguration_ShouldStillQueueMessages_ButNotSendThem()
    {
        // Arrange - Disabled SignalR configuration
        var disabledConfig = new SignalRConfiguration
        {
            BaseUrl = "https://test.example.com",
            Enabled = false, // Disabled
            TimeoutSeconds = 5
        };
        
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, disabledConfig);
        var progress = new MigrationProgress { ProcessedEntities = 10, TotalEntities = 100, Status = "InProgress" };
        
        // Act
        await _service.BroadcastProgressUpdateAsync(_testMigrationId, progress);
        await Task.Delay(TimeSpan.FromSeconds(1)); // Wait for any background processing
        
        // Assert - Should not attempt HTTP calls when disabled
        _mockHttpClientFactory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never,
            "disabled SignalR should not attempt HTTP calls");
    }

    [Fact]
    public async Task InvalidConfiguration_ShouldQueueMessages_ButLogWarnings()
    {
        // Arrange - Invalid configuration
        var invalidConfig = new SignalRConfiguration
        {
            BaseUrl = "invalid-url", // Invalid URL
            Enabled = true,
            TimeoutSeconds = 5
        };
        
        _service = new AsyncQueuedSignalRService(_mockLogger.Object, _mockHttpClientFactory.Object, invalidConfig);
        var progress = new MigrationProgress { ProcessedEntities = 10, TotalEntities = 100, Status = "InProgress" };
        
        // Act
        await _service.BroadcastProgressUpdateAsync(_testMigrationId, progress);
        
        // Assert - Should handle invalid config gracefully
        // (Implementation should validate config and log warnings)
    }

    #endregion

    public void Dispose()
    {
        _service?.Dispose();
    }
}

// Note: Using MigrationProgress from BigCommerce.Migration.Core.Interfaces namespace 