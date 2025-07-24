using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Services;

namespace BigCommerce.Migration.UnitTests.Orchestration.Services
{
    /// <summary>
    /// Unit tests for OrchestratorCleanupService
    /// Phase 3.3: Tests cleanup service functionality for orphaned lock management
    /// </summary>
    public class OrchestratorCleanupServiceTests
    {
        private readonly Mock<IDistributedLockService> _mockLockService;
        private readonly Mock<IDistributedLockHeartbeatService> _mockHeartbeatService;
        private readonly Mock<ILogger<OrchestratorCleanupService>> _mockLogger;
        private readonly OrchestratorCleanupService _service;
        
        private const string TestMigrationId1 = "test-migration-123";
        private const string TestMigrationId2 = "test-migration-456";
        private const string TestInstanceId1 = "test-instance-789";
        private const string TestInstanceId2 = "test-instance-012";

        public OrchestratorCleanupServiceTests()
        {
            _mockLockService = new Mock<IDistributedLockService>();
            _mockHeartbeatService = new Mock<IDistributedLockHeartbeatService>();
            _mockLogger = new Mock<ILogger<OrchestratorCleanupService>>();
            
            _service = new OrchestratorCleanupService(
                _mockLockService.Object,
                _mockHeartbeatService.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task CleanupOrphanedLocksAsync_WithOrphanedLocks_ShouldCleanupSuccessfully()
        {
            // Arrange
            var maxHeartbeatAge = TimeSpan.FromMinutes(10);
            var oldTime = DateTime.UtcNow.AddMinutes(-15); // Older than max age
            var recentTime = DateTime.UtcNow.AddMinutes(-2); // Recent

            var allLocks = new List<DistributedLockInfo>
            {
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId1}",
                    InstanceId = TestInstanceId1,
                    MigrationId = TestMigrationId1,
                    LastHeartbeat = oldTime, // Orphaned
                    Status = DistributedLockStatus.Active
                },
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId2}",
                    InstanceId = TestInstanceId2,
                    MigrationId = TestMigrationId2,
                    LastHeartbeat = recentTime, // Not orphaned
                    Status = DistributedLockStatus.Active
                }
            };

            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(allLocks);

            _mockLockService
                .Setup(x => x.ForceReleaseLockAsync($"orchestrator-{TestMigrationId1}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.CleanupOrphanedLocksAsync(maxHeartbeatAge);

            // Assert
            Assert.Equal(1, result.OrphanedLocksFound);
            Assert.Equal(1, result.LocksCleanedUp);
            Assert.Equal(0, result.FailedCleanups);
            Assert.Single(result.CleanedUpMigrationIds);
            Assert.Contains(TestMigrationId1, result.CleanedUpMigrationIds);
            Assert.Empty(result.Failures);

            _mockLockService.Verify(
                x => x.ForceReleaseLockAsync($"orchestrator-{TestMigrationId1}", It.IsAny<CancellationToken>()), 
                Times.Once);
            _mockLockService.Verify(
                x => x.ForceReleaseLockAsync($"orchestrator-{TestMigrationId2}", It.IsAny<CancellationToken>()), 
                Times.Never);
        }

        [Fact]
        public async Task CleanupOrphanedLocksAsync_WithFailedCleanup_ShouldRecordFailure()
        {
            // Arrange
            var maxHeartbeatAge = TimeSpan.FromMinutes(10);
            var oldTime = DateTime.UtcNow.AddMinutes(-15);

            var allLocks = new List<DistributedLockInfo>
            {
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId1}",
                    InstanceId = TestInstanceId1,
                    MigrationId = TestMigrationId1,
                    LastHeartbeat = oldTime,
                    Status = DistributedLockStatus.Active
                }
            };

            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(allLocks);

            _mockLockService
                .Setup(x => x.ForceReleaseLockAsync($"orchestrator-{TestMigrationId1}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false); // Cleanup fails

            // Act
            var result = await _service.CleanupOrphanedLocksAsync(maxHeartbeatAge);

            // Assert
            Assert.Equal(1, result.OrphanedLocksFound);
            Assert.Equal(0, result.LocksCleanedUp);
            Assert.Equal(1, result.FailedCleanups);
            Assert.Empty(result.CleanedUpMigrationIds);
            Assert.Single(result.Failures);
            Assert.Equal(TestMigrationId1, result.Failures[0].MigrationId);
            Assert.Equal("Failed to force release lock", result.Failures[0].ErrorMessage);
        }

        [Fact]
        public async Task CleanupOrphanedLocksAsync_WithException_ShouldRecordFailureWithException()
        {
            // Arrange
            var maxHeartbeatAge = TimeSpan.FromMinutes(10);
            var oldTime = DateTime.UtcNow.AddMinutes(-15);
            var testException = new InvalidOperationException("Test exception");

            var allLocks = new List<DistributedLockInfo>
            {
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId1}",
                    InstanceId = TestInstanceId1,
                    MigrationId = TestMigrationId1,
                    LastHeartbeat = oldTime,
                    Status = DistributedLockStatus.Active
                }
            };

            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(allLocks);

            _mockLockService
                .Setup(x => x.ForceReleaseLockAsync($"orchestrator-{TestMigrationId1}", It.IsAny<CancellationToken>()))
                .ThrowsAsync(testException);

            // Act
            var result = await _service.CleanupOrphanedLocksAsync(maxHeartbeatAge);

            // Assert
            Assert.Equal(1, result.OrphanedLocksFound);
            Assert.Equal(0, result.LocksCleanedUp);
            Assert.Equal(1, result.FailedCleanups);
            Assert.Single(result.Failures);
            Assert.Equal("Test exception", result.Failures[0].ErrorMessage);
            Assert.Contains("Test exception", result.Failures[0].ExceptionDetails);
        }

        [Fact]
        public async Task CleanupOrphanedLocksAsync_WithNoOrphanedLocks_ShouldReturnZeroResults()
        {
            // Arrange
            var maxHeartbeatAge = TimeSpan.FromMinutes(10);
            var recentTime = DateTime.UtcNow.AddMinutes(-2);

            var allLocks = new List<DistributedLockInfo>
            {
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId1}",
                    InstanceId = TestInstanceId1,
                    MigrationId = TestMigrationId1,
                    LastHeartbeat = recentTime, // Not orphaned
                    Status = DistributedLockStatus.Active
                }
            };

            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(allLocks);

            // Act
            var result = await _service.CleanupOrphanedLocksAsync(maxHeartbeatAge);

            // Assert
            Assert.Equal(0, result.OrphanedLocksFound);
            Assert.Equal(0, result.LocksCleanedUp);
            Assert.Equal(0, result.FailedCleanups);
            Assert.Empty(result.CleanedUpMigrationIds);
            Assert.Empty(result.Failures);

            _mockLockService.Verify(
                x => x.ForceReleaseLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), 
                Times.Never);
        }

        [Fact]
        public async Task ForceReleaseLockAsync_WithValidMigrationId_ShouldReleaseSuccessfully()
        {
            // Arrange
            var reason = "Administrative cleanup";

            _mockLockService
                .Setup(x => x.ForceReleaseLockAsync($"orchestrator-{TestMigrationId1}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.ForceReleaseLockAsync(TestMigrationId1, reason);

            // Assert
            Assert.True(result);
            _mockLockService.Verify(
                x => x.ForceReleaseLockAsync($"orchestrator-{TestMigrationId1}", It.IsAny<CancellationToken>()), 
                Times.Once);
        }

        [Fact]
        public async Task ForceReleaseLockAsync_WithNullMigrationId_ShouldThrowArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _service.ForceReleaseLockAsync(null!, "test reason"));
        }

        [Fact]
        public async Task GetAllActiveLocksAsync_ShouldReturnOnlyOrchestratorLocks()
        {
            // Arrange
            var allLocks = new List<DistributedLockInfo>
            {
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId1}",
                    InstanceId = TestInstanceId1,
                    MigrationId = TestMigrationId1,
                    LastHeartbeat = DateTime.UtcNow.AddMinutes(-2),
                    Status = DistributedLockStatus.Active
                },
                new DistributedLockInfo
                {
                    LockKey = "other-lock-type", // Not an orchestrator lock
                    InstanceId = TestInstanceId2,
                    MigrationId = TestMigrationId2,
                    LastHeartbeat = DateTime.UtcNow.AddMinutes(-1),
                    Status = DistributedLockStatus.Active
                }
            };

            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(allLocks);

            // Act
            var result = await _service.GetAllActiveLocksAsync();

            // Assert
            Assert.Single(result);
            Assert.Equal(TestMigrationId1, result[0].MigrationId);
            Assert.Equal(TestInstanceId1, result[0].InstanceId);
        }

        [Fact]
        public async Task GetCleanupCandidatesAsync_ShouldIdentifyExpiredLocks()
        {
            // Arrange
            var maxHeartbeatAge = TimeSpan.FromMinutes(10);
            var oldTime = DateTime.UtcNow.AddMinutes(-15);
            var recentTime = DateTime.UtcNow.AddMinutes(-2);

            var allLocks = new List<DistributedLockInfo>
            {
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId1}",
                    InstanceId = TestInstanceId1,
                    MigrationId = TestMigrationId1,
                    LastHeartbeat = oldTime, // Candidate for cleanup
                    Status = DistributedLockStatus.Active
                },
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId2}",
                    InstanceId = TestInstanceId2,
                    MigrationId = TestMigrationId2,
                    LastHeartbeat = recentTime, // Not a candidate
                    Status = DistributedLockStatus.Active
                }
            };

            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(allLocks);

            // Act
            var result = await _service.GetCleanupCandidatesAsync(maxHeartbeatAge);

            // Assert
            Assert.Single(result);
            Assert.Equal(TestMigrationId1, result[0].MigrationId);
            Assert.True(result[0].IsOrphaned);
        }

        [Fact]
        public async Task ValidateSystemHealthAsync_WithHealthySystem_ShouldReturnHealthyStatus()
        {
            // Arrange
            var recentTime = DateTime.UtcNow.AddMinutes(-1);

            var allLocks = new List<DistributedLockInfo>
            {
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId1}",
                    InstanceId = TestInstanceId1,
                    MigrationId = TestMigrationId1,
                    LastHeartbeat = recentTime, // Healthy
                    Status = DistributedLockStatus.Active
                },
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId2}",
                    InstanceId = TestInstanceId2,
                    MigrationId = TestMigrationId2,
                    LastHeartbeat = recentTime, // Healthy
                    Status = DistributedLockStatus.Active
                }
            };

            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(allLocks);

            // Act
            var result = await _service.ValidateSystemHealthAsync();

            // Assert
            Assert.Equal(LockSystemHealth.Healthy, result.OverallHealth);
            Assert.Equal(2, result.TotalActiveLocks);
            Assert.Equal(2, result.HealthyLocks);
            Assert.Equal(0, result.StaleLocks);
            Assert.Equal(0, result.OrphanedLocks);
            Assert.Contains("100% of locks are healthy", result.HealthMessages[0]);
        }

        [Fact]
        public async Task ValidateSystemHealthAsync_WithOrphanedLocks_ShouldReturnCriticalStatus()
        {
            // Arrange
            var oldTime = DateTime.UtcNow.AddMinutes(-10); // Orphaned (older than 5 minutes)

            var allLocks = new List<DistributedLockInfo>
            {
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId1}",
                    InstanceId = TestInstanceId1,
                    MigrationId = TestMigrationId1,
                    LastHeartbeat = oldTime, // Orphaned
                    Status = DistributedLockStatus.Active
                },
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId2}",
                    InstanceId = TestInstanceId2,
                    MigrationId = TestMigrationId2,
                    LastHeartbeat = oldTime, // Orphaned
                    Status = DistributedLockStatus.Active
                }
            };

            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(allLocks);

            // Act
            var result = await _service.ValidateSystemHealthAsync();

            // Assert
            Assert.Equal(LockSystemHealth.Critical, result.OverallHealth);
            Assert.Equal(2, result.TotalActiveLocks);
            Assert.Equal(0, result.HealthyLocks);
            Assert.Equal(0, result.StaleLocks);
            Assert.Equal(2, result.OrphanedLocks);
            Assert.Contains("Critical: 100% of locks appear orphaned", result.HealthMessages[0]);
        }

        [Fact]
        public async Task ValidateSystemHealthAsync_WithNoLocks_ShouldReturnHealthyStatus()
        {
            // Arrange
            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DistributedLockInfo>());

            // Act
            var result = await _service.ValidateSystemHealthAsync();

            // Assert
            Assert.Equal(LockSystemHealth.Healthy, result.OverallHealth);
            Assert.Equal(0, result.TotalActiveLocks);
            Assert.Contains("No active locks - system is idle", result.HealthMessages[0]);
        }

        [Fact]
        public async Task PerformMaintenanceAsync_WithBothOptions_ShouldPerformBothOperations()
        {
            // Arrange
            var options = new MaintenanceOptions
            {
                CleanupOrphanedLocks = true,
                ValidateHealth = true,
                MaxHeartbeatAge = TimeSpan.FromMinutes(10),
                MaintenanceReason = "Scheduled maintenance test"
            };

            var recentTime = DateTime.UtcNow.AddMinutes(-1);
            var oldTime = DateTime.UtcNow.AddMinutes(-15);

            var allLocks = new List<DistributedLockInfo>
            {
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId1}",
                    InstanceId = TestInstanceId1,
                    MigrationId = TestMigrationId1,
                    LastHeartbeat = recentTime, // Healthy
                    Status = DistributedLockStatus.Active
                },
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId2}",
                    InstanceId = TestInstanceId2,
                    MigrationId = TestMigrationId2,
                    LastHeartbeat = oldTime, // Orphaned
                    Status = DistributedLockStatus.Active
                }
            };

            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(allLocks);

            _mockLockService
                .Setup(x => x.ForceReleaseLockAsync($"orchestrator-{TestMigrationId2}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _service.PerformMaintenanceAsync(options);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.CleanupResult);
            Assert.NotNull(result.HealthResult);
            Assert.Equal(1, result.CleanupResult.OrphanedLocksFound);
            Assert.Equal(1, result.CleanupResult.LocksCleanedUp);
            // Health check runs BEFORE cleanup, so it sees the original state (1 healthy, 1 orphaned = Warning)
            Assert.Equal(LockSystemHealth.Warning, result.HealthResult.OverallHealth);
            Assert.Contains("Starting maintenance", result.Messages[0]);
            Assert.Contains("Health check completed", result.Messages[1]);
            Assert.Contains("Cleanup completed", result.Messages[2]);
        }

        [Fact]
        public async Task PerformMaintenanceAsync_WithHealthOnlyOption_ShouldOnlyValidateHealth()
        {
            // Arrange
            var options = new MaintenanceOptions
            {
                CleanupOrphanedLocks = false,
                ValidateHealth = true,
                MaintenanceReason = "Health check only"
            };

            var recentTime = DateTime.UtcNow.AddMinutes(-1);
            var allLocks = new List<DistributedLockInfo>
            {
                new DistributedLockInfo
                {
                    LockKey = $"orchestrator-{TestMigrationId1}",
                    InstanceId = TestInstanceId1,
                    MigrationId = TestMigrationId1,
                    LastHeartbeat = recentTime,
                    Status = DistributedLockStatus.Active
                }
            };

            _mockLockService
                .Setup(x => x.GetAllLocksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(allLocks);

            // Act
            var result = await _service.PerformMaintenanceAsync(options);

            // Assert
            Assert.True(result.Success);
            Assert.Null(result.CleanupResult);
            Assert.NotNull(result.HealthResult);
            Assert.Equal(LockSystemHealth.Healthy, result.HealthResult.OverallHealth);
            Assert.DoesNotContain(result.Messages, m => m.Contains("Cleanup"));
            Assert.Contains(result.Messages, m => m.Contains("Health check completed"));

            _mockLockService.Verify(
                x => x.ForceReleaseLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), 
                Times.Never);
        }

        [Fact]
        public void Constructor_WithNullParameters_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new OrchestratorCleanupService(null!, _mockHeartbeatService.Object, _mockLogger.Object));
            
            Assert.Throws<ArgumentNullException>(() => 
                new OrchestratorCleanupService(_mockLockService.Object, null!, _mockLogger.Object));
            
            Assert.Throws<ArgumentNullException>(() => 
                new OrchestratorCleanupService(_mockLockService.Object, _mockHeartbeatService.Object, null!));
        }
    }
} 