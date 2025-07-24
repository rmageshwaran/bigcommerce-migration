using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services
{
    /// <summary>
    /// Phase 4.3: Unit tests for ProgressStateValidator service
    /// Ensures progress state validation works correctly with cancellation state
    /// </summary>
    public class ProgressStateValidatorTests
    {
        private readonly Mock<IMigrationStorageService> _mockStorageService;
        private readonly Mock<ILogger<ProgressStateValidator>> _mockLogger;
        private readonly ProgressStateValidator _validator;

        public ProgressStateValidatorTests()
        {
            _mockStorageService = new Mock<IMigrationStorageService>();
            _mockLogger = new Mock<ILogger<ProgressStateValidator>>();
            _validator = new ProgressStateValidator(_mockStorageService.Object, _mockLogger.Object);
        }

        #region ValidateProgressUpdateAsync Tests

        [Fact]
        public async Task ValidateProgressUpdateAsync_WithConsistentCancelledState_ReturnsValid()
        {
            // Arrange
            var migrationId = "test-migration";
            var cancellationReason = "User requested cancellation";
            var cancelledAt = DateTime.UtcNow.AddMinutes(-5);

            var progressUpdate = new ProgressUpdate
            {
                MigrationId = migrationId,
                IsCancelled = true,
                CancellationReason = cancellationReason,
                CancelledAt = cancelledAt
            };

            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedAt = cancelledAt.AddMinutes(-1)
            };

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);

            // Act
            var result = await _validator.ValidateProgressUpdateAsync(progressUpdate);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public async Task ValidateProgressUpdateAsync_WithMissingCancellationReason_ReturnsWarning()
        {
            // Arrange
            var migrationId = "test-migration";
            var progressUpdate = new ProgressUpdate
            {
                MigrationId = migrationId,
                IsCancelled = true,
                CancellationReason = null, // Missing reason
                CancelledAt = DateTime.UtcNow
            };

            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = "User requested",
                RequestedAt = DateTime.UtcNow.AddMinutes(-1)
            };

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);

            // Act
            var result = await _validator.ValidateProgressUpdateAsync(progressUpdate);

            // Assert
            Assert.True(result.IsValid);
            Assert.Contains("Progress update marked as cancelled but lacks cancellation reason", result.Warnings);
        }

        [Fact]
        public async Task ValidateProgressUpdateAsync_WithInconsistentState_ReturnsInvalid()
        {
            // Arrange
            var migrationId = "test-migration";
            var progressUpdate = new ProgressUpdate
            {
                MigrationId = migrationId,
                IsCancelled = false // Claims not cancelled
            };

            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = "User requested",
                RequestedAt = DateTime.UtcNow.AddMinutes(-1)
            };

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken); // But storage says it's cancelled

            // Act
            var result = await _validator.ValidateProgressUpdateAsync(progressUpdate);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("is cancelled in storage but progress update does not reflect cancellation", result.Errors[0]);
            Assert.Equal("Ensure soft cancellation token is properly propagated from orchestrator", result.RecommendedAction);
        }

        [Fact]
        public async Task ValidateProgressUpdateAsync_WithStaleState_ReturnsInvalid()
        {
            // Arrange
            var migrationId = "test-migration";
            var progressUpdate = new ProgressUpdate
            {
                MigrationId = migrationId,
                IsCancelled = true,
                CancellationReason = "Stale reason"
            };

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync((CancellationTokenEntry?)null); // Storage says not cancelled

            // Act
            var result = await _validator.ValidateProgressUpdateAsync(progressUpdate);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Progress update claims cancellation but migration", result.Errors[0]);
            Assert.Equal("Check for stale cancellation state in soft token propagation", result.RecommendedAction);
        }

        #endregion

        #region ValidateProgressEventAsync Tests

        [Fact]
        public async Task ValidateProgressEventAsync_WithProgressTypeAndCancelled_ReturnsWarning()
        {
            // Arrange
            var migrationId = "test-migration";
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = migrationId,
                EventType = "progress",
                IsCancelled = true,
                CancellationReason = "User requested"
            };

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(new CancellationTokenEntry { MigrationId = migrationId, Reason = "User requested" });

            // Act
            var result = await _validator.ValidateProgressEventAsync(progressEvent);

            // Assert
            Assert.True(result.IsValid);
            Assert.Contains("Progress-type event (progress) is marked as cancelled - should be filtered by SignalR", result.Warnings);
        }

        [Fact]
        public async Task ValidateProgressEventAsync_WithStatusType_DoesNotWarn()
        {
            // Arrange
            var migrationId = "test-migration";
            var progressEvent = new StatusProgressEvent
            {
                MigrationId = migrationId,
                EventType = "status",
                IsCancelled = true,
                CancellationReason = "User requested",
                CancelledAt = DateTime.UtcNow.AddMinutes(-5) // Add missing timestamp
            };

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(new CancellationTokenEntry { MigrationId = migrationId, Reason = "User requested" });

            // Act
            var result = await _validator.ValidateProgressEventAsync(progressEvent);

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Warnings);
            Assert.Equal("Control event (status) correctly includes cancellation state", result.ValidationMessage);
        }

        [Fact]
        public async Task ValidateProgressEventAsync_WithInconsistentEventState_ReturnsInvalid()
        {
            // Arrange
            var migrationId = "test-migration";
            var progressEvent = new EntityProgressEvent
            {
                MigrationId = migrationId,
                EventType = "entity",
                IsCancelled = false // Event claims not cancelled
            };

            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = "User requested",
                RequestedAt = DateTime.UtcNow.AddMinutes(-1)
            };

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken); // But storage says it's cancelled

            // Act
            var result = await _validator.ValidateProgressEventAsync(progressEvent);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("is cancelled but progress event does not reflect cancellation", result.Errors[0]);
            Assert.Equal("Check soft cancellation token propagation through ProgressTracker", result.RecommendedAction);
        }

        #endregion

        #region ValidateMigrationProgressConsistencyAsync Tests

        [Fact]
        public async Task ValidateMigrationProgressConsistencyAsync_WithValidState_ReturnsValid()
        {
            // Arrange
            var migrationId = "test-migration";
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = "User requested",
                RequestedAt = DateTime.UtcNow.AddMinutes(-5)
            };

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);

            // Act
            var result = await _validator.ValidateMigrationProgressConsistencyAsync(migrationId);

            // Assert
            Assert.True(result.IsOverallValid);
            Assert.True(result.IsCancellationStateConsistent);
            Assert.True(result.IsProgressStateConsistent);
            Assert.Equal(migrationId, result.MigrationId);
            Assert.Contains("CANCELLED (User requested)", result.ValidationSummary[0]);
            Assert.Contains("✅ PASSED", result.ValidationSummary[1]);
        }

        [Fact]
        public async Task ValidateMigrationProgressConsistencyAsync_WithActiveMigration_ReturnsValid()
        {
            // Arrange
            var migrationId = "test-migration";

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync((CancellationTokenEntry?)null); // Not cancelled

            // Act
            var result = await _validator.ValidateMigrationProgressConsistencyAsync(migrationId);

            // Assert
            Assert.True(result.IsOverallValid);
            Assert.Equal(migrationId, result.MigrationId);
            Assert.Contains("ACTIVE", result.ValidationSummary[0]);
        }

        #endregion

        #region ValidateCancellationPropagationAsync Tests

        [Fact]
        public async Task ValidateCancellationPropagationAsync_WithCancellation_ValidatesPropagationPath()
        {
            // Arrange
            var migrationId = "test-migration";
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = "User requested cancellation",
                RequestedAt = DateTime.UtcNow.AddMinutes(-5)
            };

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);

            // Act
            var result = await _validator.ValidateCancellationPropagationAsync(migrationId);

            // Assert
            Assert.True(result.IsPropagationWorking);
            Assert.True(result.IsOrchestratorAware);
            Assert.True(result.AreActivitiesAware);
            Assert.True(result.IsProgressTrackerAware);
            Assert.True(result.IsSignalRFilteringWorking);
            Assert.Equal(migrationId, result.MigrationId);
            
            // Check propagation path details
            Assert.Contains("✅ HAS CANCELLATION", result.PropagationPath[0]);
            Assert.Contains("User requested cancellation", result.PropagationPath[1]);
            Assert.Contains("Orchestrator: ✅ AWARE", result.PropagationPath[3]);
            Assert.Contains("Activities: ✅ AWARE", result.PropagationPath[4]);
            Assert.Contains("ProgressTracker: ✅ AWARE", result.PropagationPath[5]);
            Assert.Contains("SignalR Functions: ✅ FILTERING", result.PropagationPath[6]);
            Assert.Contains("✅ WORKING CORRECTLY", result.PropagationPath[7]);
        }

        [Fact]
        public async Task ValidateCancellationPropagationAsync_WithoutCancellation_ValidatesCorrectly()
        {
            // Arrange
            var migrationId = "test-migration";

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync((CancellationTokenEntry?)null);

            // Act
            var result = await _validator.ValidateCancellationPropagationAsync(migrationId);

            // Assert
            Assert.True(result.IsPropagationWorking);
            Assert.Equal(migrationId, result.MigrationId);
            Assert.Contains("ℹ️ NO CANCELLATION", result.PropagationPath[0]);
            Assert.Contains("✅ WORKING CORRECTLY", result.PropagationPath.Last());
        }

        #endregion

        #region Exception Handling Tests

        [Fact]
        public async Task ValidateProgressUpdateAsync_WithStorageException_ReturnsInvalid()
        {
            // Arrange
            var migrationId = "test-migration";
            var progressUpdate = new ProgressUpdate { MigrationId = migrationId };

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ThrowsAsync(new InvalidOperationException("Storage error"));

            // Act
            var result = await _validator.ValidateProgressUpdateAsync(progressUpdate);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Storage error", result.Errors[0]);
            Assert.Equal("Check validation service configuration and storage connectivity", result.RecommendedAction);
        }

        [Fact]
        public async Task ValidateMigrationProgressConsistencyAsync_WithException_ReturnsInvalid()
        {
            // Arrange
            var migrationId = "test-migration";

            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ThrowsAsync(new TimeoutException("Timeout"));

            // Act
            var result = await _validator.ValidateMigrationProgressConsistencyAsync(migrationId);

            // Assert
            Assert.False(result.IsOverallValid);
            Assert.Equal(migrationId, result.MigrationId);
            Assert.Contains("Timeout", result.ValidationSummary[0]);
        }

        #endregion
    }
} 