using System;
using Xunit;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.Core.Models
{
    /// <summary>
    /// Unit tests for ProgressEvent models
    /// Tests follow TDD principles and ensure all components are unit testable
    /// </summary>
    public class ProgressEventTests
    {
        #region MigrationProgressEvent Tests

        [Fact]
        public void MigrationProgressEvent_Constructor_ShouldSetDefaultValues()
        {
            // Act
            var progressEvent = new MigrationProgressEvent();

            // Assert
            Assert.Equal("progress", progressEvent.EventType);
            Assert.Equal("MigrationProgressUpdated", progressEvent.HubMethod);
            Assert.Equal(string.Empty, progressEvent.MigrationId);
            Assert.Equal(string.Empty, progressEvent.Status);
            Assert.Equal(0, progressEvent.OverallProgress);
            Assert.Equal(0, progressEvent.TotalEntities);
            Assert.Equal(0, progressEvent.ProcessedEntities);
            Assert.Equal(0, progressEvent.FailedEntities);
            Assert.Null(progressEvent.CurrentEntityType);
            Assert.Null(progressEvent.EstimatedTimeRemaining);
            Assert.True(progressEvent.Timestamp > DateTime.MinValue);
        }

        [Fact]
        public void MigrationProgressEvent_Properties_ShouldAcceptValidValues()
        {
            // Arrange
            var migrationId = "test-migration-123";
            var status = "running";
            var progress = 75.5;
            var totalEntities = 1000;
            var processedEntities = 755;
            var failedEntities = 10;
            var entityType = "products";
            var estimatedTime = TimeSpan.FromMinutes(30);

            // Act
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = migrationId,
                Status = status,
                OverallProgress = progress,
                TotalEntities = totalEntities,
                ProcessedEntities = processedEntities,
                FailedEntities = failedEntities,
                CurrentEntityType = entityType,
                EstimatedTimeRemaining = estimatedTime
            };

            // Assert
            Assert.Equal(migrationId, progressEvent.MigrationId);
            Assert.Equal(status, progressEvent.Status);
            Assert.Equal(progress, progressEvent.OverallProgress);
            Assert.Equal(totalEntities, progressEvent.TotalEntities);
            Assert.Equal(processedEntities, progressEvent.ProcessedEntities);
            Assert.Equal(failedEntities, progressEvent.FailedEntities);
            Assert.Equal(entityType, progressEvent.CurrentEntityType);
            Assert.Equal(estimatedTime, progressEvent.EstimatedTimeRemaining);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(50, 50)]
        [InlineData(100, 100)]
        [InlineData(150, 150)] // Can exceed 100% for edge cases
        public void MigrationProgressEvent_OverallProgress_ShouldAcceptValidPercentages(double input, double expected)
        {
            // Act
            var progressEvent = new MigrationProgressEvent { OverallProgress = input };

            // Assert
            Assert.Equal(expected, progressEvent.OverallProgress);
        }

        #endregion

        #region BatchProgressEvent Tests

        [Fact]
        public void BatchProgressEvent_Constructor_ShouldSetDefaultValues()
        {
            // Act
            var batchEvent = new BatchProgressEvent();

            // Assert
            Assert.Equal("batch", batchEvent.EventType);
            Assert.Equal("BatchProgressUpdated", batchEvent.HubMethod);
            Assert.Equal(string.Empty, batchEvent.EntityType);
            Assert.Equal(0, batchEvent.BatchNumber);
            Assert.Equal(0, batchEvent.TotalBatches);
            Assert.Equal(0, batchEvent.BatchSize);
            Assert.Equal(0, batchEvent.ProcessedCount);
            Assert.Equal(0, batchEvent.FailedCount);
            Assert.Equal(string.Empty, batchEvent.Status);
            Assert.Null(batchEvent.ProcessingTime);
        }

        [Fact]
        public void BatchProgressEvent_Properties_ShouldAcceptValidValues()
        {
            // Arrange
            var entityType = "categories";
            var batchNumber = 3;
            var totalBatches = 10;
            var batchSize = 25;
            var processedCount = 22;
            var failedCount = 3;
            var status = "completed";
            var processingTime = TimeSpan.FromSeconds(45);

            // Act
            var batchEvent = new BatchProgressEvent
            {
                EntityType = entityType,
                BatchNumber = batchNumber,
                TotalBatches = totalBatches,
                BatchSize = batchSize,
                ProcessedCount = processedCount,
                FailedCount = failedCount,
                Status = status,
                ProcessingTime = processingTime
            };

            // Assert
            Assert.Equal(entityType, batchEvent.EntityType);
            Assert.Equal(batchNumber, batchEvent.BatchNumber);
            Assert.Equal(totalBatches, batchEvent.TotalBatches);
            Assert.Equal(batchSize, batchEvent.BatchSize);
            Assert.Equal(processedCount, batchEvent.ProcessedCount);
            Assert.Equal(failedCount, batchEvent.FailedCount);
            Assert.Equal(status, batchEvent.Status);
            Assert.Equal(processingTime, batchEvent.ProcessingTime);
        }

        [Theory]
        [InlineData("starting")]
        [InlineData("processing")]
        [InlineData("completed")]
        [InlineData("failed")]
        public void BatchProgressEvent_Status_ShouldAcceptValidStatuses(string status)
        {
            // Act
            var batchEvent = new BatchProgressEvent { Status = status };

            // Assert
            Assert.Equal(status, batchEvent.Status);
        }

        #endregion

        #region EntityProgressEvent Tests

        [Fact]
        public void EntityProgressEvent_Constructor_ShouldSetDefaultValues()
        {
            // Act
            var entityEvent = new EntityProgressEvent();

            // Assert
            Assert.Equal("entity", entityEvent.EventType);
            Assert.Equal("EntityProgressUpdated", entityEvent.HubMethod);
            Assert.Equal(string.Empty, entityEvent.EntityType);
            Assert.Equal(0, entityEvent.TotalCount);
            Assert.Equal(0, entityEvent.ProcessedCount);
            Assert.Equal(0, entityEvent.SuccessCount);
            Assert.Equal(0, entityEvent.FailureCount);
            Assert.Equal(string.Empty, entityEvent.Status);
            Assert.Null(entityEvent.ProcessingTime);
        }

        [Fact]
        public void EntityProgressEvent_Progress_ShouldCalculateCorrectPercentage()
        {
            // Arrange
            var entityEvent = new EntityProgressEvent
            {
                TotalCount = 100,
                ProcessedCount = 75
            };

            // Act & Assert
            Assert.Equal(75.0, entityEvent.Progress);
        }

        [Fact]
        public void EntityProgressEvent_Progress_WithZeroTotal_ShouldReturnZero()
        {
            // Arrange
            var entityEvent = new EntityProgressEvent
            {
                TotalCount = 0,
                ProcessedCount = 10
            };

            // Act & Assert
            Assert.Equal(0.0, entityEvent.Progress);
        }

        [Theory]
        [InlineData(100, 25, 25.0)]
        [InlineData(100, 50, 50.0)]
        [InlineData(100, 100, 100.0)]
        [InlineData(200, 150, 75.0)]
        [InlineData(1, 1, 100.0)]
        public void EntityProgressEvent_Progress_ShouldCalculateCorrectly(int total, int processed, double expected)
        {
            // Arrange
            var entityEvent = new EntityProgressEvent
            {
                TotalCount = total,
                ProcessedCount = processed
            };

            // Act & Assert
            Assert.Equal(expected, entityEvent.Progress);
        }

        [Fact]
        public void EntityProgressEvent_Properties_ShouldAcceptValidValues()
        {
            // Arrange
            var entityType = "brands";
            var totalCount = 500;
            var processedCount = 450;
            var successCount = 440;
            var failureCount = 10;
            var status = "processing";
            var processingTime = TimeSpan.FromMinutes(5);

            // Act
            var entityEvent = new EntityProgressEvent
            {
                EntityType = entityType,
                TotalCount = totalCount,
                ProcessedCount = processedCount,
                SuccessCount = successCount,
                FailureCount = failureCount,
                Status = status,
                ProcessingTime = processingTime
            };

            // Assert
            Assert.Equal(entityType, entityEvent.EntityType);
            Assert.Equal(totalCount, entityEvent.TotalCount);
            Assert.Equal(processedCount, entityEvent.ProcessedCount);
            Assert.Equal(successCount, entityEvent.SuccessCount);
            Assert.Equal(failureCount, entityEvent.FailureCount);
            Assert.Equal(status, entityEvent.Status);
            Assert.Equal(processingTime, entityEvent.ProcessingTime);
        }

        #endregion

        #region ErrorProgressEvent Tests

        [Fact]
        public void ErrorProgressEvent_Constructor_ShouldSetDefaultValues()
        {
            // Act
            var errorEvent = new ErrorProgressEvent();

            // Assert
            Assert.Equal("error", errorEvent.EventType);
            Assert.Equal("ErrorOccurred", errorEvent.HubMethod);
            Assert.Equal("error", errorEvent.Severity);
            Assert.Equal(string.Empty, errorEvent.Message);
            Assert.Null(errorEvent.EntityType);
            Assert.Null(errorEvent.EntityId);
            Assert.Null(errorEvent.BatchNumber);
            Assert.Null(errorEvent.Details);
            Assert.True(errorEvent.IsContinuable);
        }

        [Fact]
        public void ErrorProgressEvent_Properties_ShouldAcceptValidValues()
        {
            // Arrange
            var severity = "critical";
            var message = "Database connection failed";
            var entityType = "products";
            var entityId = "product-123";
            var batchNumber = 5;
            var details = "Connection timeout after 30 seconds";
            var isContinuable = false;

            // Act
            var errorEvent = new ErrorProgressEvent
            {
                Severity = severity,
                Message = message,
                EntityType = entityType,
                EntityId = entityId,
                BatchNumber = batchNumber,
                Details = details,
                IsContinuable = isContinuable
            };

            // Assert
            Assert.Equal(severity, errorEvent.Severity);
            Assert.Equal(message, errorEvent.Message);
            Assert.Equal(entityType, errorEvent.EntityType);
            Assert.Equal(entityId, errorEvent.EntityId);
            Assert.Equal(batchNumber, errorEvent.BatchNumber);
            Assert.Equal(details, errorEvent.Details);
            Assert.Equal(isContinuable, errorEvent.IsContinuable);
        }

        [Theory]
        [InlineData("warning")]
        [InlineData("error")]
        [InlineData("critical")]
        public void ErrorProgressEvent_Severity_ShouldAcceptValidLevels(string severity)
        {
            // Act
            var errorEvent = new ErrorProgressEvent { Severity = severity };

            // Assert
            Assert.Equal(severity, errorEvent.Severity);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ErrorProgressEvent_IsContinuable_ShouldAcceptBooleanValues(bool isContinuable)
        {
            // Act
            var errorEvent = new ErrorProgressEvent { IsContinuable = isContinuable };

            // Assert
            Assert.Equal(isContinuable, errorEvent.IsContinuable);
        }

        #endregion

        #region StatusProgressEvent Tests

        [Fact]
        public void StatusProgressEvent_Constructor_ShouldSetDefaultValues()
        {
            // Act
            var statusEvent = new StatusProgressEvent();

            // Assert
            Assert.Equal("status", statusEvent.EventType);
            Assert.Equal("MigrationStatusChanged", statusEvent.HubMethod);
            Assert.Equal(string.Empty, statusEvent.Status);
            Assert.Equal(string.Empty, statusEvent.Message);
            Assert.Null(statusEvent.Metadata);
        }

        [Fact]
        public void StatusProgressEvent_Properties_ShouldAcceptValidValues()
        {
            // Arrange
            var status = "completed";
            var message = "Migration completed successfully";
            var metadata = new { TotalEntities = 1000, Duration = "00:15:30" };

            // Act
            var statusEvent = new StatusProgressEvent
            {
                Status = status,
                Message = message,
                Metadata = metadata
            };

            // Assert
            Assert.Equal(status, statusEvent.Status);
            Assert.Equal(message, statusEvent.Message);
            Assert.Equal(metadata, statusEvent.Metadata);
        }

        [Theory]
        [InlineData("started")]
        [InlineData("running")]
        [InlineData("completed")]
        [InlineData("failed")]
        [InlineData("cancelled")]
        public void StatusProgressEvent_Status_ShouldAcceptValidStatuses(string status)
        {
            // Act
            var statusEvent = new StatusProgressEvent { Status = status };

            // Assert
            Assert.Equal(status, statusEvent.Status);
        }

        #endregion

        #region Base ProgressEvent Tests

        [Fact]
        public void ProgressEvent_BaseProperties_ShouldHaveCorrectDefaults()
        {
            // Act
            var progressEvent = new MigrationProgressEvent(); // Using concrete implementation

            // Assert
            Assert.Equal(string.Empty, progressEvent.MigrationId);
            Assert.Equal("progress", progressEvent.EventType);
            Assert.Equal("MigrationProgressUpdated", progressEvent.HubMethod); // Concrete class sets specific hub method
            Assert.Null(progressEvent.ConnectionId);
            Assert.Null(progressEvent.GroupName);
            Assert.True(progressEvent.Timestamp > DateTime.MinValue);
            Assert.True(progressEvent.Timestamp <= DateTime.UtcNow);
        }

        [Fact]
        public void ProgressEvent_BaseProperties_ShouldAcceptValidValues()
        {
            // Arrange
            var migrationId = "migration-456";
            var connectionId = "connection-789";
            var groupName = "migration-group";
            var timestamp = DateTime.UtcNow.AddMinutes(-5);

            // Act
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = migrationId,
                ConnectionId = connectionId,
                GroupName = groupName,
                Timestamp = timestamp
            };

            // Assert
            Assert.Equal(migrationId, progressEvent.MigrationId);
            Assert.Equal(connectionId, progressEvent.ConnectionId);
            Assert.Equal(groupName, progressEvent.GroupName);
            Assert.Equal(timestamp, progressEvent.Timestamp);
        }

        [Fact]
        public void ProgressEvent_Timestamp_ShouldUseUtcTime()
        {
            // Act
            var progressEvent = new MigrationProgressEvent();
            var now = DateTime.UtcNow;

            // Assert
            Assert.True(progressEvent.Timestamp.Kind == DateTimeKind.Utc || progressEvent.Timestamp.Kind == DateTimeKind.Unspecified);
            Assert.True(Math.Abs((now - progressEvent.Timestamp).TotalSeconds) < 1); // Within 1 second
        }

        #endregion
    }
} 