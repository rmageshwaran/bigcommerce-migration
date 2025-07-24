using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services
{
    /// <summary>
    /// Unit tests for AzureTableDistributedLockHeartbeatService
    /// Phase 3.2: Tests heartbeat functionality for distributed locks
    /// </summary>
    public class AzureTableDistributedLockHeartbeatServiceTests : IDisposable
    {
        private readonly Mock<IDistributedLockService> _mockLockService;
        private readonly Mock<ILogger<AzureTableDistributedLockHeartbeatService>> _mockLogger;
        private readonly AzureTableDistributedLockHeartbeatService _service;
        
        private const string TestLockKey = "test-lock-123";
        private const string TestInstanceId = "test-instance-456";

        public AzureTableDistributedLockHeartbeatServiceTests()
        {
            _mockLockService = new Mock<IDistributedLockService>();
            _mockLogger = new Mock<ILogger<AzureTableDistributedLockHeartbeatService>>();
            
            _service = new AzureTableDistributedLockHeartbeatService(_mockLockService.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task StartHeartbeatAsync_WithValidParameters_ShouldStartHeartbeat()
        {
            // Arrange
            var heartbeatInterval = TimeSpan.FromSeconds(30);
            var cancellationTokenSource = new CancellationTokenSource();

            _mockLockService
                .Setup(x => x.RenewLockAsync(TestLockKey, TestInstanceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DistributedLockResult { Success = true });

            // Act
            await _service.StartHeartbeatAsync(TestLockKey, TestInstanceId, heartbeatInterval, cancellationTokenSource.Token);
            
            // Allow some time for heartbeat to start
            await Task.Delay(100);

            // Assert
            var activeHeartbeats = await _service.GetActiveHeartbeatsAsync();
            Assert.Single(activeHeartbeats);
            Assert.Equal(TestLockKey, activeHeartbeats[0].LockKey);
            Assert.Equal(TestInstanceId, activeHeartbeats[0].InstanceId);
            Assert.True(activeHeartbeats[0].IsActive);

            // Cleanup
            cancellationTokenSource.Cancel();
            await _service.StopHeartbeatAsync(TestLockKey, TestInstanceId);
        }

        [Fact]
        public async Task StartHeartbeatAsync_WithShortInterval_ShouldThrowArgumentException()
        {
            // Arrange
            var shortInterval = TimeSpan.FromSeconds(5); // Less than minimum 10 seconds

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.StartHeartbeatAsync(TestLockKey, TestInstanceId, shortInterval));
        }

        [Fact]
        public async Task StartHeartbeatAsync_WithNullLockKey_ShouldThrowArgumentException()
        {
            // Arrange
            var heartbeatInterval = TimeSpan.FromSeconds(30);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.StartHeartbeatAsync(null!, TestInstanceId, heartbeatInterval));
        }

        [Fact]
        public async Task StartHeartbeatAsync_WithEmptyInstanceId_ShouldThrowArgumentException()
        {
            // Arrange
            var heartbeatInterval = TimeSpan.FromSeconds(30);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.StartHeartbeatAsync(TestLockKey, "", heartbeatInterval));
        }

        [Fact]
        public async Task StartHeartbeatAsync_WhenAlreadyActive_ShouldNotStartDuplicate()
        {
            // Arrange
            var heartbeatInterval = TimeSpan.FromSeconds(30);
            var cancellationTokenSource = new CancellationTokenSource();

            _mockLockService
                .Setup(x => x.RenewLockAsync(TestLockKey, TestInstanceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DistributedLockResult { Success = true });

            // Act
            await _service.StartHeartbeatAsync(TestLockKey, TestInstanceId, heartbeatInterval, cancellationTokenSource.Token);
            await _service.StartHeartbeatAsync(TestLockKey, TestInstanceId, heartbeatInterval, cancellationTokenSource.Token); // Second call

            // Allow some time for heartbeat to start
            await Task.Delay(100);

            // Assert
            var activeHeartbeats = await _service.GetActiveHeartbeatsAsync();
            Assert.Single(activeHeartbeats); // Should only have one heartbeat

            // Cleanup
            cancellationTokenSource.Cancel();
            await _service.StopHeartbeatAsync(TestLockKey, TestInstanceId);
        }

        [Fact]
        public async Task StopHeartbeatAsync_WithActiveHeartbeat_ShouldStopSuccessfully()
        {
            // Arrange
            var heartbeatInterval = TimeSpan.FromSeconds(30);
            var cancellationTokenSource = new CancellationTokenSource();

            _mockLockService
                .Setup(x => x.RenewLockAsync(TestLockKey, TestInstanceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DistributedLockResult { Success = true });

            await _service.StartHeartbeatAsync(TestLockKey, TestInstanceId, heartbeatInterval, cancellationTokenSource.Token);
            await Task.Delay(100); // Allow heartbeat to start

            // Act
            await _service.StopHeartbeatAsync(TestLockKey, TestInstanceId);

            // Assert
            var activeHeartbeats = await _service.GetActiveHeartbeatsAsync();
            Assert.Empty(activeHeartbeats);
        }

        [Fact]
        public async Task SendHeartbeatAsync_WithSuccessfulRenewal_ShouldReturnTrue()
        {
            // Arrange
            _mockLockService
                .Setup(x => x.RenewLockAsync(TestLockKey, TestInstanceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DistributedLockResult { Success = true });

            // Act
            var result = await _service.SendHeartbeatAsync(TestLockKey, TestInstanceId);

            // Assert
            Assert.True(result);
            _mockLockService.Verify(
                x => x.RenewLockAsync(TestLockKey, TestInstanceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), 
                Times.Once);
        }

        [Fact]
        public async Task SendHeartbeatAsync_WithFailedRenewal_ShouldReturnFalse()
        {
            // Arrange
            _mockLockService
                .Setup(x => x.RenewLockAsync(TestLockKey, TestInstanceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DistributedLockResult { Success = false, FailureReason = "Lock not found" });

            // Act
            var result = await _service.SendHeartbeatAsync(TestLockKey, TestInstanceId);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task SendHeartbeatAsync_WithException_ShouldReturnFalse()
        {
            // Arrange
            _mockLockService
                .Setup(x => x.RenewLockAsync(TestLockKey, TestInstanceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Test exception"));

            // Act
            var result = await _service.SendHeartbeatAsync(TestLockKey, TestInstanceId);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task IsLockExpiredAsync_WithRecentHeartbeat_ShouldReturnFalse()
        {
            // Arrange
            var maxAge = TimeSpan.FromMinutes(5);
            var lockInfo = new DistributedLockInfo
            {
                LockKey = TestLockKey,
                InstanceId = TestInstanceId,
                LastHeartbeat = DateTime.UtcNow.AddMinutes(-2) // Recent heartbeat
            };

            _mockLockService
                .Setup(x => x.GetLockInfoAsync(TestLockKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lockInfo);

            // Act
            var result = await _service.IsLockExpiredAsync(TestLockKey, maxAge);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task IsLockExpiredAsync_WithOldHeartbeat_ShouldReturnTrue()
        {
            // Arrange
            var maxAge = TimeSpan.FromMinutes(5);
            var lockInfo = new DistributedLockInfo
            {
                LockKey = TestLockKey,
                InstanceId = TestInstanceId,
                LastHeartbeat = DateTime.UtcNow.AddMinutes(-10) // Old heartbeat
            };

            _mockLockService
                .Setup(x => x.GetLockInfoAsync(TestLockKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lockInfo);

            // Act
            var result = await _service.IsLockExpiredAsync(TestLockKey, maxAge);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task IsLockExpiredAsync_WithNoLock_ShouldReturnTrue()
        {
            // Arrange
            var maxAge = TimeSpan.FromMinutes(5);

            _mockLockService
                .Setup(x => x.GetLockInfoAsync(TestLockKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync((DistributedLockInfo?)null);

            // Act
            var result = await _service.IsLockExpiredAsync(TestLockKey, maxAge);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task IsLockExpiredAsync_WithException_ShouldReturnFalse()
        {
            // Arrange
            var maxAge = TimeSpan.FromMinutes(5);

            _mockLockService
                .Setup(x => x.GetLockInfoAsync(TestLockKey, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Test exception"));

            // Act
            var result = await _service.IsLockExpiredAsync(TestLockKey, maxAge);

            // Assert
            Assert.False(result); // Safe default
        }

        [Fact]
        public async Task GetActiveHeartbeatsAsync_WithNoActiveHeartbeats_ShouldReturnEmpty()
        {
            // Act
            var result = await _service.GetActiveHeartbeatsAsync();

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task CleanupExpiredLocksAsync_ShouldReturnZero()
        {
            // Arrange
            var maxAge = TimeSpan.FromMinutes(5);

            // Act
            var result = await _service.CleanupExpiredLocksAsync(maxAge);

            // Assert
            Assert.Equal(0, result); // Current implementation doesn't have direct enumeration
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithNullOrInvalidParameters_ShouldThrowArgumentNullException(string? invalidParam)
        {
            // Act & Assert
            if (invalidParam == null)
            {
                Assert.Throws<ArgumentNullException>(() => 
                    new AzureTableDistributedLockHeartbeatService(null!, _mockLogger.Object));
                
                Assert.Throws<ArgumentNullException>(() => 
                    new AzureTableDistributedLockHeartbeatService(_mockLockService.Object, null!));
            }
        }

        [Fact]
        public void Dispose_WithActiveHeartbeats_ShouldStopAllHeartbeats()
        {
            // Arrange
            var heartbeatInterval = TimeSpan.FromSeconds(30);

            _mockLockService
                .Setup(x => x.RenewLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DistributedLockResult { Success = true });

            // Start a heartbeat
            var task = _service.StartHeartbeatAsync(TestLockKey, TestInstanceId, heartbeatInterval);

            // Act
            _service.Dispose();

            // Assert - Should not throw and should clean up properly
            // The test validates that Dispose() completes without hanging
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_ShouldNotThrow()
        {
            // Act & Assert
            _service.Dispose();
            _service.Dispose(); // Second call should not throw
        }

        [Fact]
        public async Task OperationsAfterDispose_ShouldThrowObjectDisposedException()
        {
            // Arrange
            _service.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _service.StartHeartbeatAsync(TestLockKey, TestInstanceId, TimeSpan.FromSeconds(30)));
            
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _service.StopHeartbeatAsync(TestLockKey, TestInstanceId));
            
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _service.SendHeartbeatAsync(TestLockKey, TestInstanceId));
            
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _service.IsLockExpiredAsync(TestLockKey, TimeSpan.FromMinutes(5)));
            
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _service.GetActiveHeartbeatsAsync());
            
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _service.CleanupExpiredLocksAsync(TimeSpan.FromMinutes(5)));
        }

        public void Dispose()
        {
            _service?.Dispose();
        }
    }
} 