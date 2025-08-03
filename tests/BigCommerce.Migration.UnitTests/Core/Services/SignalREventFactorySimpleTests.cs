using System;
using System.Text.Json;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Services
{
    /// <summary>
    /// Unit tests for SignalREventFactory based on actual class structure
    /// Tests core functionality and validates that factory methods work correctly
    /// </summary>
    public class SignalREventFactorySimpleTests
    {
        private readonly Mock<IDateTimeProvider> _mockDateTimeProvider;
        private readonly SignalREventFactory _factory;
        private readonly DateTime _testTimestamp = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        private const string TestMigrationId = "test-migration-123";

        public SignalREventFactorySimpleTests()
        {
            _mockDateTimeProvider = new Mock<IDateTimeProvider>();
            _mockDateTimeProvider.Setup(x => x.UtcNow).Returns(_testTimestamp);
            _factory = new SignalREventFactory(_mockDateTimeProvider.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullDateTimeProvider_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => new SignalREventFactory(null!));
            exception.ParamName.Should().Be("dateTimeProvider");
        }

        [Fact]
        public void Constructor_WithValidDateTimeProvider_ShouldCreateInstance()
        {
            // Act
            var factory = new SignalREventFactory(_mockDateTimeProvider.Object);

            // Assert
            factory.Should().NotBeNull();
        }

        #endregion

        #region CreateMigrationProgress Tests

        [Fact]
        public void CreateMigrationProgress_WithValidOptions_ShouldCreateEventWithCorrectProperties()
        {
            // Arrange
            var options = new MigrationProgressOptions
            {
                OverallProgress = 75.5,
                Status = "processing",
                TotalEntities = 1000,
                ProcessedEntities = 755,
                FailedEntities = 5,
                CurrentEntityType = "products"
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Timestamp.Should().Be(_testTimestamp);
            result.IsCancelled.Should().BeFalse();
            result.HubMethod.Should().Be("MigrationProgressUpdated");
            result.EventType.Should().Be("progress");
            result.OverallProgress.Should().Be(75.5);
            result.Status.Should().Be("processing");
            result.TotalEntities.Should().Be(1000);
            result.ProcessedEntities.Should().Be(755);
            result.FailedEntities.Should().Be(5);
            result.SuccessfulEntities.Should().Be(750); // 🎯 SUCCESS RATE FIX: ProcessedEntities (755) - FailedEntities (5) = 750
            result.CurrentEntityType.Should().Be("products");
            
            // 🎯 TIME TRACKING FIX: Should have null time properties when not provided
            result.StartTime.Should().BeNull("because StartTime was not provided");
            result.ElapsedTime.Should().BeNull("because StartTime was not provided for auto-calculation");
            result.EntitiesPerSecond.Should().BeNull("because insufficient data for calculation");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldAutoCalculateSuccessfulEntities_WhenNotProvided()
        {
            // Arrange - Don't set SuccessfulEntities explicitly
            var options = new MigrationProgressOptions
            {
                OverallProgress = 80.0,
                Status = "processing",
                TotalEntities = 500,
                ProcessedEntities = 400,
                FailedEntities = 25
                // SuccessfulEntities not set - should be auto-calculated
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.SuccessfulEntities.Should().Be(375, "because SuccessfulEntities should be auto-calculated as ProcessedEntities (400) - FailedEntities (25) = 375");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldUseProvidedSuccessfulEntities_WhenExplicitlySet()
        {
            // Arrange - Explicitly set SuccessfulEntities
            var options = new MigrationProgressOptions
            {
                OverallProgress = 80.0,
                Status = "processing",
                TotalEntities = 500,
                ProcessedEntities = 400,
                FailedEntities = 25,
                SuccessfulEntities = 300 // Explicitly set (different from auto-calculated value)
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.SuccessfulEntities.Should().Be(300, "because explicitly provided SuccessfulEntities should be used instead of auto-calculation");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldHandleNegativeCalculation_WhenFailedEntitiesExceedProcessed()
        {
            // Arrange - Edge case where calculation might be negative
            var options = new MigrationProgressOptions
            {
                OverallProgress = 50.0,
                Status = "processing",
                TotalEntities = 100,
                ProcessedEntities = 10,
                FailedEntities = 15 // More failed than processed (edge case)
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.SuccessfulEntities.Should().Be(0, "because Math.Max ensures SuccessfulEntities cannot be negative");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldAutoCalculateElapsedTime_WhenStartTimeProvided()
        {
            // Arrange - Provide StartTime but not ElapsedTime
            var startTime = _testTimestamp.AddMinutes(-5); // 5 minutes ago
            var options = new MigrationProgressOptions
            {
                OverallProgress = 60.0,
                Status = "processing",
                TotalEntities = 1000,
                ProcessedEntities = 600,
                FailedEntities = 10,
                StartTime = startTime
                // ElapsedTime not set - should be auto-calculated
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.StartTime.Should().Be(startTime);
            result.ElapsedTime.Should().Be(_testTimestamp - startTime, "because ElapsedTime should be auto-calculated from StartTime");
            result.EntitiesPerSecond.Should().BeApproximately(2.0, 0.01, "because 600 entities / 300 seconds = 2 entities/second");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldUseProvidedTimeProperties_WhenExplicitlySet()
        {
            // Arrange - Explicitly set all time properties
            var startTime = _testTimestamp.AddHours(-1);
            var elapsedTime = TimeSpan.FromMinutes(45);
            var entitiesPerSecond = 5.5;
            
            var options = new MigrationProgressOptions
            {
                OverallProgress = 70.0,
                Status = "processing",
                TotalEntities = 1000,
                ProcessedEntities = 700,
                FailedEntities = 15,
                StartTime = startTime,
                ElapsedTime = elapsedTime,
                EntitiesPerSecond = entitiesPerSecond
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.StartTime.Should().Be(startTime, "because explicitly provided StartTime should be used");
            result.ElapsedTime.Should().Be(elapsedTime, "because explicitly provided ElapsedTime should be used instead of auto-calculation");
            result.EntitiesPerSecond.Should().Be(entitiesPerSecond, "because explicitly provided EntitiesPerSecond should be used instead of auto-calculation");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldReturnNullEntitiesPerSecond_WhenInsufficientDataForCalculation()
        {
            // Arrange - No time data and no processed entities
            var options = new MigrationProgressOptions
            {
                OverallProgress = 0.0,
                Status = "starting",
                TotalEntities = 1000,
                ProcessedEntities = 0, // No entities processed yet
                FailedEntities = 0
                // No StartTime or ElapsedTime provided
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.EntitiesPerSecond.Should().BeNull("because calculation requires both time elapsed and processed entities > 0");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldHandleZeroElapsedTime_WhenCalculatingEntitiesPerSecond()
        {
            // Arrange - StartTime same as current time (zero elapsed)
            var options = new MigrationProgressOptions
            {
                OverallProgress = 50.0,
                Status = "processing",
                TotalEntities = 1000,
                ProcessedEntities = 500,
                FailedEntities = 10,
                StartTime = _testTimestamp, // Same as current time = 0 elapsed
                ElapsedTime = TimeSpan.Zero
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.EntitiesPerSecond.Should().BeNull("because division by zero should be avoided when elapsed time is zero");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldCalculateCorrectEntitiesPerSecond_WithElapsedTimeOnly()
        {
            // Arrange - Provide ElapsedTime but not StartTime
            var elapsedTime = TimeSpan.FromMinutes(10); // 600 seconds
            var options = new MigrationProgressOptions
            {
                OverallProgress = 80.0,
                Status = "processing",
                TotalEntities = 1000,
                ProcessedEntities = 800,
                FailedEntities = 20,
                ElapsedTime = elapsedTime
                // StartTime not provided
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.ElapsedTime.Should().Be(elapsedTime);
            result.EntitiesPerSecond.Should().BeApproximately(1.333, 0.01, "because 800 entities / 600 seconds = 1.333 entities/second");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldCalculateEstimatedTimeRemaining_WhenEntitiesPerSecondProvided()
        {
            // Arrange - Migration with 1000 total entities, 400 processed, 2 entities/second
            var options = new MigrationProgressOptions
            {
                OverallProgress = 40.0,
                Status = "processing",
                TotalEntities = 1000,
                ProcessedEntities = 400,
                FailedEntities = 10,
                EntitiesPerSecond = 2.0 // 2 entities processed per second
                // EstimatedTimeRemaining should be auto-calculated: (1000-400) / 2.0 = 300 seconds
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.EstimatedTimeRemaining.Should().Be(TimeSpan.FromSeconds(300), 
                "because (1000 total - 400 processed) / 2.0 entities/second = 300 seconds remaining");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldUseProvidedEstimatedTimeRemaining_WhenExplicitlySet()
        {
            // Arrange - Explicitly set EstimatedTimeRemaining 
            var explicitTimeRemaining = TimeSpan.FromMinutes(45);
            var options = new MigrationProgressOptions
            {
                OverallProgress = 60.0,
                Status = "processing",
                TotalEntities = 1000,
                ProcessedEntities = 600,
                FailedEntities = 15,
                EntitiesPerSecond = 5.0,
                EstimatedTimeRemaining = explicitTimeRemaining // Explicit value should override calculation
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.EstimatedTimeRemaining.Should().Be(explicitTimeRemaining, 
                "because explicitly provided EstimatedTimeRemaining should be used instead of auto-calculation");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldReturnZeroEstimatedTime_WhenNoEntitiesRemaining()
        {
            // Arrange - Migration completed (all entities processed)
            var options = new MigrationProgressOptions
            {
                OverallProgress = 100.0,
                Status = "completed",
                TotalEntities = 500,
                ProcessedEntities = 500, // All entities processed
                FailedEntities = 25,
                EntitiesPerSecond = 3.5
                // Should return TimeSpan.Zero because no entities remain
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.EstimatedTimeRemaining.Should().Be(TimeSpan.Zero, 
                "because when all entities are processed, no time should remain");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldReturnNullEstimatedTime_WhenEntitiesPerSecondIsZero()
        {
            // Arrange - No processing speed available
            var options = new MigrationProgressOptions
            {
                OverallProgress = 25.0,
                Status = "processing",
                TotalEntities = 800,
                ProcessedEntities = 200,
                FailedEntities = 5,
                EntitiesPerSecond = 0.0 // No processing speed
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.EstimatedTimeRemaining.Should().BeNull(
                "because when processing speed is zero, time estimation is impossible");
        }

        [Fact]
        public void CreateMigrationProgress_ShouldPopulateCurrentBatchDetails_WhenProvided()
        {
            // Arrange - With CurrentBatch details
            var currentBatch = new CurrentBatchDetails
            {
                BatchNumber = 3,
                BatchSize = 50,
                ProcessedInBatch = 25,
                BatchProgressPercentage = 50.0,
                BatchProcessingSpeed = 2.5,
                BatchElapsedTime = TimeSpan.FromMinutes(2),
                EstimatedBatchTimeRemaining = TimeSpan.FromMinutes(2)
            };

            var options = new MigrationProgressOptions
            {
                OverallProgress = 60.0,
                Status = "processing",
                TotalEntities = 500,
                ProcessedEntities = 300,
                FailedEntities = 5,
                CurrentBatchNumber = 3,
                CurrentActivity = "Processing entities",
                CurrentBatch = currentBatch
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.CurrentBatchNumber.Should().Be(3);
            result.CurrentActivity.Should().Be("Processing entities");
            
            result.CurrentBatch.Should().NotBeNull();
            result.CurrentBatch!.BatchNumber.Should().Be(3);
            result.CurrentBatch.BatchSize.Should().Be(50);
            result.CurrentBatch.ProcessedInBatch.Should().Be(25);
            result.CurrentBatch.BatchProgressPercentage.Should().Be(50.0);
            result.CurrentBatch.BatchProcessingSpeed.Should().Be(2.5);
            result.CurrentBatch.BatchElapsedTime.Should().Be(TimeSpan.FromMinutes(2));
            result.CurrentBatch.EstimatedBatchTimeRemaining.Should().Be(TimeSpan.FromMinutes(2));
        }

        [Fact]
        public void CreateMigrationProgress_ShouldHaveNullCurrentBatch_WhenNotProvided()
        {
            // Arrange - Without CurrentBatch details
            var options = new MigrationProgressOptions
            {
                OverallProgress = 60.0,
                Status = "processing",
                TotalEntities = 500,
                ProcessedEntities = 300,
                FailedEntities = 5
                // No CurrentBatch, CurrentBatchNumber, or CurrentActivity
            };

            // Act
            var result = _factory.CreateMigrationProgress(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.CurrentBatchNumber.Should().BeNull();
            result.CurrentActivity.Should().BeNull();
            result.CurrentBatch.Should().BeNull();
        }

        [Fact]
        public void CreateMigrationProgress_WithNullMigrationId_ShouldThrowArgumentException()
        {
            // Arrange
            var options = new MigrationProgressOptions { OverallProgress = 50.0 };

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => _factory.CreateMigrationProgress(null!, options));
            exception.Message.Should().Contain("MigrationId cannot be null or empty");
        }

        [Fact]
        public void CreateMigrationProgress_WithNullOptions_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => _factory.CreateMigrationProgress(TestMigrationId, null!));
            exception.ParamName.Should().Be("options");
        }

        #endregion

        #region CreateBatchProgress Tests

        [Fact]
        public void CreateBatchProgress_WithValidOptions_ShouldCreateEventWithCorrectProperties()
        {
            // Arrange
            var options = new BatchProgressOptions
            {
                BatchNumber = 5,
                TotalBatches = 20,
                BatchSize = 100,
                ProcessedCount = 95,
                FailedCount = 5,
                Status = "completed",
                EntityType = "categories"
            };

            // Act
            var result = _factory.CreateBatchProgress(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Timestamp.Should().Be(_testTimestamp);
            result.HubMethod.Should().Be("BatchProgressUpdated");
            result.EventType.Should().Be("batch");
            result.BatchNumber.Should().Be(5);
            result.TotalBatches.Should().Be(20);
            result.BatchSize.Should().Be(100);
            result.ProcessedCount.Should().Be(95);
            result.FailedCount.Should().Be(5);
            result.Status.Should().Be("completed");
            result.EntityType.Should().Be("categories");
        }

        #endregion

        #region CreateEntityProgress Tests

        [Fact]
        public void CreateEntityProgress_WithValidOptions_ShouldCreateEventWithCorrectProperties()
        {
            // Arrange
            var options = new EntityProgressOptions
            {
                EntityType = "products",
                EntityId = "prod-123",
                Status = "completed",
                ProcessedCount = 100,
                TotalCount = 150
            };

            // Act
            var result = _factory.CreateEntityProgress(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Timestamp.Should().Be(_testTimestamp);
            result.HubMethod.Should().Be("EntityProgressUpdated");
            result.EventType.Should().Be("entity");
            result.EntityType.Should().Be("products");
            result.ProcessedCount.Should().Be(100);
            result.TotalCount.Should().Be(150);
            result.Status.Should().Be("completed");
        }

        #endregion

        #region CreateErrorProgress Tests

        [Fact]
        public void CreateErrorProgress_WithValidOptions_ShouldCreateEventWithCorrectProperties()
        {
            // Arrange
            var options = new ErrorProgressOptions
            {
                ErrorMessage = "API rate limit exceeded",
                Severity = "warning",
                EntityType = "products",
                EntityId = "prod-456"
            };

            // Act
            var result = _factory.CreateErrorProgress(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Timestamp.Should().Be(_testTimestamp);
            result.HubMethod.Should().Be("ErrorOccurred");
            result.EventType.Should().Be("error");
            result.Severity.Should().Be("warning");
            result.Message.Should().Be("API rate limit exceeded");
            result.EntityType.Should().Be("products");
            result.EntityId.Should().Be("prod-456");
        }

        #endregion

        #region CreateStatusProgress Tests

        [Fact]
        public void CreateStatusProgress_WithValidOptions_ShouldCreateEventWithCorrectProperties()
        {
            // Arrange
            var options = new StatusProgressOptions
            {
                Status = "completed",
                Message = "Migration completed successfully"
            };

            // Act
            var result = _factory.CreateStatusProgress(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Timestamp.Should().Be(_testTimestamp);
            result.HubMethod.Should().Be("MigrationStatusChanged");
            result.EventType.Should().Be("status");
            result.Status.Should().Be("completed");
            result.Message.Should().Be("Migration completed successfully");
        }

        #endregion

        #region CreateSubBatchStarted Tests

        [Fact]
        public void CreateSubBatchStarted_WithValidOptions_ShouldCreateEventWithCorrectProperties()
        {
            // Arrange
            var options = new SubBatchStartedOptions
            {
                ParentBatchNumber = 3,
                SubBatchNumber = 2,
                TotalSubBatches = 5,
                EntityType = "products"
            };

            // Act
            var result = _factory.CreateSubBatchStarted(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Timestamp.Should().Be(_testTimestamp);
            result.HubMethod.Should().Be("SubBatchStarted");
            result.EventType.Should().Be("subbatch-started");
            result.ParentBatchNumber.Should().Be(3);
            result.SubBatchNumber.Should().Be(2);
            result.TotalSubBatches.Should().Be(5);
            result.EntityType.Should().Be("products");
        }

        #endregion

        #region CreateSubBatchCompleted Tests

        [Fact]
        public void CreateSubBatchCompleted_WithValidOptions_ShouldCreateEventWithCorrectProperties()
        {
            // Arrange
            var options = new SubBatchCompletedOptions
            {
                ParentBatchNumber = 3,
                SubBatchNumber = 2,
                TotalSubBatches = 5,
                SuccessfulEntities = 45,
                FailedEntities = 5,
                TotalEntities = 50,
                EntityType = "products"
            };

            // Act
            var result = _factory.CreateSubBatchCompleted(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Timestamp.Should().Be(_testTimestamp);
            result.HubMethod.Should().Be("SubBatchCompleted");
            result.EventType.Should().Be("subbatch-completed");
            result.ParentBatchNumber.Should().Be(3);
            result.SubBatchNumber.Should().Be(2);
            result.TotalSubBatches.Should().Be(5);
            result.SuccessfulEntities.Should().Be(45);
            result.FailedEntities.Should().Be(5);
            result.TotalEntities.Should().Be(50);
            result.EntityType.Should().Be("products");
        }

        #endregion

        #region CreateSubBatchProgress Tests

        [Fact]
        public void CreateSubBatchProgress_WithValidOptions_ShouldCreateEventWithCorrectProperties()
        {
            // Arrange
            var options = new SubBatchProgressOptions
            {
                TotalPages = 10,
                CompletedPages = 7,
                TotalSubBatches = 5,
                CompletedSubBatches = 3,
                TotalSuccessfulEntities = 450,
                TotalFailedEntities = 25,
                TotalExpectedEntities = 500,
                ProcessingRate = 15.5,
                OverallProgressPercentage = 70.0
            };

            // Act
            var result = _factory.CreateSubBatchProgress(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Timestamp.Should().Be(_testTimestamp);
            result.HubMethod.Should().Be("SubBatchMigrationProgress");
            result.EventType.Should().Be("subbatch-progress");
            result.TotalPages.Should().Be(10);
            result.CompletedPages.Should().Be(7);
            result.TotalSubBatches.Should().Be(5);
            result.CompletedSubBatches.Should().Be(3);
            result.TotalSuccessfulEntities.Should().Be(450);
            result.TotalFailedEntities.Should().Be(25);
            result.TotalExpectedEntities.Should().Be(500);
            result.ProcessingRate.Should().Be(15.5);
            result.OverallProgressPercentage.Should().Be(70.0);
        }

        #endregion

        #region CreateCancellationProgress Tests

        [Fact]
        public void CreateCancellationProgress_WithValidOptions_ShouldCreateEventWithCorrectProperties()
        {
            // Arrange
            var options = new CancellationProgressOptions
            {
                Scope = CancellationScope.Migration,
                Status = "propagating",
                Reason = "User requested cancellation",
                EntityType = "products",
                BatchId = "batch-123",
                StoreId = "store-456",
                EstimatedTimeToComplete = 30,
                RequestedBy = "user@example.com",
                TotalInstances = 3,
                AcknowledgedInstances = 1
            };

            // Act
            var result = _factory.CreateCancellationProgress(TestMigrationId, options);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Timestamp.Should().Be(_testTimestamp);
            result.HubMethod.Should().Be("CancellationProgressUpdated");
            result.EventType.Should().Be("cancellation-progress");
            result.Scope.Should().Be(CancellationScope.Migration);
            result.Status.Should().Be("propagating");
            result.Reason.Should().Be("User requested cancellation");
            result.EntityType.Should().Be("products");
            result.BatchId.Should().Be("batch-123");
            result.StoreId.Should().Be("store-456");
            result.EstimatedTimeToComplete.Should().Be(30);
            result.RequestedBy.Should().Be("user@example.com");
            result.TotalInstances.Should().Be(3);
            result.AcknowledgedInstances.Should().Be(1);
            result.AdditionalContext.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void CreateCancellationProgress_ShouldNotSetBaseClassCancellationProperties()
        {
            // Arrange - This test ensures we don't set conflicting base class properties
            var options = new CancellationProgressOptions
            {
                Scope = CancellationScope.EntityType,
                Reason = "Test cancellation reason",
                EntityType = "brands"
            };

            // Act
            var result = _factory.CreateCancellationProgress(TestMigrationId, options);

            // Assert - Base class cancellation properties should NOT be set to avoid JSON conflicts
            result.IsCancelled.Should().BeFalse("because base class IsCancelled should not be set for CancellationProgressEvent");
            result.CancellationReason.Should().BeNull("because base class CancellationReason should not be set for CancellationProgressEvent");
            result.CancelledAt.Should().BeNull("because base class CancelledAt should not be set for CancellationProgressEvent");
            
            // But the specific CancellationProgressEvent properties should be set
            result.Reason.Should().Be("Test cancellation reason");
            result.Scope.Should().Be(CancellationScope.EntityType);
        }

        [Fact]
        public void CreateCancellationProgress_ShouldSerializeAndDeserializeWithoutConflicts()
        {
            // Arrange - This is a round-trip test to catch property conflicts
            var options = new CancellationProgressOptions
            {
                Scope = CancellationScope.Batch,
                Status = "completed",
                Reason = "Batch cancellation test",
                BatchId = "test-batch-456"
            };

            // Act - Create the event
            var cancellationEvent = _factory.CreateCancellationProgress(TestMigrationId, options);
            
            // Serialize to JSON (what actually goes to the queue)
            var json = JsonSerializer.Serialize(cancellationEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Deserialize back (what ProcessProgressEvents does)
            var deserializedEvent = JsonSerializer.Deserialize<CancellationProgressEvent>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Assert - Should deserialize successfully without conflicts
            deserializedEvent.Should().NotBeNull();
            deserializedEvent!.MigrationId.Should().Be(TestMigrationId);
            deserializedEvent.EventType.Should().Be("cancellation-progress");
            deserializedEvent.HubMethod.Should().Be("CancellationProgressUpdated");
            deserializedEvent.Scope.Should().Be(CancellationScope.Batch);
            deserializedEvent.Status.Should().Be("completed");
            deserializedEvent.Reason.Should().Be("Batch cancellation test");
            deserializedEvent.BatchId.Should().Be("test-batch-456");
        }

        [Fact]
        public void CreateCancellationProgress_WithNullReason_ShouldThrowArgumentException()
        {
            // Arrange
            var options = new CancellationProgressOptions
            {
                Scope = CancellationScope.Migration,
                Reason = null! // Reason is required
            };

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => _factory.CreateCancellationProgress(TestMigrationId, options));
            exception.Message.Should().Contain("Reason is required for cancellation events");
        }

        [Fact]
        public void CreateCancellationProgress_WithMinimalOptions_ShouldUseDefaults()
        {
            // Arrange - Only provide required properties
            var options = new CancellationProgressOptions
            {
                Scope = CancellationScope.Store,
                Reason = "Minimal test"
            };

            // Act
            var result = _factory.CreateCancellationProgress(TestMigrationId, options);

            // Assert - Should use appropriate defaults
            result.Status.Should().Be("requested", "because default status should be 'requested'");
            result.EntityType.Should().BeNull();
            result.BatchId.Should().BeNull();
            result.StoreId.Should().BeNull();
            result.EstimatedTimeToComplete.Should().BeNull();
            result.RequestedBy.Should().BeNull();
            result.TotalInstances.Should().BeNull();
            result.AcknowledgedInstances.Should().BeNull();
            result.AdditionalContext.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void CreateCancellationProgress_WithNullMigrationId_ShouldThrowArgumentException()
        {
            // Arrange
            var options = new CancellationProgressOptions
            {
                Scope = CancellationScope.Migration,
                Reason = "Test reason"
            };

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => _factory.CreateCancellationProgress(null!, options));
            exception.Message.Should().Contain("MigrationId cannot be null or empty");
        }

        [Fact]
        public void CreateCancellationProgress_WithNullOptions_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => _factory.CreateCancellationProgress(TestMigrationId, null!));
            exception.ParamName.Should().Be("options");
        }

        #endregion

        #region Base Properties Tests

        [Fact]
        public void AllFactoryMethods_ShouldSetTimestampFromDateTimeProvider()
        {
            // Arrange
            var migrationOptions = new MigrationProgressOptions { OverallProgress = 50.0 };
            var batchOptions = new BatchProgressOptions { BatchNumber = 1, TotalBatches = 10, EntityType = "categories" };

            // Act
            var migrationEvent = _factory.CreateMigrationProgress(TestMigrationId, migrationOptions);
            var batchEvent = _factory.CreateBatchProgress(TestMigrationId, batchOptions);

            // Assert
            migrationEvent.Timestamp.Should().Be(_testTimestamp);
            batchEvent.Timestamp.Should().Be(_testTimestamp);
            _mockDateTimeProvider.Verify(x => x.UtcNow, Times.AtLeast(2));
        }

        [Fact]
        public void AllFactoryMethods_ShouldSetMigrationIdFromParameter()
        {
            // Arrange
            const string customMigrationId = "custom-migration-xyz";
            var migrationOptions = new MigrationProgressOptions { OverallProgress = 50.0 };

            // Act
            var result = _factory.CreateMigrationProgress(customMigrationId, migrationOptions);

            // Assert
            result.MigrationId.Should().Be(customMigrationId);
        }

        #endregion
    }
} 