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
    /// Integration tests for orchestrator collision detection logic
    /// Phase 3.1: Tests collision detection service and result creation
    /// </summary>
    public class OrchestratorCollisionDetectionIntegrationTests
    {
        private readonly Mock<IDistributedLockService> _mockLockService;
        private readonly Mock<IDistributedLockHeartbeatService> _mockHeartbeatService;
        private readonly Mock<ILogger<OrchestratorCollisionDetectionService>> _mockLogger;
        private readonly OrchestratorCollisionDetectionService _service;
        
        private const string TestMigrationId = "test-migration-123";
        private const string TestInstanceId = "test-instance-456";
        private const string TestOtherInstanceId = "other-instance-789";

        public OrchestratorCollisionDetectionIntegrationTests()
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
        public async Task TryAcquireOrchestratorLockAsync_WhenLockSuccessful_ShouldReturnCanProceed()
        {
            // Arrange
            var expectedExpiration = DateTime.UtcNow.AddMinutes(30);
            var successfulLockResult = new DistributedLockResult
            {
                Success = true,
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestInstanceId,
                AcquiredAt = DateTime.UtcNow,
                ExpiresAt = expectedExpiration
            };

            _mockLockService
                .Setup(x => x.TryAcquireLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(successfulLockResult);

            // Act
            var result = await _service.TryAcquireOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.True(result.CanProceed);
            Assert.True(result.LockAcquired);
            Assert.False(result.CollisionDetected);
            Assert.False(result.HasError);
            Assert.Equal(TestMigrationId, result.MigrationId);
            Assert.Equal(TestInstanceId, result.InstanceId);
            Assert.Equal($"orchestrator-{TestMigrationId}", result.LockKey);
            Assert.Equal(expectedExpiration, result.ExpiresAt);
            Assert.Contains("Orchestrator lock acquired successfully", result.Message); // Phase 3.2: Updated to support heartbeat message
        }

        [Fact]
        public async Task TryAcquireOrchestratorLockAsync_WhenLockConflict_ShouldReturnCollisionDetected()
        {
            // Arrange
            var existingLockInfo = new DistributedLockInfo
            {
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestOtherInstanceId,
                AcquiredAt = DateTime.UtcNow.AddMinutes(-10),
                ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                Status = DistributedLockStatus.Active,
                MigrationId = TestMigrationId
            };

            var conflictLockResult = new DistributedLockResult
            {
                Success = false,
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestInstanceId,
                FailureReason = $"Lock is held by instance {TestOtherInstanceId}",
                CurrentLockHolder = existingLockInfo
            };

            _mockLockService
                .Setup(x => x.TryAcquireLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(conflictLockResult);

            // Act
            var result = await _service.TryAcquireOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.False(result.CanProceed);
            Assert.False(result.LockAcquired);
            Assert.True(result.CollisionDetected);
            Assert.False(result.HasError);
            Assert.Equal(TestMigrationId, result.MigrationId);
            Assert.Equal(TestInstanceId, result.InstanceId);
            Assert.NotNull(result.CurrentLockHolder);
            Assert.Equal(TestOtherInstanceId, result.CurrentLockHolder.InstanceId);
            Assert.Contains("Lock is held by instance", result.Message);
        }

        [Fact]
        public async Task TryAcquireOrchestratorLockAsync_WhenServiceException_ShouldReturnHasError()
        {
            // Arrange
            var exception = new InvalidOperationException("Storage service unavailable");
            
            _mockLockService
                .Setup(x => x.TryAcquireLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            // Act
            var result = await _service.TryAcquireOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.False(result.CanProceed);
            Assert.False(result.LockAcquired);
            Assert.False(result.CollisionDetected);
            Assert.True(result.HasError);
            Assert.Equal(TestMigrationId, result.MigrationId);
            Assert.Equal(TestInstanceId, result.InstanceId);
            Assert.Contains("Error during collision detection", result.Message);
            Assert.Contains("Storage service unavailable", result.Message);
        }

        [Fact]
        public async Task RenewOrchestratorLockAsync_WhenRenewalSuccessful_ShouldReturnTrue()
        {
            // Arrange
            var successfulRenewalResult = new DistributedLockResult
            {
                Success = true,
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestInstanceId,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            };

            _mockLockService
                .Setup(x => x.RenewLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(successfulRenewalResult);

            // Act
            var result = await _service.RenewOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task RenewOrchestratorLockAsync_WhenRenewalFails_ShouldReturnFalse()
        {
            // Arrange
            var failedRenewalResult = new DistributedLockResult
            {
                Success = false,
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestInstanceId,
                FailureReason = "Lock not found or owned by different instance"
            };

            _mockLockService
                .Setup(x => x.RenewLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<TimeSpan>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(failedRenewalResult);

            // Act
            var result = await _service.RenewOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ReleaseOrchestratorLockAsync_WhenReleaseSuccessful_ShouldReturnTrue()
        {
            // Arrange
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
        }

        [Fact]
        public async Task ReleaseOrchestratorLockAsync_WhenReleaseFails_ShouldReturnFalse()
        {
            // Arrange
            _mockLockService
                .Setup(x => x.ReleaseLockAsync(
                    $"orchestrator-{TestMigrationId}", 
                    TestInstanceId, 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var result = await _service.ReleaseOrchestratorLockAsync(TestMigrationId, TestInstanceId);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task GetOrchestratorLockInfoAsync_WhenLockExists_ShouldReturnLockInfo()
        {
            // Arrange
            var expectedLockInfo = new DistributedLockInfo
            {
                LockKey = $"orchestrator-{TestMigrationId}",
                InstanceId = TestInstanceId,
                AcquiredAt = DateTime.UtcNow.AddMinutes(-10),
                ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                Status = DistributedLockStatus.Active,
                MigrationId = TestMigrationId,
                RenewalCount = 2
            };

            _mockLockService
                .Setup(x => x.GetLockInfoAsync(
                    $"orchestrator-{TestMigrationId}", 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedLockInfo);

            // Act
            var result = await _service.GetOrchestratorLockInfoAsync(TestMigrationId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal($"orchestrator-{TestMigrationId}", result.LockKey);
            Assert.Equal(TestInstanceId, result.InstanceId);
            Assert.Equal(DistributedLockStatus.Active, result.Status);
            Assert.Equal(TestMigrationId, result.MigrationId);
            Assert.Equal(2, result.RenewalCount);
        }

        [Fact]
        public async Task GetOrchestratorLockInfoAsync_WhenLockDoesNotExist_ShouldReturnNull()
        {
            // Arrange
            _mockLockService
                .Setup(x => x.GetLockInfoAsync(
                    $"orchestrator-{TestMigrationId}", 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((DistributedLockInfo?)null);

            // Act
            var result = await _service.GetOrchestratorLockInfoAsync(TestMigrationId);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void CreateCollisionCancellationResult_ShouldCreateDeterministicResult()
        {
            // Arrange
            var currentUtcTime = DateTime.UtcNow;
            var cancellationReason = "Another orchestrator instance is already running";

            // Act
            var result = _service.CreateCollisionCancellationResult(
                TestMigrationId, TestInstanceId, cancellationReason, currentUtcTime);

            // Assert
            Assert.NotNull(result);
            
            // Use reflection to access properties of the anonymous type returned by CancelledResultFactory
            var resultType = result.GetType();
            
            Assert.Equal(TestMigrationId, resultType.GetProperty("MigrationId")?.GetValue(result) as string);
            Assert.Equal("Cancelled", resultType.GetProperty("Status")?.GetValue(result) as string);
            Assert.True((bool)(resultType.GetProperty("IsCancelled")?.GetValue(result) ?? false));
            
            var message = resultType.GetProperty("Message")?.GetValue(result) as string;
            Assert.Contains("Orchestrator collision detected", message ?? "");
            Assert.Contains(cancellationReason, message ?? "");
        }

        [Fact]
        public void Constructor_WhenDistributedLockServiceIsNull_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new OrchestratorCollisionDetectionService(null!, _mockHeartbeatService.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WhenLoggerIsNull_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new OrchestratorCollisionDetectionService(_mockLockService.Object, _mockHeartbeatService.Object, null!));
        }
    }
} 