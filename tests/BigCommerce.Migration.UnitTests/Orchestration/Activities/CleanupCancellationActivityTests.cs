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
/// TDD unit tests for CleanupCancellationActivity
/// Task 7.2.5: Comprehensive testing for cancellation cleanup and resource disposal
/// CRITICAL: Tests graceful cleanup, resource disposal, and error handling during cleanup
/// </summary>
[Trait("Category", "LiveCancellation")]
[Trait("Component", "Activities")]
public class CleanupCancellationActivityTests
{
    private readonly Mock<ILiveCancellationManager> _mockLiveCancellationManager;
    private readonly Mock<IMigrationStorageService> _mockMigrationStorageService;
    private readonly Mock<ILogger<CleanupCancellationActivity>> _mockLogger;
    private readonly CleanupCancellationActivity _activity;
    private const string TestMigrationId = "test-migration-789";
    private const string TestEntityType = "categories";
    private const string TestBatchId = "batch-123";
    private const string TestStoreId = "store-456";

    public CleanupCancellationActivityTests()
    {
        _mockLiveCancellationManager = new Mock<ILiveCancellationManager>();
        _mockMigrationStorageService = new Mock<IMigrationStorageService>();
        _mockLogger = new Mock<ILogger<CleanupCancellationActivity>>();
        _activity = new CleanupCancellationActivity(
            _mockLiveCancellationManager.Object,
            _mockMigrationStorageService.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLiveCancellationManager_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CleanupCancellationActivity(null!, _mockMigrationStorageService.Object, _mockLogger.Object));

        exception.ParamName.Should().Be("liveCancellationManager");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CleanupCancellationActivity(_mockLiveCancellationManager.Object, _mockMigrationStorageService.Object, null!));

        exception.ParamName.Should().Be("logger");
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act
        var activity = new CleanupCancellationActivity(
            _mockLiveCancellationManager.Object,
            _mockMigrationStorageService.Object,
            _mockLogger.Object);

        // Assert
        activity.Should().NotBeNull();
    }

    #endregion

    #region Azure Functions Structure Tests

    [Fact]
    public void CleanupCancellationActivity_ShouldHave_AzureFunctionsAttributes()
    {
        // Arrange & Act
        var activityType = typeof(CleanupCancellationActivity);
        
        // Assert - Verify Azure Functions method structure and attributes
        var methods = activityType.GetMethods();
        var cleanupCancellationMethod = methods.FirstOrDefault(m => m.Name == "CleanupCancellationAsync");
        
        cleanupCancellationMethod.Should().NotBeNull("Activity should have CleanupCancellationAsync method for Azure Functions execution");
        cleanupCancellationMethod!.Should().BeDecoratedWith<Microsoft.Azure.Functions.Worker.FunctionAttribute>(
            "CleanupCancellationAsync method should have Function attribute for Azure Functions");
    }

    [Fact]
    public void CleanupCancellationActivity_ShouldHave_ProperConstructorDependencies()
    {
        // Arrange & Act
        var constructors = typeof(CleanupCancellationActivity).GetConstructors();
        
        // Assert - Verify dependency injection structure for cleanup operations
        constructors.Should().HaveCount(1, "Should have single constructor for DI");
        
        var constructor = constructors[0];
        var parameters = constructor.GetParameters();
        
        parameters.Should().Contain(p => p.ParameterType == typeof(ILiveCancellationManager),
            "Should depend on LiveCancellationManager for cleanup operations");
        parameters.Should().Contain(p => p.ParameterType == typeof(ILogger<CleanupCancellationActivity>),
            "Should depend on logger for cleanup observability");
    }

    #endregion

    #region Request Validation Tests

    [Fact]
    public async Task CleanupCancellationAsync_WithNullRequest_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _activity.CleanupCancellationAsync(null!));

        exception.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task CleanupCancellationAsync_WithEmptyMigrationId_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new CleanupRequest
        {
            MigrationId = "",
            Scope = CancellationScope.Migration
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _activity.CleanupCancellationAsync(request));

        exception.ParamName.Should().Be("request");
        exception.Message.Should().Contain("Migration ID is required");
    }

    [Fact]
    public async Task CleanupCancellationAsync_WithWhitespaceMigrationId_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new CleanupRequest
        {
            MigrationId = "   ",
            Scope = CancellationScope.Migration
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _activity.CleanupCancellationAsync(request));

        exception.ParamName.Should().Be("request");
        exception.Message.Should().Contain("Migration ID is required");
    }

    #endregion

    #region Single Cleanup Tests

    [Theory]
    [InlineData(CancellationScope.Migration)]
    [InlineData(CancellationScope.EntityType)]
    [InlineData(CancellationScope.Batch)]
    [InlineData(CancellationScope.Store)]
    public async Task CleanupCancellationAsync_WithAllScopes_ShouldCallLiveCancellationManager(CancellationScope scope)
    {
        // Arrange
        var request = new CleanupRequest
        {
            MigrationId = TestMigrationId,
            Scope = scope,
            EntityType = TestEntityType,
            BatchId = TestBatchId,
            StoreId = TestStoreId,
            ForceCleanup = false
        };

        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, scope, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.CleanupCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Scope.Should().Be(scope);
        result.Success.Should().BeTrue();
        result.CleanedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        
        _mockLiveCancellationManager.Verify(x => x.MarkCancellationProcessedAsync(TestMigrationId, scope, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CleanupCancellationAsync_WithSuccessfulCleanup_ShouldReturnSuccessResult()
    {
        // Arrange
        var request = new CleanupRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.EntityType,
            EntityType = TestEntityType,
            ForceCleanup = false
        };

        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.EntityType, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.CleanupCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Scope.Should().Be(CancellationScope.EntityType);
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.ResourcesFreed.Should().BeTrue();
        result.CleanedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CleanupCancellationAsync_WithForceCleanup_ShouldPerformAdditionalCleanupSteps()
    {
        // Arrange
        var request = new CleanupRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration,
            ForceCleanup = true,
            AdditionalContext = new Dictionary<string, object>
            {
                { "cleanup_temp_files", true },
                { "cleanup_memory", true }
            }
        };

        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.CleanupCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ForceCleanupPerformed.Should().BeTrue();
        result.ResourcesFreed.Should().BeTrue();
        result.CleanupDetails.Should().NotBeNull();
        result.CleanupDetails!.Should().ContainKey("force_cleanup_completed");
        
        _mockLiveCancellationManager.Verify(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Batch Cleanup Tests

    [Fact]
    public async Task BatchCleanupCancellationAsync_WithMultipleRequests_ShouldProcessAllRequests()
    {
        // Arrange
        var requests = new List<CleanupRequest>
        {
            new()
            {
                MigrationId = TestMigrationId,
                Scope = CancellationScope.EntityType,
                EntityType = "categories",
                ForceCleanup = false
            },
            new()
            {
                MigrationId = TestMigrationId,
                Scope = CancellationScope.Batch,
                BatchId = TestBatchId,
                ForceCleanup = false
            },
            new()
            {
                MigrationId = TestMigrationId,
                Scope = CancellationScope.Store,
                StoreId = TestStoreId,
                ForceCleanup = true
            }
        };

        var batchRequest = new BatchCleanupRequest
        {
            Requests = requests
        };

        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, It.IsAny<CancellationScope>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.BatchCleanupCancellationAsync(batchRequest);

        // Assert
        result.Should().NotBeNull();
        result.TotalRequests.Should().Be(3);
        result.SuccessfulCleanups.Should().Be(3);
        result.FailedCleanups.Should().Be(0);
        result.Results.Should().HaveCount(3);
        result.Results.Should().OnlyContain(r => r.Success);

        _mockLiveCancellationManager.Verify(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.EntityType, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Batch, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Store, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BatchCleanupCancellationAsync_WithMixedResults_ShouldReturnCorrectCounts()
    {
        // Arrange
        var requests = new List<CleanupRequest>
        {
            new()
            {
                MigrationId = TestMigrationId,
                Scope = CancellationScope.EntityType,
                EntityType = "categories"
            },
            new()
            {
                MigrationId = "invalid-id",
                Scope = CancellationScope.Batch,
                BatchId = TestBatchId
            }
        };

        var batchRequest = new BatchCleanupRequest { Requests = requests };

        // Setup mixed results - first succeeds, second fails
        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.EntityType, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync("invalid-id", CancellationScope.Batch, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Cleanup failed"));

        // Act
        var result = await _activity.BatchCleanupCancellationAsync(batchRequest);

        // Assert
        result.Should().NotBeNull();
        result.TotalRequests.Should().Be(2);
        result.SuccessfulCleanups.Should().Be(1);
        result.FailedCleanups.Should().Be(1);
        result.Results.Should().HaveCount(2);
        result.Results.Should().ContainSingle(r => r.Success);
        result.Results.Should().ContainSingle(r => !r.Success);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CleanupCancellationAsync_WhenOperationCancelled_ShouldReturnCancelledResult()
    {
        // Arrange
        var request = new CleanupRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        var cancellationToken = new CancellationToken(true);

        // Act
        var result = await _activity.CleanupCancellationAsync(request, cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Cleanup operation was cancelled");
        result.ResourcesFreed.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupCancellationAsync_WhenLiveCancellationManagerThrows_ShouldReturnErrorResult()
    {
        // Arrange
        var request = new CleanupRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        var testException = new InvalidOperationException("Test cleanup error");
        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(testException);

        // Act
        var result = await _activity.CleanupCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Test cleanup error");
        result.ResourcesFreed.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupCancellationAsync_WhenForceCleanupFails_ShouldStillMarkProcessed()
    {
        // Arrange
        var request = new CleanupRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration,
            ForceCleanup = true
        };

        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act - Force cleanup will fail internally but should still mark as processed
        var result = await _activity.CleanupCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue(); // Main cleanup succeeded
        result.ForceCleanupPerformed.Should().BeTrue();
        result.ResourcesFreed.Should().BeTrue();
        
        _mockLiveCancellationManager.Verify(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Continue-on-Error Tests

    [Fact]
    public async Task BatchCleanupCancellationAsync_WhenSomeRequestsFail_ShouldContinueProcessingOthers()
    {
        // Arrange
        var requests = new List<CleanupRequest>
        {
            new() { MigrationId = TestMigrationId, Scope = CancellationScope.Migration },
            new() { MigrationId = "failing-id", Scope = CancellationScope.EntityType, EntityType = "products" },
            new() { MigrationId = TestMigrationId, Scope = CancellationScope.Batch, BatchId = TestBatchId }
        };

        var batchRequest = new BatchCleanupRequest { Requests = requests };

        // Setup - first succeeds, second throws, third succeeds
        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync("failing-id", CancellationScope.EntityType, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Cleanup failed"));

        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Batch, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.BatchCleanupCancellationAsync(batchRequest);

        // Assert - Continue-on-error behavior
        result.Should().NotBeNull();
        result.TotalRequests.Should().Be(3);
        result.SuccessfulCleanups.Should().Be(2);
        result.FailedCleanups.Should().Be(1);
        result.Results.Should().HaveCount(3);
        
        // Verify all requests were attempted despite middle failure
        _mockLiveCancellationManager.Verify(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.MarkCancellationProcessedAsync("failing-id", CancellationScope.EntityType, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Batch, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Resource Management Tests

    [Fact]
    public async Task CleanupCancellationAsync_ShouldTrack_CleanupMetrics()
    {
        // Arrange
        var request = new CleanupRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.EntityType,
            EntityType = TestEntityType,
            ForceCleanup = true
        };

        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.EntityType, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.CleanupCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.CleanupTimeMs.Should().BeGreaterThan(0, "Cleanup time should be measured");
        result.ResourcesFreed.Should().BeTrue();
        result.CleanupDetails.Should().NotBeNull();
        result.CleanupDetails!.Should().ContainKey("cleanup_completed_at");
    }

    [Fact]
    public async Task CleanupCancellationAsync_WithAdditionalContext_ShouldIncludeInResult()
    {
        // Arrange
        var additionalContext = new Dictionary<string, object>
        {
            { "temp_files_count", 5 },
            { "memory_usage_mb", 128 },
            { "custom_cleanup_flag", true }
        };

        var request = new CleanupRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Batch,
            BatchId = TestBatchId,
            AdditionalContext = additionalContext
        };

        _mockLiveCancellationManager
            .Setup(x => x.MarkCancellationProcessedAsync(TestMigrationId, CancellationScope.Batch, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _activity.CleanupCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.CleanupDetails.Should().NotBeNull();
        result.CleanupDetails!.Should().ContainKey("additional_context");
        
        var contextResult = result.CleanupDetails!["additional_context"] as Dictionary<string, object>;
        contextResult.Should().NotBeNull();
        contextResult!.Should().ContainKey("temp_files_count");
        contextResult!["temp_files_count"].Should().Be(5);
    }

    #endregion

    #region Helper Methods

    private CleanupRequest CreateTestRequest(CancellationScope scope = CancellationScope.Migration)
    {
        return new CleanupRequest
        {
            MigrationId = TestMigrationId,
            Scope = scope,
            EntityType = TestEntityType,
            BatchId = TestBatchId,
            StoreId = TestStoreId,
            ForceCleanup = false
        };
    }

    #endregion
}