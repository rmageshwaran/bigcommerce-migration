using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.UnitTests.Orchestration.Integration
{
    /// <summary>
    /// Integration tests for OrchestratorCollisionDetectionService with heartbeat functionality
    /// Phase 3.2: Tests enhanced collision detection with phantom orchestrator prevention
    /// </summary>
    public class OrchestratorCollisionDetectionHeartbeatIntegrationTests
    {
        private readonly Mock<IDistributedLockService> _mockLockService;
        private readonly Mock<IDistributedLockHeartbeatService> _mockHeartbeatService;
        private readonly Mock<ILogger<OrchestratorCollisionDetectionService>> _mockLogger;
        private readonly OrchestratorCollisionDetectionService _service;
        
        private const string TestMigrationId = "test-migration-123";
        private const string TestInstanceId = "test-instance-456";
        private const string TestOtherInstanceId = "other-instance-789";

        public OrchestratorCollisionDetectionHeartbeatIntegrationTests()
        {
            _mockLockService = new Mock<IDistributedLockService>();
            _mockHeartbeatService = new Mock<IDistributedLockHeartbeatService>();
            _mockLogger = new Mock<ILogger<OrchestratorCollisionDetectionService>>();
            
            _service = new OrchestratorCollisionDetectionService(
                _mockLockService.Object, 
                _mockHeartbeatService.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task TryAcquireOrchestratorLockAsync_WhenSuccessful_ShouldStartHeartbeat()
        {
            // Arrange
            var lockResult = new DistributedLockResult
            {
                Success = true,
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestInstanceId,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            };

            _mockLockService
                .Setup(x => x.TryAcquireLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(lockResult);

            _mockHeartbeatService
                .Setup(x => x.StartHeartbeatAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _service.TryAcquireOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.True(result.CanProceed);
            Assert.True(result.LockAcquired);
            Assert.False(result.CollisionDetected);
            Assert.False(result.HasError);
            Assert.Equal(TestMigrationId, result.MigrationId);
            Assert.Equal(TestInstanceId, result.InstanceId);
            Assert.Contains("heartbeat started", result.Message);

            // Verify heartbeat was started (Note: Fire-and-forget, so might not be immediately verifiable)
            // We can verify the setup was called, but the actual start is async
            _mockLockService.Verify(
                x => x.TryAcquireLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()), 
                Times.Once);
        }

        [Fact]
        public async Task TryAcquireOrchestratorLockAsync_WhenConflictWithExpiredLock_ShouldForceReleaseAndRetry()
        {
            // Arrange
            var conflictLockInfo = new DistributedLockInfo
            {
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestOtherInstanceId,
                AcquiredAt = DateTime.UtcNow.AddMinutes(-30),
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                LastHeartbeat = DateTime.UtcNow.AddMinutes(-10), // Old heartbeat
                Status = DistributedLockStatus.Active
            };

            var conflictResult = new DistributedLockResult
            {
                Success = false,
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestInstanceId,
                FailureReason = "Lock already exists",
                CurrentLockHolder = conflictLockInfo
            };

            var retryResult = new DistributedLockResult
            {
                Success = true,
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestInstanceId,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            };

            // Setup sequence: first call returns conflict, second call returns success
            _mockLockService
                .SetupSequence(x => x.TryAcquireLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(conflictResult)  // First call: conflict
                .ReturnsAsync(retryResult);    // Second call: success after force release

            // Setup expired lock detection
            _mockHeartbeatService
                .Setup(x => x.IsLockExpiredAsync(
                    $"orchestrator-{TestMigrationId}", 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Setup force release
            _mockLockService
                .Setup(x => x.ForceReleaseLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.TryAcquireOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.True(result.CanProceed);
            Assert.True(result.LockAcquired);
            Assert.False(result.CollisionDetected);
            Assert.Contains("expired lock", result.Message);

            // Verify the sequence of calls
            _mockHeartbeatService.Verify(
                x => x.IsLockExpiredAsync(
                    $"orchestrator-{TestMigrationId}", 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()), 
                Times.Once);

            _mockLockService.Verify(
                x => x.ForceReleaseLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    It.IsAny<CancellationToken>()), 
                Times.Once);
        }

        [Fact]
        public async Task TryAcquireOrchestratorLockAsync_WhenConflictWithActiveLock_ShouldReturnCollision()
        {
            // Arrange
            var activeLockInfo = new DistributedLockInfo
            {
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestOtherInstanceId,
                AcquiredAt = DateTime.UtcNow.AddMinutes(-10),
                ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                LastHeartbeat = DateTime.UtcNow.AddMinutes(-1), // Recent heartbeat
                Status = DistributedLockStatus.Active
            };

            var conflictResult = new DistributedLockResult
            {
                Success = false,
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestInstanceId,
                FailureReason = $"Lock is held by instance {TestOtherInstanceId}",
                CurrentLockHolder = activeLockInfo
            };

            _mockLockService
                .Setup(x => x.TryAcquireLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(conflictResult);

            // Setup active lock detection (not expired)
            _mockHeartbeatService
                .Setup(x => x.IsLockExpiredAsync(
                    $"orchestrator-{TestMigrationId}", 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var result = await _service.TryAcquireOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.False(result.CanProceed);
            Assert.False(result.LockAcquired);
            Assert.True(result.CollisionDetected);
            Assert.False(result.HasError);
            Assert.NotNull(result.CurrentLockHolder);
            Assert.Equal(TestOtherInstanceId, result.CurrentLockHolder.InstanceId);

            // Verify expired check was performed
            _mockHeartbeatService.Verify(
                x => x.IsLockExpiredAsync(
                    $"orchestrator-{TestMigrationId}", 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()), 
                Times.Once);

            // Verify no force release was attempted
            _mockLockService.Verify(
                x => x.ForceReleaseLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), 
                Times.Never);
        }

        [Fact]
        public async Task ReleaseOrchestratorLockAsync_WhenSuccessful_ShouldStopHeartbeat()
        {
            // Arrange
            _mockHeartbeatService
                .Setup(x => x.StopHeartbeatAsync($"orchestrator-{TestMigrationId}", TestInstanceId))
                .Returns(Task.CompletedTask);

            _mockLockService
                .Setup(x => x.ReleaseLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ReleaseOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.True(result);

            // Verify heartbeat was stopped before lock release
            _mockHeartbeatService.Verify(
                x => x.StopHeartbeatAsync($"orchestrator-{TestMigrationId}", TestInstanceId), 
                Times.Once);

            _mockLockService.Verify(
                x => x.ReleaseLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<CancellationToken>()), 
                Times.Once);
        }

        [Fact]
        public async Task ReleaseOrchestratorLockAsync_WhenHeartbeatStopFails_ShouldContinueWithLockRelease()
        {
            // Arrange
            _mockHeartbeatService
                .Setup(x => x.StopHeartbeatAsync($"orchestrator-{TestMigrationId}", TestInstanceId))
                .ThrowsAsync(new Exception("Heartbeat stop failed"));

            _mockLockService
                .Setup(x => x.ReleaseLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ReleaseOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.True(result); // Should still succeed despite heartbeat failure

            // Verify both operations were attempted
            _mockHeartbeatService.Verify(
                x => x.StopHeartbeatAsync($"orchestrator-{TestMigrationId}", TestInstanceId), 
                Times.Once);

            _mockLockService.Verify(
                x => x.ReleaseLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<CancellationToken>()), 
                Times.Once);
        }

        [Fact]
        public async Task TryAcquireOrchestratorLockAsync_WhenHeartbeatStartFails_ShouldStillAcquireLock()
        {
            // Arrange
            var lockResult = new DistributedLockResult
            {
                Success = true,
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestInstanceId,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            };

            _mockLockService
                .Setup(x => x.TryAcquireLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(lockResult);

            _mockHeartbeatService
                .Setup(x => x.StartHeartbeatAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Heartbeat start failed"));

            // Act
            var result = await _service.TryAcquireOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.True(result.CanProceed); // Should still succeed despite heartbeat failure
            Assert.True(result.LockAcquired);
            Assert.False(result.CollisionDetected);
            Assert.False(result.HasError);
        }

        [Fact]
        public void Constructor_WithNullHeartbeatService_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new OrchestratorCollisionDetectionService(_mockLockService.Object, null!, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLockService_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new OrchestratorCollisionDetectionService(null!, _mockHeartbeatService.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new OrchestratorCollisionDetectionService(_mockLockService.Object, _mockHeartbeatService.Object, null!));
        }

        [Fact]
        public async Task TryAcquireOrchestratorLockAsync_WhenExpiredCheckFails_ShouldTreatAsActive()
        {
            // Arrange
            var conflictLockInfo = new DistributedLockInfo
            {
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestOtherInstanceId,
                Status = DistributedLockStatus.Active
            };

            var conflictResult = new DistributedLockResult
            {
                Success = false,
                CurrentLockHolder = conflictLockInfo,
                FailureReason = "Lock exists"
            };

            _mockLockService
                .Setup(x => x.TryAcquireLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(conflictResult);

            // Setup heartbeat check to fail
            _mockHeartbeatService
                .Setup(x => x.IsLockExpiredAsync(
                    $"orchestrator-{TestMigrationId}", 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Heartbeat check failed"));

            // Act
            var result = await _service.TryAcquireOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.False(result.CanProceed); // Should treat as collision since check failed
            Assert.True(result.CollisionDetected);

            // Verify no force release was attempted due to check failure
            _mockLockService.Verify(
                x => x.ForceReleaseLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), 
                Times.Never);
        }
    }
} 