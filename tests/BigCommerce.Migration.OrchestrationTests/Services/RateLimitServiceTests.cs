using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Services;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Services;

/// <summary>
/// Tests for RateLimitService with comprehensive cancellation support
/// </summary>
public class RateLimitServiceTests
{
    private readonly Mock<ILogger<RateLimitService>> _mockLogger;
    private readonly RateLimitService _service;
    private readonly string _testStoreId = "test_store_123";

    public RateLimitServiceTests()
    {
        _mockLogger = new Mock<ILogger<RateLimitService>>();
        _service = new RateLimitService(_mockLogger.Object);
    }

    [Fact]
    public async Task CheckAndWaitAsync_WithValidStore_AllowsRequest()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        await _service.CheckAndWaitAsync(_testStoreId, cancellationToken);

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public async Task CheckAndWaitAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.CheckAndWaitAsync(_testStoreId, cancellationToken));
    }

    [Fact]
    public async Task CheckAndWaitAsync_WithNullStoreId_ThrowsArgumentException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.CheckAndWaitAsync(null!, cancellationToken));
    }

    [Fact]
    public async Task CheckAndWaitAsync_WithEmptyStoreId_ThrowsArgumentException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.CheckAndWaitAsync(string.Empty, cancellationToken));
    }

    [Fact]
    public async Task CheckRateLimitAsync_WithValidStore_ReturnsCanProceed()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _service.CheckRateLimitAsync(_testStoreId, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.CanProceed);
        Assert.Equal(0, result.DelayMs);
        Assert.Equal(12, result.RequestLimit);
    }

    [Fact]
    public async Task CheckRateLimitAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.CheckRateLimitAsync(_testStoreId, cancellationToken));
    }

    [Fact]
    public async Task CheckRateLimitAsync_WithRateLimitHit_ReturnsDelay()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Simulate hitting rate limit by making 12 requests quickly
        for (int i = 0; i < 12; i++)
        {
            await _service.RecordApiCallAsync(_testStoreId, "/test", 100, true, cancellationToken);
        }

        // Act
        var result = await _service.CheckRateLimitAsync(_testStoreId, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.CanProceed);
        Assert.True(result.DelayMs > 0);
        Assert.Equal(12, result.CurrentRequestCount);
    }

    [Fact]
    public async Task RecordApiCallAsync_WithValidParameters_RecordsSuccessfully()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        await _service.RecordApiCallAsync(_testStoreId, "/products", 250.5, true, cancellationToken);

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public async Task RecordApiCallAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.RecordApiCallAsync(_testStoreId, "/products", 250.5, true, cancellationToken));
    }

    [Fact]
    public async Task RecordApiCallAsync_WithNullStoreId_ThrowsArgumentException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.RecordApiCallAsync(null!, "/products", 250.5, true, cancellationToken));
    }

    [Fact]
    public async Task RecordApiCallAsync_WithNullEndpoint_ThrowsArgumentException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _service.RecordApiCallAsync(_testStoreId, null!, 250.5, true, cancellationToken));
    }

    [Fact]
    public async Task GetRateLimitStatusAsync_WithValidStore_ReturnsStatus()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        var status = await _service.GetRateLimitStatusAsync(_testStoreId, cancellationToken);

        // Assert
        Assert.NotNull(status);
        Assert.Equal(_testStoreId, status.StoreId);
        Assert.Equal(12, status.RequestsRemaining);
        Assert.False(status.IsLimited);
    }

    [Fact]
    public async Task GetRateLimitStatusAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.GetRateLimitStatusAsync(_testStoreId, cancellationToken));
    }

    [Fact]
    public async Task CalculateDelayAsync_WithNoRateLimit_ReturnsZero()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        var delay = await _service.CalculateDelayAsync(_testStoreId, cancellationToken);

        // Assert
        Assert.Equal(0, delay);
    }

    [Fact]
    public async Task CalculateDelayAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.CalculateDelayAsync(_testStoreId, cancellationToken));
    }

    [Fact]
    public async Task CanMakeRequestAsync_WithValidStore_ReturnsTrue()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        var canMakeRequest = await _service.CanMakeRequestAsync(_testStoreId, cancellationToken);

        // Assert
        Assert.True(canMakeRequest);
    }

    [Fact]
    public async Task CanMakeRequestAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _service.CanMakeRequestAsync(_testStoreId, cancellationToken));
    }

    [Fact]
    public async Task RateLimitService_MultipleRequests_RespectsRateLimit()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act - Make 12 requests (the limit)
        for (int i = 0; i < 12; i++)
        {
            await _service.RecordApiCallAsync(_testStoreId, $"/endpoint_{i}", 100, true, cancellationToken);
        }

        // Check rate limit
        var result = await _service.CheckRateLimitAsync(_testStoreId, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.CanProceed);
        Assert.Equal(12, result.CurrentRequestCount);
        Assert.Equal(12, result.RequestLimit);
    }

    [Fact]
    public async Task CheckAndWaitAsync_WithNoRateLimit_CompletesImmediately()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        await _service.CheckAndWaitAsync(_testStoreId, cancellationToken);

        // Assert - Should complete without exception
        Assert.True(true);
    }

    [Fact]
    public async Task CheckAndWaitAsync_WithCancellationDuringWait_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        
        // Hit rate limit
        for (int i = 0; i < 12; i++)
        {
            await _service.RecordApiCallAsync(_testStoreId, $"/endpoint_{i}", 100, true, cts.Token);
        }

        // Cancel after short delay
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => 
            _service.CheckAndWaitAsync(_testStoreId, cts.Token));
    }
} 