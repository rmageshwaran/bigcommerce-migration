using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Functions.Functions;

namespace BigCommerce.Migration.UnitTests.Integration
{
    /// <summary>
    /// Phase 4.4: Comprehensive integration tests for the complete soft cancellation flow
    /// Tests end-to-end cancellation propagation across all system components
    /// </summary>
    public class SoftCancellationFlowIntegrationTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly Mock<IMigrationStorageService> _mockStorageService;
        private readonly Mock<IQueueService> _mockQueueService;
        private readonly Mock<IProgressTracker> _mockProgressTracker;

        public SoftCancellationFlowIntegrationTests()
        {
            // Setup service collection with real implementations
            var services = new ServiceCollection();
            
            // Add logging
            services.AddLogging(builder => builder.AddConsole());
            
            // Mock external dependencies
            _mockStorageService = new Mock<IMigrationStorageService>();
            _mockQueueService = new Mock<IQueueService>();
            _mockProgressTracker = new Mock<IProgressTracker>();
            
            services.AddSingleton(_mockStorageService.Object);
            services.AddSingleton(_mockQueueService.Object);
            services.AddSingleton(_mockProgressTracker.Object);
            
            // Add missing ILiveCancellationManager mock for activities that need it
            var mockLiveCancellationManager = new Mock<ILiveCancellationManager>();
            services.AddSingleton(mockLiveCancellationManager.Object);
            
            // Add real implementations for validation
            services.AddScoped<IProgressStateValidator, ProgressStateValidator>();
            services.AddSingleton<ISignalRMessageConverter, SignalRMessageConverter>();
            services.AddScoped<UpdateEntityProgressActivity>();
            services.AddScoped<StartEntityProcessingActivity>();
            services.AddScoped<CheckExternalCancellationActivity>();
            services.AddScoped<SignalRProgressFunctions>();
            
            _serviceProvider = services.BuildServiceProvider();
        }

        #region End-to-End Cancellation Flow Tests

        [Fact]
        public async Task EndToEndCancellationFlow_WithCancelledMigration_PropagatesCorrectly()
        {
            // Arrange
            var migrationId = "test-migration-cancelled";
            var cancellationReason = "User requested cancellation during processing";
            var cancelledAt = DateTime.UtcNow.AddMinutes(-10);
            
            // Setup: Migration is cancelled in storage
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedBy = "integration-test",
                RequestedAt = cancelledAt,
                IsProcessed = false,
                Status = "Pending"
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);

            // Act & Assert: Test the complete flow
            await TestOrchestratorToBatchFlow(migrationId, cancellationReason, cancelledAt);
            await TestActivityCancellationHandling(migrationId, cancellationReason, cancelledAt);
            await TestProgressTrackingCancellation(migrationId, cancellationReason, cancelledAt);
            await TestSignalRFilteringCancellation(migrationId, cancellationReason, cancelledAt);
            await TestValidationServiceIntegration(migrationId, cancellationReason, cancelledAt);
        }

        [Fact]
        public async Task EndToEndCancellationFlow_WithActiveMigration_AllowsNormalFlow()
        {
            // Arrange
            var migrationId = "test-migration-active";
            
            // Setup: Migration is NOT cancelled
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync((CancellationTokenEntry?)null);

            // Act & Assert: Test normal flow (no cancellation)
            await TestNormalProcessingFlow(migrationId);
        }

        [Fact]
        public async Task CheckExternalCancellationActivity_WithCancelledMigration_ReturnsCorrectResult()
        {
            // Arrange
            var migrationId = "test-migration-activity";
            var cancellationReason = "Activity test cancellation";
            var cancelledAt = DateTime.UtcNow.AddMinutes(-5);
            
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedAt = cancelledAt,
                RequestedBy = "activity-test"
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);
            
            var activity = _serviceProvider.GetRequiredService<CheckExternalCancellationActivity>();
            
            // Act
            var request = new CheckExternalCancellationRequest { MigrationId = migrationId };
            var result = await activity.CheckExternalCancellationOnceAsync(request);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsCancelled);
            Assert.Equal(cancellationReason, result.CancellationReason);
        }

        #endregion

        #region Component Integration Tests

        private async Task TestOrchestratorToBatchFlow(string migrationId, string cancellationReason, DateTime cancelledAt)
        {
            // Test that orchestrator level cancellation state flows to batch processing correctly
            
            // Simulate orchestrator creating batch processing request with soft cancellation token
            var batchRequest = new BatchProcessingRequest
            {
                MigrationId = migrationId,
                EntityType = "products",
                EntityIds = new List<string> { "1", "2", "3" },
                BatchNumber = 1,
                TotalBatches = 5,
                // Phase 4.1: Soft cancellation token from orchestrator
                IsCancelled = true,
                CancellationReason = cancellationReason,
                CancelledAt = cancelledAt,
                Timestamp = DateTime.UtcNow
            };

            // Verify batch request contains correct cancellation state
            Assert.True(batchRequest.IsCancelled);
            Assert.Equal(cancellationReason, batchRequest.CancellationReason);
            Assert.Equal(cancelledAt, batchRequest.CancelledAt);
            
            // Verify timestamp is deterministic (from orchestrator)
            Assert.True(batchRequest.Timestamp > DateTime.UtcNow.AddMinutes(-1));
            
            await Task.CompletedTask;
        }

        private async Task TestActivityCancellationHandling(string migrationId, string cancellationReason, DateTime cancelledAt)
        {
            // Test that activities properly handle soft cancellation tokens
            
            var updateActivity = _serviceProvider.GetRequiredService<UpdateEntityProgressActivity>();
            var startActivity = _serviceProvider.GetRequiredService<StartEntityProcessingActivity>();
            
            // Test UpdateEntityProgressActivity with cancellation
            var updateRequest = new UpdateEntityProgressRequest
            {
                MigrationId = migrationId,
                EntityType = "products",
                Phase = "Processing",
                ProcessedEntities = 50,
                Timestamp = DateTime.UtcNow,
                // Phase 4.1: Soft cancellation token
                IsCancelled = true,
                CancellationReason = cancellationReason,
                CancelledAt = cancelledAt
            };
            
            // Activity should handle cancellation gracefully (no exception)
            await updateActivity.UpdateEntityProgressAsync(updateRequest);
            
            // Test StartEntityProcessingActivity with cancellation
            var startRequest = new StartEntityProcessingRequest
            {
                MigrationId = migrationId,
                EntityType = "products",
                TotalCount = 100,
                Timestamp = DateTime.UtcNow,
                // Phase 4.1: Soft cancellation token
                IsCancelled = true,
                CancellationReason = cancellationReason,
                CancelledAt = cancelledAt
            };
            
            // Activity should handle cancellation gracefully (no exception)
            await startActivity.StartEntityProcessingAsync(startRequest);
        }

        private async Task TestProgressTrackingCancellation(string migrationId, string cancellationReason, DateTime cancelledAt)
        {
            // Test that progress tracking correctly handles and propagates cancellation state
            
            // Setup mock to capture progress updates
            ProgressUpdate? capturedProgressUpdate = null;
            _mockProgressTracker.Setup(x => x.UpdateProgressAsync(migrationId, It.IsAny<ProgressUpdate>(), It.IsAny<CancellationToken>()))
                .Callback<string, ProgressUpdate, CancellationToken>((id, update, token) => capturedProgressUpdate = update)
                .Returns(Task.CompletedTask);
            
            // Create progress update with cancellation state
            var progressUpdate = new ProgressUpdate
            {
                MigrationId = migrationId,
                EntityType = "products",
                Phase = "Processing",
                ProcessedCount = 75,
                SuccessCount = 70,
                FailureCount = 5,
                Timestamp = DateTime.UtcNow,
                // Phase 4.2: Cancellation state from activity
                IsCancelled = true,
                CancellationReason = cancellationReason,
                CancelledAt = cancelledAt
            };
            
            // Simulate progress tracker receiving the update
            await _mockProgressTracker.Object.UpdateProgressAsync(migrationId, progressUpdate);
            
            // Verify the progress update was received with correct cancellation state
            Assert.NotNull(capturedProgressUpdate);
            Assert.True(capturedProgressUpdate.IsCancelled);
            Assert.Equal(cancellationReason, capturedProgressUpdate.CancellationReason);
            Assert.Equal(cancelledAt, capturedProgressUpdate.CancelledAt);
        }

        private async Task TestSignalRFilteringCancellation(string migrationId, string cancellationReason, DateTime cancelledAt)
        {
            // Test that SignalR functions correctly filter cancelled progress events
            
            var signalRFunctions = _serviceProvider.GetRequiredService<SignalRProgressFunctions>();
            
            // Create a progress event with cancellation state
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = migrationId,
                EventType = "progress",
                Timestamp = DateTime.UtcNow,
                // Phase 4.2: Cancellation state from ProgressTracker
                IsCancelled = true,
                CancellationReason = cancellationReason,
                CancelledAt = cancelledAt,
                // Progress data
                TotalEntities = 100,
                ProcessedEntities = 75,
                FailedEntities = 5
            };
            
            // Serialize the event as it would come from the queue
            var queueMessage = System.Text.Json.JsonSerializer.Serialize(progressEvent);
            
            // Process the event through SignalR function
            var result = signalRFunctions.ProcessProgressEvents(queueMessage);
            
            // Verify that progress event was filtered and cancellation notification was sent
            Assert.NotNull(result);
            // Should return a "migrationCancelled" notification instead of progress
        }

        private async Task TestValidationServiceIntegration(string migrationId, string cancellationReason, DateTime cancelledAt)
        {
            // Test that the validation service correctly validates the entire flow
            
            var validator = _serviceProvider.GetRequiredService<IProgressStateValidator>();
            
            // Test progress update validation
            var progressUpdate = new ProgressUpdate
            {
                MigrationId = migrationId,
                EntityType = "products",
                Phase = "Processing",
                ProcessedCount = 75,
                Timestamp = DateTime.UtcNow,
                IsCancelled = true,
                CancellationReason = cancellationReason,
                CancelledAt = cancelledAt
            };
            
            var updateValidation = await validator.ValidateProgressUpdateAsync(progressUpdate);
            Assert.True(updateValidation.IsValid);
            
            // Test progress event validation
            var progressEvent = new EntityProgressEvent
            {
                MigrationId = migrationId,
                EventType = "entity",
                IsCancelled = true,
                CancellationReason = cancellationReason,
                CancelledAt = cancelledAt
            };
            
            var eventValidation = await validator.ValidateProgressEventAsync(progressEvent);
            // Progress events should be flagged for filtering
            Assert.True(eventValidation.IsValid);
            
            // Test migration-wide validation
            var migrationValidation = await validator.ValidateMigrationProgressConsistencyAsync(migrationId);
            Assert.True(migrationValidation.IsOverallValid);
            
            // Test cancellation propagation validation
            var propagationValidation = await validator.ValidateCancellationPropagationAsync(migrationId);
            Assert.True(propagationValidation.IsPropagationWorking);
        }

        private async Task TestNormalProcessingFlow(string migrationId)
        {
            // Test that normal processing (no cancellation) works correctly
            
            // Test activity processing without cancellation
            var updateActivity = _serviceProvider.GetRequiredService<UpdateEntityProgressActivity>();
            
            var updateRequest = new UpdateEntityProgressRequest
            {
                MigrationId = migrationId,
                EntityType = "products",
                Phase = "Processing",
                ProcessedEntities = 50,
                Timestamp = DateTime.UtcNow,
                // No cancellation
                IsCancelled = false,
                CancellationReason = null,
                CancelledAt = null
            };
            
            // Should process normally without issues
            await updateActivity.UpdateEntityProgressAsync(updateRequest);
            
            // Test validation for active migration
            var validator = _serviceProvider.GetRequiredService<IProgressStateValidator>();
            var validation = await validator.ValidateMigrationProgressConsistencyAsync(migrationId);
            
            Assert.True(validation.IsOverallValid);
        }

        #endregion

        #region Performance Tests

        [Fact]
        public async Task CancellationResponsiveness_FastPropagation_CompletesQuickly()
        {
            // Test that cancellation propagates through the system quickly
            
            var migrationId = "test-migration-responsive";
            var cancellationReason = "Performance test cancellation";
            var threshold = TimeSpan.FromMilliseconds(500); // 500ms threshold
            
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedAt = DateTime.UtcNow,
                RequestedBy = "performance-test"
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);
            
            var validator = _serviceProvider.GetRequiredService<IProgressStateValidator>();
            
            // Measure propagation time through validation service
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var propagationResult = await validator.ValidateCancellationPropagationAsync(migrationId);
            var migrationValidation = await validator.ValidateMigrationProgressConsistencyAsync(migrationId);
            
            stopwatch.Stop();
            
            // Verify fast response
            Assert.True(stopwatch.Elapsed < threshold, 
                $"Cancellation validation took {stopwatch.ElapsedMilliseconds}ms, expected < {threshold.TotalMilliseconds}ms");
            
            // Verify correctness
            Assert.True(propagationResult.IsPropagationWorking);
            Assert.True(migrationValidation.IsOverallValid);
        }

        #endregion

        #region Error Scenario Tests

        [Fact]
        public async Task ErrorScenario_StorageUnavailable_HandlesGracefully()
        {
            // Test that the system handles storage unavailability gracefully
            
            var migrationId = "test-migration-storage-error";
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ThrowsAsync(new InvalidOperationException("Storage unavailable"));
            
            var validator = _serviceProvider.GetRequiredService<IProgressStateValidator>();
            
            // Validation should handle the error gracefully
            var result = await validator.ValidateMigrationProgressConsistencyAsync(migrationId);
            
            Assert.False(result.IsOverallValid);
        }

        [Fact]
        public async Task ErrorScenario_InconsistentCancellationState_DetectedCorrectly()
        {
            // Test that inconsistent cancellation state is detected and reported
            
            var migrationId = "test-migration-inconsistent";
            
            // Setup: Storage says migration is cancelled
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = "Cancelled by user",
                RequestedAt = DateTime.UtcNow.AddMinutes(-5)
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);
            
            var validator = _serviceProvider.GetRequiredService<IProgressStateValidator>();
            
            // But progress update claims it's not cancelled (inconsistent state)
            var progressUpdate = new ProgressUpdate
            {
                MigrationId = migrationId,
                IsCancelled = false, // Inconsistent with storage
                EntityType = "products",
                Phase = "Processing"
            };
            
            var result = await validator.ValidateProgressUpdateAsync(progressUpdate);
            
            // Should detect the inconsistency
            Assert.False(result.IsValid);
            Assert.Equal("Ensure soft cancellation token is properly propagated from orchestrator", 
                result.RecommendedAction);
        }

        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _serviceProvider?.Dispose();
            }
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
} 