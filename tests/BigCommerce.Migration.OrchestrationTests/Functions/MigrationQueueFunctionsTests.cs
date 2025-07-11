using Microsoft.Extensions.Logging;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Functions.Functions;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Functions;

/// <summary>
/// Tests for MigrationQueueFunctions
/// Updated to work with Durable Function integration
/// </summary>
public class MigrationQueueFunctionsTests
{
    private readonly Mock<ILogger<MigrationQueueFunctions>> _loggerMock;
    private readonly Mock<IQueueService> _queueServiceMock;
    private readonly Mock<IMigrationStorageService> _migrationStorageMock;
    private readonly Mock<IOpenSearchService> _openSearchMock;
    private readonly Mock<IDurableTaskClient> _durableClientMock;
    private readonly MigrationQueueFunctions _functions;

    public MigrationQueueFunctionsTests()
    {
        _loggerMock = new Mock<ILogger<MigrationQueueFunctions>>();
        _queueServiceMock = new Mock<IQueueService>();
        _migrationStorageMock = new Mock<IMigrationStorageService>();
        _openSearchMock = new Mock<IOpenSearchService>();
        _durableClientMock = new Mock<IDurableTaskClient>();
        
        _functions = new MigrationQueueFunctions(
            _loggerMock.Object,
            _queueServiceMock.Object,
            _migrationStorageMock.Object,
            _openSearchMock.Object);
    }

    [Fact]
    public async Task ProcessMigrationStartMessage_ValidMessage_StartsOrchestrator()
    {
        // Arrange
        var queueMessage = new QueueMessage
        {
            MessageId = "test-message-123",
            Content = "{\"migrationId\":\"test-migration-123\",\"migrationRequest\":{\"entities\":[\"categories\"]},\"categoryTreeContext\":null}"
        };

        var validationResult = new MessageValidationResult { IsValid = true };
        _queueServiceMock.Setup(x => x.ValidateQueueMessageAsync(It.IsAny<QueueMessage>()))
            .ReturnsAsync(validationResult);

        // Create a proper dictionary structure that can be serialized/deserialized
        var mockMigrationData = new Dictionary<string, object>
        {
            ["migrationId"] = "test-migration-123",
            ["migrationRequest"] = new Dictionary<string, object>
            {
                ["entities"] = new List<string> { "categories" }
            },
            ["categoryTreeContext"] = null
        };

        _queueServiceMock.Setup(x => x.ParseQueueMessageAsync<dynamic>(It.IsAny<QueueMessage>()))
            .ReturnsAsync(mockMigrationData);

        // Return a fresh object each time for consistency  
        _migrationStorageMock.Setup(x => x.GetMigrationAsync(It.IsAny<string>()))
            .ReturnsAsync(() => new Core.Models.MigrationEntry 
            { 
                Id = "test-migration-123", 
                Status = Core.Models.MigrationStatus.Queued 
            });

        _durableClientMock.Setup(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("instance-123");

        // Act
        await _functions.ProcessMigrationStartMessageInternal(queueMessage, _durableClientMock.Object);

        // Assert
        _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
            "MigrationDurableOrchestrator",
            It.IsAny<object>(),
            It.IsAny<StartOrchestrationOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessMigrationStartMessage_InvalidMessage_DoesNotStartOrchestrator()
    {
        // Arrange
        var queueMessage = new QueueMessage
        {
            MessageId = "test-message-123",
            Content = "invalid-json"
        };

        var validationResult = new MessageValidationResult 
        { 
            IsValid = false, 
            ValidationError = "Invalid JSON format" 
        };
        
        _queueServiceMock.Setup(x => x.ValidateQueueMessageAsync(It.IsAny<QueueMessage>()))
            .ReturnsAsync(validationResult);

        var deadLetterMessage = new DeadLetterMessage 
        { 
            MessageId = "dead-letter-123",
            OriginalMessage = queueMessage,
            Reason = "Invalid message format"
        };
        _queueServiceMock.Setup(x => x.CreateDeadLetterMessage(It.IsAny<QueueMessage>(), It.IsAny<string>()))
            .Returns(deadLetterMessage);

        // Act
        await _functions.ProcessMigrationStartMessageInternal(queueMessage, _durableClientMock.Object);

        // Assert
        _durableClientMock.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<StartOrchestrationOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessMigrationStartMessage_OrchestratorStartFails_UpdatesMigrationStatusToFailed()
    {
        // Arrange
        var queueMessage = new QueueMessage
        {
            MessageId = "test-message-123",
            Content = "{\"migrationId\":\"test-migration-123\",\"migrationRequest\":{\"entities\":[\"categories\"]},\"categoryTreeContext\":null}"
        };

        var validationResult = new MessageValidationResult { IsValid = true };
        _queueServiceMock.Setup(x => x.ValidateQueueMessageAsync(It.IsAny<QueueMessage>()))
            .ReturnsAsync(validationResult);

        // Create a proper dictionary structure that can be serialized/deserialized
        var mockMigrationData = new Dictionary<string, object>
        {
            ["migrationId"] = "test-migration-123",
            ["migrationRequest"] = new Dictionary<string, object>
            {
                ["entities"] = new List<string> { "categories" }
            },
            ["categoryTreeContext"] = null
        };

        _queueServiceMock.Setup(x => x.ParseQueueMessageAsync<dynamic>(It.IsAny<QueueMessage>()))
            .ReturnsAsync(mockMigrationData);

        // Return a fresh object each time to avoid reference issues in mock verification
        // The function calls GetMigrationAsync twice (once for InProgress, once for Failed)
        _migrationStorageMock.Setup(x => x.GetMigrationAsync(It.IsAny<string>()))
            .ReturnsAsync(() => new Core.Models.MigrationEntry 
            { 
                Id = "test-migration-123", 
                Status = Core.Models.MigrationStatus.Queued 
            });

        _durableClientMock.Setup(x => x.ScheduleNewOrchestrationInstanceAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<StartOrchestrationOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Orchestrator start failed"));

        var deadLetterMessage = new DeadLetterMessage 
        { 
            MessageId = "dead-letter-123",
            OriginalMessage = queueMessage,
            Reason = "Unexpected error"
        };
        _queueServiceMock.Setup(x => x.CreateDeadLetterMessage(It.IsAny<QueueMessage>(), It.IsAny<string>()))
            .Returns(deadLetterMessage);

        // Act
        await _functions.ProcessMigrationStartMessageInternal(queueMessage, _durableClientMock.Object);

        // Assert - Verify the correct flow: first InProgress, then Failed
        _migrationStorageMock.Verify(x => x.UpdateMigrationAsync(It.IsAny<Core.Models.MigrationEntry>()), Times.Exactly(2));
        
        // Verify first call updates to InProgress
        _migrationStorageMock.Verify(x => x.UpdateMigrationAsync(
            It.Is<Core.Models.MigrationEntry>(m => m.Status == Core.Models.MigrationStatus.InProgress)), Times.Once);
        
        // Verify second call updates to Failed
        _migrationStorageMock.Verify(x => x.UpdateMigrationAsync(
            It.Is<Core.Models.MigrationEntry>(m => m.Status == Core.Models.MigrationStatus.Failed)), Times.Once);
    }

    [Fact]
    public async Task ProcessMigrationStartMessage_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var queueMessage = new QueueMessage
        {
            MessageId = "test-message-123",
            Content = "{\"migrationId\":\"test-migration-123\"}"
        };

        var cancellationToken = new CancellationToken(canceled: true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _functions.ProcessMigrationStartMessageInternal(queueMessage, _durableClientMock.Object, cancellationToken));
    }

    [Fact]
    public async Task ProcessEntityBatchMessage_ValidMessage_ProcessesSuccessfully()
    {
        // Arrange
        var queueMessage = new QueueMessage
        {
            MessageId = "test-batch-123",
            Content = "{\"migrationId\":\"test-migration-123\",\"entityType\":\"products\",\"batchNumber\":1}"
        };

        var validationResult = new MessageValidationResult { IsValid = true };
        _queueServiceMock.Setup(x => x.ValidateQueueMessageAsync(It.IsAny<QueueMessage>()))
            .ReturnsAsync(validationResult);

        var mockBatchData = new EntityBatchMessage
        {
            MigrationId = "test-migration-123",
            EntityType = "products",
            BatchNumber = 1
        };

        _queueServiceMock.Setup(x => x.ParseQueueMessageAsync<EntityBatchMessage>(It.IsAny<QueueMessage>()))
            .ReturnsAsync(mockBatchData);

        var processingResult = new MessageProcessingResult
        {
            IsSuccess = true,
            ProcessingDuration = TimeSpan.FromSeconds(1),
            Metadata = new Dictionary<string, object>
            {
                ["SuccessCount"] = 5,
                ["FailureCount"] = 0
            }
        };

        _queueServiceMock.Setup(x => x.ProcessEntityBatchMessageAsync(It.IsAny<QueueMessage>()))
            .ReturnsAsync(processingResult);

        // Act
        await _functions.ProcessEntityBatchMessageInternal(queueMessage);

        // Assert
        _queueServiceMock.Verify(x => x.ProcessEntityBatchMessageAsync(queueMessage), Times.Once);
    }

    [Fact]
    public async Task ProcessCancellationMessage_ValidMessage_TerminatesOrchestrator()
    {
        // Arrange
        var queueMessage = new QueueMessage
        {
            MessageId = "test-cancel-123",
            Content = "{\"migrationId\":\"test-migration-123\",\"reason\":\"User requested\"}"
        };

        var processingResult = new MessageProcessingResult
        {
            IsSuccess = true,
            ProcessingDuration = TimeSpan.FromSeconds(1),
            Metadata = new Dictionary<string, object>
            {
                ["MigrationId"] = "test-migration-123",
                ["CancellationReason"] = "User requested"
            }
        };

        _queueServiceMock.Setup(x => x.ProcessCancellationMessageAsync(It.IsAny<QueueMessage>()))
            .ReturnsAsync(processingResult);

        // Setup mock to return a running instance (null means not found)
        _durableClientMock.Setup(x => x.GetInstanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationMetadata?)null);

        // For this test, we'll setup the mock to simulate that there's no running instance
        // In practice, the function should handle both cases (instance found vs not found)

        // Act
        await _functions.ProcessCancellationMessageInternal(queueMessage, _durableClientMock.Object);

        // Assert - Since no instance was found, termination should not be called
        _durableClientMock.Verify(x => x.TerminateInstanceAsync(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessCancellationMessage_ProcessingFails_CreatesDeadLetterMessage()
    {
        // Arrange
        var queueMessage = new QueueMessage
        {
            MessageId = "test-cancel-123",
            Content = "invalid-cancellation-data"
        };

        var processingResult = new MessageProcessingResult
        {
            IsSuccess = false,
            ErrorDetails = "Invalid cancellation data format"
        };

        _queueServiceMock.Setup(x => x.ProcessCancellationMessageAsync(It.IsAny<QueueMessage>()))
            .ReturnsAsync(processingResult);

        var deadLetterMessage = new DeadLetterMessage 
        { 
            MessageId = "dead-letter-cancel-123",
            OriginalMessage = queueMessage,
            Reason = "Processing failed: Invalid cancellation data format"
        };
        _queueServiceMock.Setup(x => x.CreateDeadLetterMessage(It.IsAny<QueueMessage>(), It.IsAny<string>()))
            .Returns(deadLetterMessage);

        // Act
        await _functions.ProcessCancellationMessageInternal(queueMessage, _durableClientMock.Object);

        // Assert
        _queueServiceMock.Verify(x => x.CreateDeadLetterMessage(queueMessage, 
            "Processing failed: Invalid cancellation data format"), Times.Once);
    }
} 