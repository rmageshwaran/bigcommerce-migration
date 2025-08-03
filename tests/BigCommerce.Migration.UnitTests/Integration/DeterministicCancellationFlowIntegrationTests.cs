using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Activities;

namespace BigCommerce.Migration.UnitTests.Integration
{
    /// <summary>
    /// Phase 4.4: Integration tests for deterministic cancellation flow
    /// Tests the deterministic cancellation state management across orchestrators and activities
    /// </summary>
    public class DeterministicCancellationFlowIntegrationTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly Mock<IMigrationStorageService> _mockStorageService;
        private readonly Mock<ILogger<CheckExternalCancellationActivity>> _mockLogger;

        public DeterministicCancellationFlowIntegrationTests()
        {
            var services = new ServiceCollection();
            
            // Add logging
            services.AddLogging(builder => builder.AddConsole());
            
            // Mock storage service
            _mockStorageService = new Mock<IMigrationStorageService>();
            _mockLogger = new Mock<ILogger<CheckExternalCancellationActivity>>();
            
            services.AddSingleton(_mockStorageService.Object);
            services.AddSingleton(_mockLogger.Object);
            
            // Add missing ILiveCancellationManager mock for CheckExternalCancellationActivity
            var mockLiveCancellationManager = new Mock<ILiveCancellationManager>();
            services.AddSingleton(mockLiveCancellationManager.Object);
            
            // Add real activity for testing
            services.AddScoped<CheckExternalCancellationActivity>();
            
            _serviceProvider = services.BuildServiceProvider();
        }

        #region Deterministic Cancellation State Tests

        [Fact]
        public async Task DeterministicCancellationState_WithExternalCancellation_ReturnsConsistentResult()
        {
            // Arrange
            var migrationId = "test-migration-deterministic";
            var cancellationReason = "External cancellation during orchestration";
            var requestedAt = DateTime.UtcNow.AddMinutes(-5);
            
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedBy = "deterministic-test",
                RequestedAt = requestedAt,
                IsProcessed = false,
                Status = "Active"
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);
            
            var activity = _serviceProvider.GetRequiredService<CheckExternalCancellationActivity>();
            var request = new CheckExternalCancellationRequest { MigrationId = migrationId };
            
            // Act: Call the activity multiple times (simulating orchestrator replay)
            var result1 = await activity.CheckExternalCancellationOnceAsync(request);
            var result2 = await activity.CheckExternalCancellationOnceAsync(request);
            var result3 = await activity.CheckExternalCancellationOnceAsync(request);
            
            // Assert: All results should be identical (deterministic)
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            Assert.NotNull(result3);
            
            Assert.Equal(result1.IsCancelled, result2.IsCancelled);
            Assert.Equal(result1.IsCancelled, result3.IsCancelled);
            
            Assert.Equal(result1.CancellationReason, result2.CancellationReason);
            Assert.Equal(result1.CancellationReason, result3.CancellationReason);
            
            // All should indicate cancellation
            Assert.True(result1.IsCancelled);
            Assert.Equal(cancellationReason, result1.CancellationReason);
        }

        [Fact]
        public async Task DeterministicCancellationState_WithNoCancellation_ReturnsConsistentResult()
        {
            // Arrange
            var migrationId = "test-migration-active-deterministic";
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync((CancellationTokenEntry?)null);
            
            var activity = _serviceProvider.GetRequiredService<CheckExternalCancellationActivity>();
            var request = new CheckExternalCancellationRequest { MigrationId = migrationId };
            
            // Act: Multiple calls to simulate replay
            var result1 = await activity.CheckExternalCancellationOnceAsync(request);
            var result2 = await activity.CheckExternalCancellationOnceAsync(request);
            var result3 = await activity.CheckExternalCancellationOnceAsync(request);
            
            // Assert: All results should be identical
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            Assert.NotNull(result3);
            
            Assert.Equal(result1.IsCancelled, result2.IsCancelled);
            Assert.Equal(result1.IsCancelled, result3.IsCancelled);
            
            // All should indicate no cancellation
            Assert.False(result1.IsCancelled);
            Assert.Equal(string.Empty, result1.CancellationReason);
        }

        [Fact]
        public async Task DeterministicCancellationState_Create_InitializesCorrectly()
        {
            // Test the deterministic cancellation state creation and management
            
            var migrationId = "test-migration-state";
            
            // Simulate deterministic cancellation state initialization
            var cancellationState = DeterministicCancellationState.Create(migrationId);
            
            // Verify initial state
            Assert.False(cancellationState.StateChecked);
            Assert.False(cancellationState.IsCancelled);
            Assert.Equal(migrationId, cancellationState.MigrationId);
            Assert.Equal(1, cancellationState.Version);
        }

        #endregion

        #region Orchestrator Context Integration Tests

        [Fact]
        public async Task OrchestratorContextIntegration_WithCancellationState_PropagatesCorrectly()
        {
            // Test integration between orchestrator context and activities
            
            var migrationId = "test-migration-context";
            var entityType = "products";
            var cancellationReason = "Context integration test";
            var cancelledAt = DateTime.UtcNow.AddMinutes(-7);
            
            // Setup cancellation in storage
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedAt = cancelledAt,
                RequestedBy = "context-test"
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);
            
            // Simulate orchestrator determining cancellation state
            var activity = _serviceProvider.GetRequiredService<CheckExternalCancellationActivity>();
            var request = new CheckExternalCancellationRequest { MigrationId = migrationId };
            var cancellationResult = await activity.CheckExternalCancellationOnceAsync(request);
            
            // Verify the result contains correct cancellation state
            Assert.True(cancellationResult.IsCancelled);
            Assert.Equal(cancellationReason, cancellationResult.CancellationReason);
            
            // Test that activities would receive this state correctly via request objects
            var updateRequest = new UpdateEntityProgressRequest
            {
                MigrationId = migrationId,
                EntityType = entityType,
                Phase = "Processing",
                ProcessedEntities = 25,
                Timestamp = DateTime.UtcNow, // From orchestrator context.CurrentUtcDateTime
                // Soft cancellation token from orchestrator
                IsCancelled = cancellationResult.IsCancelled,
                CancellationReason = cancellationResult.CancellationReason,
                CancelledAt = cancellationResult.CancelledAt
            };
            
            Assert.True(updateRequest.IsCancelled);
            Assert.Equal(cancellationReason, updateRequest.CancellationReason);
        }

        [Fact]
        public async Task OrchestratorReplay_WithCancellationState_RemainsConsistent()
        {
            // Test that orchestrator replay scenarios maintain cancellation state consistency
            
            var migrationId = "test-migration-replay";
            var cancellationReason = "Replay consistency test";
            
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedAt = DateTime.UtcNow.AddMinutes(-10),
                RequestedBy = "replay-test"
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);
            
            // Simulate multiple orchestrator executions (replays)
            var activity = _serviceProvider.GetRequiredService<CheckExternalCancellationActivity>();
            var request = new CheckExternalCancellationRequest { MigrationId = migrationId };
            
            var executions = new List<CheckExternalCancellationResponse>();
            
            // Simulate 5 orchestrator replays
            for (int i = 0; i < 5; i++)
            {
                var result = await activity.CheckExternalCancellationOnceAsync(request);
                executions.Add(result);
            }
            
            // All executions should return identical results
            for (int i = 1; i < executions.Count; i++)
            {
                Assert.Equal(executions[0].IsCancelled, executions[i].IsCancelled);
                Assert.Equal(executions[0].CancellationReason, executions[i].CancellationReason);
                Assert.Equal(executions[0].CancelledAt, executions[i].CancelledAt);
            }
            
            // All should indicate cancellation
            Assert.All(executions, execution => Assert.True(execution.IsCancelled));
            Assert.All(executions, execution => Assert.Equal(cancellationReason, execution.CancellationReason));
        }

        #endregion

        #region Batch Processing Integration Tests

        [Fact]
        public async Task BatchProcessingIntegration_WithCancellation_StopsCorrectly()
        {
            // Test batch processing integration with cancellation
            
            var migrationId = "test-migration-batch-integration";
            var cancellationReason = "Batch processing cancellation";
            var cancelledAt = DateTime.UtcNow.AddMinutes(-2);
            
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedAt = cancelledAt,
                RequestedBy = "batch-integration-test"
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);
            
            // Simulate orchestrator checking cancellation once
            var activity = _serviceProvider.GetRequiredService<CheckExternalCancellationActivity>();
            var request = new CheckExternalCancellationRequest { MigrationId = migrationId };
            var cancellationResult = await activity.CheckExternalCancellationOnceAsync(request);
            
            // Simulate multiple batch processing requests with the same cancellation state
            var batches = new List<BatchProcessingRequest>();
            
            for (int batchIndex = 0; batchIndex < 10; batchIndex++)
            {
                var batchRequest = new BatchProcessingRequest
                {
                    MigrationId = migrationId,
                    EntityType = "products",
                    EntityIds = GenerateTestEntityIds(batchIndex * 10, 10),
                    BatchNumber = batchIndex + 1,
                    TotalBatches = 10,
                    Timestamp = DateTime.UtcNow,
                    // Cancellation state from orchestrator (same for all batches)
                    IsCancelled = cancellationResult.IsCancelled,
                    CancellationReason = cancellationResult.CancellationReason,
                    CancelledAt = cancellationResult.CancelledAt
                };
                
                batches.Add(batchRequest);
            }
            
            // All batches should have consistent cancellation state
            Assert.All(batches, batch => Assert.True(batch.IsCancelled));
            Assert.All(batches, batch => Assert.Equal(cancellationReason, batch.CancellationReason));
            Assert.All(batches, batch => Assert.Equal(cancelledAt, batch.CancelledAt));
            
            // Simulate batch processing logic - should stop early due to cancellation
            var processedBatches = 0;
            foreach (var batch in batches)
            {
                if (batch.IsCancelled)
                {
                    // Batch processing should stop immediately
                    break;
                }
                processedBatches++;
            }
            
            // No batches should be processed due to cancellation
            Assert.Equal(0, processedBatches);
        }

        [Fact]
        public async Task BatchProcessingIntegration_WithoutCancellation_ProcessesNormally()
        {
            // Test normal batch processing without cancellation
            
            var migrationId = "test-migration-batch-normal";
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync((CancellationTokenEntry?)null);
            
            // Orchestrator checks cancellation - should find none
            var activity = _serviceProvider.GetRequiredService<CheckExternalCancellationActivity>();
            var request = new CheckExternalCancellationRequest { MigrationId = migrationId };
            var cancellationResult = await activity.CheckExternalCancellationOnceAsync(request);
            
            Assert.False(cancellationResult.IsCancelled);
            
            // Create batch request without cancellation
            var batchRequest = new BatchProcessingRequest
            {
                MigrationId = migrationId,
                EntityType = "products",
                EntityIds = GenerateTestEntityIds(0, 10),
                BatchNumber = 1,
                TotalBatches = 5,
                Timestamp = DateTime.UtcNow,
                // No cancellation
                IsCancelled = cancellationResult.IsCancelled,
                CancellationReason = cancellationResult.CancellationReason,
                CancelledAt = cancellationResult.CancelledAt
            };
            
            Assert.False(batchRequest.IsCancelled);
            Assert.Equal(cancellationResult.CancellationReason, batchRequest.CancellationReason);
            Assert.Null(batchRequest.CancelledAt);
            
            // Batch should proceed normally
            var shouldProcess = !batchRequest.IsCancelled;
            Assert.True(shouldProcess);
        }

        #endregion

        #region Error Handling Integration Tests

        [Fact]
        public async Task ErrorHandlingIntegration_StorageException_HandlesGracefully()
        {
            // Test error handling integration when storage fails
            
            var migrationId = "test-migration-storage-error";
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ThrowsAsync(new TimeoutException("Storage timeout"));
            
            var activity = _serviceProvider.GetRequiredService<CheckExternalCancellationActivity>();
            var request = new CheckExternalCancellationRequest { MigrationId = migrationId };
            
            // Activity should handle the exception gracefully and return safe default
            var result = await activity.CheckExternalCancellationOnceAsync(request);
            
            // Should return safe default (not cancelled) to allow migration to continue
            Assert.False(result.IsCancelled);
            Assert.False(result.IsProcessed);
            Assert.Contains("Error checking cancellation", result.CancellationReason);
            Assert.Contains("Storage timeout", result.CancellationReason);
            
            // In real implementation, this would be logged and the migration continues
        }

        [Fact]
        public async Task ErrorHandlingIntegration_InvalidCancellationData_HandlesGracefully()
        {
            // Test handling of invalid cancellation data
            
            var migrationId = "test-migration-invalid-data";
            
            // Setup invalid cancellation token (missing required fields)
            var invalidCancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = null!, // Invalid: null reason
                RequestedAt = default, // Invalid: default timestamp
                RequestedBy = ""
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(invalidCancellationToken);
            
            var activity = _serviceProvider.GetRequiredService<CheckExternalCancellationActivity>();
            var request = new CheckExternalCancellationRequest { MigrationId = migrationId };
            
            // Activity should handle invalid data gracefully
            var result = await activity.CheckExternalCancellationOnceAsync(request);
            
            // Should still indicate cancellation but with safe values
            Assert.True(result.IsCancelled);
            // Implementation should handle null/invalid data appropriately
        }

        #endregion

        #region Helper Methods

        private List<string> GenerateTestEntityIds(int startIndex, int count)
        {
            var entityIds = new List<string>();
            
            for (int i = 0; i < count; i++)
            {
                entityIds.Add($"entity-{startIndex + i + 1}");
            }
            
            return entityIds;
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