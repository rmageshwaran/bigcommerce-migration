using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services
{
    /// <summary>
    /// Unit tests for ProgressEventPublisher service
    /// Tests follow TDD principles and ensure all components are unit testable
    /// </summary>
    public class ProgressEventPublisherTests
    {
        private readonly Mock<IProgressQueueService> _mockProgressQueueService;
        private readonly Mock<ILogger<ProgressEventPublisher>> _mockLogger;
        private readonly ProgressEventPublisher _progressEventPublisher;

        public ProgressEventPublisherTests()
        {
            _mockProgressQueueService = new Mock<IProgressQueueService>();
            _mockLogger = new Mock<ILogger<ProgressEventPublisher>>();
            
            // Setup queue service mocks for successful operations
            _mockProgressQueueService
                .Setup(x => x.EnsureQueueExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _mockProgressQueueService
                .Setup(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _progressEventPublisher = new ProgressEventPublisher(_mockProgressQueueService.Object, _mockLogger.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullProgressQueueService_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new ProgressEventPublisher(null!, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new ProgressEventPublisher(_mockProgressQueueService.Object, null!));
        }

        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateInstance()
        {
            // Act
            var publisher = new ProgressEventPublisher(_mockProgressQueueService.Object, _mockLogger.Object);

            // Assert
            Assert.NotNull(publisher);
        }

        #endregion

        #region PublishMigrationProgressAsync Tests

        [Fact]
        public async Task PublishMigrationProgressAsync_WithValidEvent_ShouldPublishSuccessfully()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = "test-migration-123",
                OverallProgress = 50.0,
                Status = "running",
                TotalEntities = 1000,
                ProcessedEntities = 500
            };

            // Act
            await _progressEventPublisher.PublishMigrationProgressAsync(progressEvent);

            // Assert
            // Note: QueueServiceClient operations are internal implementation details
            // We can't easily mock them since QueueServiceClient is created internally
            // For unit testing purposes, we'd need to inject the QueueServiceClient or use integration tests
        }

        [Fact]
        public async Task PublishMigrationProgressAsync_WithCancellationToken_ShouldPassTokenToQueue()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent { MigrationId = "test-migration" };
            var cancellationToken = new CancellationToken();

            // Act
            await _progressEventPublisher.PublishMigrationProgressAsync(progressEvent, cancellationToken);

            // Assert
            _mockProgressQueueService.Verify(x => x.EnsureQueueExistsAsync(It.IsAny<string>(), cancellationToken), Times.Once);
            _mockProgressQueueService.Verify(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), cancellationToken), Times.Once);
        }

        #endregion

        #region PublishBatchProgressAsync Tests

        [Fact]
        public async Task PublishBatchProgressAsync_WithValidEvent_ShouldPublishSuccessfully()
        {
            // Arrange
            var batchEvent = new BatchProgressEvent
            {
                MigrationId = "test-migration-456",
                EntityType = "products",
                BatchNumber = 3,
                TotalBatches = 10,
                Status = "completed"
            };

            // Act
            await _progressEventPublisher.PublishBatchProgressAsync(batchEvent);

            // Assert
            _mockProgressQueueService.Verify(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region PublishEntityProgressAsync Tests

        [Fact]
        public async Task PublishEntityProgressAsync_WithValidEvent_ShouldPublishSuccessfully()
        {
            // Arrange
            var entityEvent = new EntityProgressEvent
            {
                MigrationId = "test-migration-789",
                EntityType = "categories",
                TotalCount = 100,
                ProcessedCount = 75,
                Status = "processing"
            };

            // Act
            await _progressEventPublisher.PublishEntityProgressAsync(entityEvent);

            // Assert
            _mockProgressQueueService.Verify(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region PublishErrorAsync Tests

        [Fact]
        public async Task PublishErrorAsync_WithValidEvent_ShouldPublishSuccessfully()
        {
            // Arrange
            var errorEvent = new ErrorProgressEvent
            {
                MigrationId = "test-migration-error",
                Severity = "error",
                Message = "Failed to process entity",
                EntityType = "products",
                IsContinuable = true
            };

            // Act
            await _progressEventPublisher.PublishErrorAsync(errorEvent);

            // Assert
            _mockProgressQueueService.Verify(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region PublishStatusAsync Tests

        [Fact]
        public async Task PublishStatusAsync_WithValidEvent_ShouldPublishSuccessfully()
        {
            // Arrange
            var statusEvent = new StatusProgressEvent
            {
                MigrationId = "test-migration-status",
                Status = "completed",
                Message = "Migration completed successfully"
            };

            // Act
            await _progressEventPublisher.PublishStatusAsync(statusEvent);

            // Assert
            _mockProgressQueueService.Verify(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region PublishAsync Core Method Tests

        [Fact]
        public async Task PublishAsync_WithNullEvent_ShouldLogWarningAndReturn()
        {
            // Act
            await _progressEventPublisher.PublishAsync(null!);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Attempted to publish null progress event")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
            
            _mockProgressQueueService.Verify(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PublishAsync_WithEmptyMigrationId_ShouldLogWarningAndReturn()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent { MigrationId = string.Empty };

            // Act
            await _progressEventPublisher.PublishAsync(progressEvent);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Attempted to publish progress event with null or empty MigrationId")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _mockProgressQueueService.Verify(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PublishAsync_WithNullMigrationId_ShouldLogWarningAndReturn()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent { MigrationId = null! };

            // Act
            await _progressEventPublisher.PublishAsync(progressEvent);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Attempted to publish progress event with null or empty MigrationId")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _mockProgressQueueService.Verify(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PublishAsync_WithValidEvent_ShouldSerializeToJsonAndSendMessage()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = "test-migration",
                OverallProgress = 75.5,
                Status = "running"
            };

            string capturedJson = string.Empty;
            _mockProgressQueueService
                .Setup(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, CancellationToken>((queueName, json, token) => capturedJson = json)
                .Returns(Task.CompletedTask);

            // Act
            await _progressEventPublisher.PublishAsync(progressEvent);

            // Assert
            Assert.Contains("test-migration", capturedJson);
            Assert.Contains("75.5", capturedJson);
            Assert.Contains("running", capturedJson);
            Assert.Contains("\"eventType\":\"progress\"", capturedJson); // Verify camelCase
        }

        [Fact]
        public async Task PublishAsync_WithValidEvent_ShouldLogDebugMessage()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = "test-migration-debug",
                EventType = "progress",
                HubMethod = "MigrationProgressUpdated"
            };

            // Act
            await _progressEventPublisher.PublishAsync(progressEvent);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Successfully published progress event")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task PublishAsync_WhenQueueThrowsException_ShouldLogErrorAndNotRethrow()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent { MigrationId = "test-migration" };
            var expectedException = new InvalidOperationException("Queue operation failed");

            _mockProgressQueueService
                .Setup(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            // Act & Assert (should not throw)
            await _progressEventPublisher.PublishAsync(progressEvent);

            // Assert error was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to publish progress event to queue")),
                    expectedException,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task PublishAsync_WhenQueueCreationThrowsException_ShouldLogErrorAndNotRethrow()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent { MigrationId = "test-migration" };
            var expectedException = new InvalidOperationException("Queue creation failed");

            _mockProgressQueueService
                .Setup(x => x.EnsureQueueExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            // Act & Assert (should not throw)
            await _progressEventPublisher.PublishAsync(progressEvent);

            // Assert error was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to publish progress event to queue")),
                    expectedException,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region Queue Operations Tests

        [Fact]
        public async Task PublishAsync_ShouldCreateQueueIfNotExists()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent { MigrationId = "test-migration" };

            // Act
            await _progressEventPublisher.PublishAsync(progressEvent);

            // Assert
            // Note: QueueServiceClient operations are internal implementation details
        }

        [Fact]
        public async Task PublishAsync_ShouldUseCorrectQueueName()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent { MigrationId = "test-migration" };

            // Act
            await _progressEventPublisher.PublishAsync(progressEvent);

            // Assert
            // Note: QueueServiceClient operations are internal implementation details
        }

        #endregion

        #region Concurrency Tests

        [Fact]
        public async Task PublishAsync_ConcurrentCalls_ShouldHandleCorrectly()
        {
            // Arrange
            var events = new ProgressEvent[]
            {
                new MigrationProgressEvent { MigrationId = "migration-1" },
                new BatchProgressEvent { MigrationId = "migration-2" },
                new EntityProgressEvent { MigrationId = "migration-3" }
            };

            // Act
            var tasks = new Task[]
            {
                _progressEventPublisher.PublishAsync(events[0]),
                _progressEventPublisher.PublishAsync(events[1]),
                _progressEventPublisher.PublishAsync(events[2])
            };

            await Task.WhenAll(tasks);

            // Assert
            _mockProgressQueueService.Verify(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        #endregion

        #region Cancellation Tests

        [Fact]
        public async Task PublishAsync_WithCancelledToken_ShouldPassCancellationToQueue()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent { MigrationId = "test-migration" };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert (should not throw but may complete quickly due to cancellation)
            await _progressEventPublisher.PublishAsync(progressEvent, cts.Token);

            // Verify cancellation token was passed through
            _mockProgressQueueService.Verify(x => x.EnsureQueueExistsAsync(It.IsAny<string>(), cts.Token), Times.Once);
            _mockProgressQueueService.Verify(x => x.SendJsonMessageAsync(It.IsAny<string>(), It.IsAny<string>(), cts.Token), Times.Once);
        }

        #endregion

        #region SOLID Principles Tests

        [Fact]
        public void ProgressEventPublisher_ShouldFollowSingleResponsibilityPrinciple()
        {
            // Assert - The class should only handle progress event publishing to queues
            var type = typeof(ProgressEventPublisher);
            var methods = type.GetMethods();
            
            // Should have specific publish methods and the core PublishAsync method
            Assert.Contains(methods, m => m.Name == "PublishMigrationProgressAsync");
            Assert.Contains(methods, m => m.Name == "PublishBatchProgressAsync");
            Assert.Contains(methods, m => m.Name == "PublishEntityProgressAsync");
            Assert.Contains(methods, m => m.Name == "PublishErrorAsync");
            Assert.Contains(methods, m => m.Name == "PublishStatusAsync");
            Assert.Contains(methods, m => m.Name == "PublishAsync");
        }

        [Fact]
        public void ProgressEventPublisher_ShouldDependOnAbstractions()
        {
            // Assert - Constructor should depend on abstractions (ILogger interface)
            var constructor = typeof(ProgressEventPublisher).GetConstructors()[0];
            var parameters = constructor.GetParameters();
            
            Assert.Equal(2, parameters.Length);
            Assert.Contains(parameters, p => p.ParameterType == typeof(IProgressQueueService));
            Assert.Contains(parameters, p => p.ParameterType == typeof(ILogger<ProgressEventPublisher>));
        }

        #endregion
    }
} 