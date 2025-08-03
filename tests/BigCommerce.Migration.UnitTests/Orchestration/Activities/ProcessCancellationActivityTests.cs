using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using System.Collections.Generic;
using System.Linq;

namespace BigCommerce.Migration.UnitTests.Orchestration.Activities;

/// <summary>
/// TDD unit tests for ProcessCancellationActivity
/// Task 7.2.5: Comprehensive testing for cancellation processing with SignalR integration
/// CRITICAL: Tests LiveCancellationManager integration, SignalR propagation, and batch processing
/// </summary>
[Trait("Category", "LiveCancellation")]
[Trait("Component", "Activities")]
public class ProcessCancellationActivityTests
{
    private readonly Mock<ILiveCancellationManager> _mockLiveCancellationManager;
    private readonly Mock<ILogger<ProcessCancellationActivity>> _mockLogger;
    private readonly ProcessCancellationActivity _activity;
    private const string TestMigrationId = "test-migration-789";
    private const string TestEntityType = "categories";
    private const string TestBatchId = "batch-123";
    private const string TestStoreId = "store-456";
    private const string TestReason = "User requested cancellation";
    private const string TestRequestedBy = "admin@test.com";

    public ProcessCancellationActivityTests()
    {
        _mockLiveCancellationManager = new Mock<ILiveCancellationManager>();
        _mockLogger = new Mock<ILogger<ProcessCancellationActivity>>();
        _activity = new ProcessCancellationActivity(_mockLiveCancellationManager.Object, _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLiveCancellationManager_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ProcessCancellationActivity(null!, _mockLogger.Object));

        exception.ParamName.Should().Be("liveCancellationManager");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ProcessCancellationActivity(_mockLiveCancellationManager.Object, null!));

        exception.ParamName.Should().Be("logger");
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act
        var activity = new ProcessCancellationActivity(_mockLiveCancellationManager.Object, _mockLogger.Object);

        // Assert
        activity.Should().NotBeNull();
    }

    #endregion

    #region Azure Functions Structure Tests

    [Fact]
    public void ProcessCancellationActivity_ShouldHave_AzureFunctionsAttributes()
    {
        // Arrange & Act
        var activityType = typeof(ProcessCancellationActivity);
        
        // Assert - Verify Azure Functions method structure and attributes
        var methods = activityType.GetMethods();
        var processCancellationMethod = methods.FirstOrDefault(m => m.Name == "ProcessCancellationAsync");
        
        processCancellationMethod.Should().NotBeNull("Activity should have ProcessCancellationAsync method for Azure Functions execution");
        processCancellationMethod!.Should().BeDecoratedWith<Microsoft.Azure.Functions.Worker.FunctionAttribute>(
            "ProcessCancellationAsync method should have Function attribute for Azure Functions");
    }

    [Fact]
    public void ProcessCancellationActivity_ShouldHave_ProperConstructorDependencies()
    {
        // Arrange & Act
        var constructors = typeof(ProcessCancellationActivity).GetConstructors();
        
        // Assert - Verify dependency injection structure for cancellation processing
        constructors.Should().HaveCount(1, "Should have single constructor for DI");
        
        var constructor = constructors[0];
        var parameters = constructor.GetParameters();
        
        parameters.Should().Contain(p => p.ParameterType == typeof(ILiveCancellationManager),
            "Should depend on LiveCancellationManager for cancellation processing and SignalR integration");
        parameters.Should().Contain(p => p.ParameterType == typeof(ILogger<ProcessCancellationActivity>),
            "Should depend on logger for cancellation processing observability");
    }

    #endregion

    #region Request Validation Tests

    [Fact]
    public async Task ProcessCancellationAsync_WithNullRequest_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _activity.ProcessCancellationAsync(null!));

        exception.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task ProcessCancellationAsync_WithEmptyMigrationId_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new CancellationProcessRequest
        {
            MigrationId = "",
            Scope = CancellationScope.Migration,
            Reason = TestReason,
            RequestedBy = TestRequestedBy
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _activity.ProcessCancellationAsync(request));

        exception.ParamName.Should().Be("request");
        exception.Message.Should().Contain("Migration ID is required");
    }

    [Fact]
    public async Task ProcessCancellationAsync_WithEmptyReason_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new CancellationProcessRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration,
            Reason = "",
            RequestedBy = TestRequestedBy
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _activity.ProcessCancellationAsync(request));

        exception.ParamName.Should().Be("request");
        exception.Message.Should().Contain("Cancellation reason is required");
    }

    #endregion

    #region Single Cancellation Processing Tests

    [Theory]
    [InlineData(CancellationScope.Migration)]
    [InlineData(CancellationScope.EntityType)]
    [InlineData(CancellationScope.Batch)]
    [InlineData(CancellationScope.Store)]
    public async Task ProcessCancellationAsync_WithAllScopes_ShouldCallLiveCancellationManager(CancellationScope scope)
    {
        // Arrange
        var request = new CancellationProcessRequest
        {
            MigrationId = TestMigrationId,
            Scope = scope,
            Reason = TestReason,
            RequestedBy = TestRequestedBy,
            EntityType = TestEntityType,
            BatchId = TestBatchId,
            StoreId = TestStoreId
        };

        var cancellationResult = new CancellationResult
        {
            Success = true,
            CancellationId = Guid.NewGuid().ToString(),
            MigrationId = TestMigrationId,
            Scope = scope,
            ProcessedAt = DateTime.UtcNow
        };

        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync(TestMigrationId, scope, TestReason, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(cancellationResult);

        // Act
        var result = await _activity.ProcessCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Scope.Should().Be(scope);
        result.Success.Should().BeTrue();
        result.CancellationId.Should().Be(cancellationResult.CancellationId);
        result.Reason.Should().Be(TestReason);
        result.RequestedBy.Should().Be(TestRequestedBy);
        
        _mockLiveCancellationManager.Verify(x => x.CancelAsync(TestMigrationId, scope, TestReason, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProcessCancellationAsync_WithSuccessfulCancellation_ShouldPropagateToAllInstances()
    {
        // Arrange
        var request = new CancellationProcessRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.EntityType,
            Reason = TestReason,
            RequestedBy = TestRequestedBy,
            EntityType = TestEntityType
        };

        var cancellationResult = new CancellationResult
        {
            Success = true,
            CancellationId = Guid.NewGuid().ToString(),
            MigrationId = TestMigrationId,
            Scope = CancellationScope.EntityType
        };

        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync(TestMigrationId, CancellationScope.EntityType, TestReason, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(cancellationResult);

        _mockLiveCancellationManager
            .Setup(x => x.PropagateToAllInstancesAsync(TestMigrationId, CancellationScope.EntityType, TestEntityType, null, null))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.ProcessCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.PropagationCompleted.Should().BeTrue();
        
        _mockLiveCancellationManager.Verify(x => x.CancelAsync(TestMigrationId, CancellationScope.EntityType, TestReason, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.PropagateToAllInstancesAsync(TestMigrationId, CancellationScope.EntityType, TestEntityType, null, null), Times.Once);
    }

    [Fact]
    public async Task ProcessCancellationAsync_WhenCancellationFails_ShouldReturnFailureResult()
    {
        // Arrange
        var request = new CancellationProcessRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration,
            Reason = TestReason,
            RequestedBy = TestRequestedBy
        };

        var cancellationResult = new CancellationResult
        {
            Success = false,
            ErrorDetails = "Cancellation failed due to storage error",
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync(TestMigrationId, CancellationScope.Migration, TestReason, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(cancellationResult);

        // Act
        var result = await _activity.ProcessCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Cancellation failed due to storage error");
        result.PropagationCompleted.Should().BeFalse();
        
        _mockLiveCancellationManager.Verify(x => x.CancelAsync(TestMigrationId, CancellationScope.Migration, TestReason, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.PropagateToAllInstancesAsync(It.IsAny<string>(), It.IsAny<CancellationScope>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Batch Cancellation Processing Tests

    [Fact]
    public async Task ProcessBatchCancellationAsync_WithMultipleRequests_ShouldProcessAllRequests()
    {
        // Arrange
        var requests = new List<CancellationProcessRequest>
        {
            new()
            {
                MigrationId = TestMigrationId,
                Scope = CancellationScope.EntityType,
                Reason = "Stop categories",
                RequestedBy = TestRequestedBy,
                EntityType = "categories"
            },
            new()
            {
                MigrationId = TestMigrationId,
                Scope = CancellationScope.Batch,
                Reason = "Stop batch",
                RequestedBy = TestRequestedBy,
                BatchId = TestBatchId
            }
        };

        var batchRequest = new BatchCancellationProcessRequest
        {
            Requests = requests
        };

        // Setup successful cancellation results
        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync(TestMigrationId, CancellationScope.EntityType, "Stop categories", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CancellationResult { Success = true, MigrationId = TestMigrationId, Scope = CancellationScope.EntityType });

        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync(TestMigrationId, CancellationScope.Batch, "Stop batch", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CancellationResult { Success = true, MigrationId = TestMigrationId, Scope = CancellationScope.Batch });

        _mockLiveCancellationManager
            .Setup(x => x.PropagateToAllInstancesAsync(It.IsAny<string>(), It.IsAny<CancellationScope>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.ProcessBatchCancellationAsync(batchRequest);

        // Assert
        result.Should().NotBeNull();
        result.TotalRequests.Should().Be(2);
        result.SuccessfulCancellations.Should().Be(2);
        result.FailedCancellations.Should().Be(0);
        result.Results.Should().HaveCount(2);
        result.Results.Should().OnlyContain(r => r.Success);

        _mockLiveCancellationManager.Verify(x => x.CancelAsync(TestMigrationId, CancellationScope.EntityType, "Stop categories", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.CancelAsync(TestMigrationId, CancellationScope.Batch, "Stop batch", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ProcessBatchCancellationAsync_WithMixedResults_ShouldReturnCorrectCounts()
    {
        // Arrange
        var requests = new List<CancellationProcessRequest>
        {
            new()
            {
                MigrationId = TestMigrationId,
                Scope = CancellationScope.EntityType,
                Reason = "Stop categories",
                RequestedBy = TestRequestedBy,
                EntityType = "categories"
            },
            new()
            {
                MigrationId = "invalid-id",
                Scope = CancellationScope.Batch,
                Reason = "Stop batch",
                RequestedBy = TestRequestedBy,
                BatchId = TestBatchId
            }
        };

        var batchRequest = new BatchCancellationProcessRequest
        {
            Requests = requests
        };

        // Setup mixed results - one success, one failure
        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync(TestMigrationId, CancellationScope.EntityType, "Stop categories", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CancellationResult { Success = true, MigrationId = TestMigrationId });

        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync("invalid-id", CancellationScope.Batch, "Stop batch", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CancellationResult { Success = false, ErrorDetails = "Migration not found" });

        _mockLiveCancellationManager
            .Setup(x => x.PropagateToAllInstancesAsync(TestMigrationId, CancellationScope.EntityType, "categories", null, null))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.ProcessBatchCancellationAsync(batchRequest);

        // Assert
        result.Should().NotBeNull();
        result.TotalRequests.Should().Be(2);
        result.SuccessfulCancellations.Should().Be(1);
        result.FailedCancellations.Should().Be(1);
        result.Results.Should().HaveCount(2);
        result.Results.Should().ContainSingle(r => r.Success);
        result.Results.Should().ContainSingle(r => !r.Success);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ProcessCancellationAsync_WhenOperationCancelled_ShouldReturnCancelledResult()
    {
        // Arrange
        var request = new CancellationProcessRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration,
            Reason = TestReason,
            RequestedBy = TestRequestedBy
        };

        var cancellationToken = new CancellationToken(true);

        // Act
        var result = await _activity.ProcessCancellationAsync(request, cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Operation was cancelled");
        result.PropagationCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessCancellationAsync_WhenLiveCancellationManagerThrows_ShouldReturnErrorResult()
    {
        // Arrange
        var request = new CancellationProcessRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration,
            Reason = TestReason,
            RequestedBy = TestRequestedBy
        };

        var testException = new InvalidOperationException("Test cancellation processing error");
        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync(TestMigrationId, CancellationScope.Migration, TestReason, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(testException);

        // Act
        var result = await _activity.ProcessCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Test cancellation processing error");
        result.PropagationCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessCancellationAsync_WhenPropagationFails_ShouldStillReturnSuccessButNotePropagationFailure()
    {
        // Arrange
        var request = new CancellationProcessRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.EntityType,
            Reason = TestReason,
            RequestedBy = TestRequestedBy,
            EntityType = TestEntityType
        };

        var cancellationResult = new CancellationResult
        {
            Success = true,
            CancellationId = Guid.NewGuid().ToString(),
            MigrationId = TestMigrationId,
            Scope = CancellationScope.EntityType
        };

        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync(TestMigrationId, CancellationScope.EntityType, TestReason, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(cancellationResult);

        _mockLiveCancellationManager
            .Setup(x => x.PropagateToAllInstancesAsync(TestMigrationId, CancellationScope.EntityType, TestEntityType, null, null))
            .ThrowsAsync(new InvalidOperationException("Propagation failed"));

        // Act
        var result = await _activity.ProcessCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue(); // Cancellation succeeded
        result.PropagationCompleted.Should().BeFalse(); // But propagation failed
        result.PropagationError.Should().Be("Propagation failed");
        
        _mockLiveCancellationManager.Verify(x => x.CancelAsync(TestMigrationId, CancellationScope.EntityType, TestReason, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Performance and Continue-on-Error Tests

    [Fact]
    public async Task ProcessBatchCancellationAsync_WhenSomeRequestsFail_ShouldContinueProcessingOthers()
    {
        // Arrange
        var requests = new List<CancellationProcessRequest>
        {
            new() { MigrationId = TestMigrationId, Scope = CancellationScope.Migration, Reason = "Test 1", RequestedBy = TestRequestedBy },
            new() { MigrationId = "failing-id", Scope = CancellationScope.EntityType, Reason = "Test 2", RequestedBy = TestRequestedBy },
            new() { MigrationId = TestMigrationId, Scope = CancellationScope.Batch, Reason = "Test 3", RequestedBy = TestRequestedBy }
        };

        var batchRequest = new BatchCancellationProcessRequest { Requests = requests };

        // Setup - first succeeds, second throws, third succeeds
        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync(TestMigrationId, CancellationScope.Migration, "Test 1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CancellationResult { Success = true });

        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync("failing-id", CancellationScope.EntityType, "Test 2", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Processing failed"));

        _mockLiveCancellationManager
            .Setup(x => x.CancelAsync(TestMigrationId, CancellationScope.Batch, "Test 3", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new CancellationResult { Success = true });

        _mockLiveCancellationManager
            .Setup(x => x.PropagateToAllInstancesAsync(It.IsAny<string>(), It.IsAny<CancellationScope>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.ProcessBatchCancellationAsync(batchRequest);

        // Assert - Continue-on-error behavior
        result.Should().NotBeNull();
        result.TotalRequests.Should().Be(3);
        result.SuccessfulCancellations.Should().Be(2);
        result.FailedCancellations.Should().Be(1);
        result.Results.Should().HaveCount(3);
        
        // Verify all requests were attempted despite middle failure
        _mockLiveCancellationManager.Verify(x => x.CancelAsync(TestMigrationId, CancellationScope.Migration, "Test 1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.CancelAsync("failing-id", CancellationScope.EntityType, "Test 2", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.CancelAsync(TestMigrationId, CancellationScope.Batch, "Test 3", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private CancellationProcessRequest CreateTestRequest(CancellationScope scope = CancellationScope.Migration)
    {
        return new CancellationProcessRequest
        {
            MigrationId = TestMigrationId,
            Scope = scope,
            Reason = TestReason,
            RequestedBy = TestRequestedBy,
            EntityType = TestEntityType,
            BatchId = TestBatchId,
            StoreId = TestStoreId
        };
    }

    #endregion
}