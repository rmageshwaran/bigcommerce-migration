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
/// TDD unit tests for CheckLiveCancellationActivity
/// Task 7.2.5: Comprehensive testing for live cancellation checking with multi-scope support
/// CRITICAL: Tests LiveCancellationManager integration, performance requirements, and error handling
/// </summary>
[Trait("Category", "LiveCancellation")]
[Trait("Component", "Activities")]
public class CheckLiveCancellationActivityTests
{
    private readonly Mock<ILiveCancellationManager> _mockLiveCancellationManager;
    private readonly Mock<ILogger<CheckLiveCancellationActivity>> _mockLogger;
    private readonly CheckLiveCancellationActivity _activity;
    private const string TestMigrationId = "test-migration-789";
    private const string TestEntityType = "categories";
    private const string TestBatchId = "batch-123";
    private const string TestStoreId = "store-456";

    public CheckLiveCancellationActivityTests()
    {
        _mockLiveCancellationManager = new Mock<ILiveCancellationManager>();
        _mockLogger = new Mock<ILogger<CheckLiveCancellationActivity>>();
        _activity = new CheckLiveCancellationActivity(_mockLiveCancellationManager.Object, _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLiveCancellationManager_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CheckLiveCancellationActivity(null!, _mockLogger.Object));

        exception.ParamName.Should().Be("liveCancellationManager");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CheckLiveCancellationActivity(_mockLiveCancellationManager.Object, null!));

        exception.ParamName.Should().Be("logger");
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act
        var activity = new CheckLiveCancellationActivity(_mockLiveCancellationManager.Object, _mockLogger.Object);

        // Assert
        activity.Should().NotBeNull();
    }

    #endregion

    #region Azure Functions Structure Tests

    [Fact]
    public void CheckLiveCancellationActivity_ShouldHave_AzureFunctionsAttributes()
    {
        // Arrange & Act
        var activityType = typeof(CheckLiveCancellationActivity);
        
        // Assert - Verify Azure Functions method structure and attributes
        var methods = activityType.GetMethods();
        var checkLiveCancellationMethod = methods.FirstOrDefault(m => m.Name == "CheckLiveCancellationAsync");
        
        checkLiveCancellationMethod.Should().NotBeNull("Activity should have CheckLiveCancellationAsync method for Azure Functions execution");
        checkLiveCancellationMethod!.Should().BeDecoratedWith<Microsoft.Azure.Functions.Worker.FunctionAttribute>(
            "CheckLiveCancellationAsync method should have Function attribute for Azure Functions");
    }

    [Fact]
    public void CheckLiveCancellationActivity_ShouldHave_ProperConstructorDependencies()
    {
        // Arrange & Act
        var constructors = typeof(CheckLiveCancellationActivity).GetConstructors();
        
        // Assert - Verify dependency injection structure for live cancellation
        constructors.Should().HaveCount(1, "Should have single constructor for DI");
        
        var constructor = constructors[0];
        var parameters = constructor.GetParameters();
        
        parameters.Should().Contain(p => p.ParameterType == typeof(ILiveCancellationManager),
            "Should depend on LiveCancellationManager for multi-scope cancellation checking");
        parameters.Should().Contain(p => p.ParameterType == typeof(ILogger<CheckLiveCancellationActivity>),
            "Should depend on logger for live cancellation observability");
    }

    #endregion

    #region Request Validation Tests

    [Fact]
    public async Task CheckLiveCancellationAsync_WithNullRequest_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _activity.CheckLiveCancellationAsync(null!));

        exception.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task CheckLiveCancellationAsync_WithEmptyMigrationId_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = "",
            Scope = CancellationScope.Migration
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _activity.CheckLiveCancellationAsync(request));

        exception.ParamName.Should().Be("request");
        exception.Message.Should().Contain("Migration ID is required");
    }

    [Fact]
    public async Task CheckLiveCancellationAsync_WithWhitespaceMigrationId_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = "   ",
            Scope = CancellationScope.Migration
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _activity.CheckLiveCancellationAsync(request));

        exception.ParamName.Should().Be("request");
        exception.Message.Should().Contain("Migration ID is required");
    }

    #endregion

    #region Multi-Scope Cancellation Tests

    [Theory]
    [InlineData(CancellationScope.Migration)]
    [InlineData(CancellationScope.EntityType)]
    [InlineData(CancellationScope.Batch)]
    [InlineData(CancellationScope.Store)]
    public async Task CheckLiveCancellationAsync_WithAllScopes_ShouldCallLiveCancellationManager(CancellationScope scope)
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = scope,
            EntityType = TestEntityType,
            BatchId = TestBatchId,
            StoreId = TestStoreId
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, scope, null, null, null))
            .ReturnsAsync(false);

        // Act
        var result = await _activity.CheckLiveCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Scope.Should().Be(scope);
        result.IsCancelled.Should().BeFalse();
        result.Success.Should().BeTrue();
        
        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, scope, null, null, null), Times.Once);
    }

    [Fact]
    public async Task CheckLiveCancellationAsync_WhenNotCancelled_ShouldReturnSuccessfulResult()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, null, null, null))
            .ReturnsAsync(false);

        // Act
        var result = await _activity.CheckLiveCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Scope.Should().Be(CancellationScope.Migration);
        result.IsCancelled.Should().BeFalse();
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.CheckedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CheckLiveCancellationAsync_WhenCancelled_ShouldReturnCancelledResultWithDetails()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.EntityType,
            EntityType = TestEntityType
        };

        var cancellationStatus = new CancellationStatus
        {
            MigrationId = TestMigrationId,
            ActiveScopes = new List<CancellationScope> { CancellationScope.EntityType },
            LastCancellationAt = DateTime.UtcNow.AddMinutes(-5),
            TotalCancellationRequests = 1,
            Reason = "User requested stop",
            RequestedBy = "admin@test.com",
            ActiveCancellations = new List<EnhancedCancellationTokenEntry>
            {
                new()
                {
                    MigrationId = TestMigrationId,
                    Scope = CancellationScope.EntityType,
                    Reason = "User requested stop",
                    RequestedBy = "admin@test.com",
                    EntityType = TestEntityType
                }
            }
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.EntityType, null, null, null))
            .ReturnsAsync(true);

        _mockLiveCancellationManager
            .Setup(x => x.GetCancellationStatusAsync(TestMigrationId))
            .ReturnsAsync(cancellationStatus);

        // Act
        var result = await _activity.CheckLiveCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Scope.Should().Be(CancellationScope.EntityType);
        result.IsCancelled.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.CancellationDetails.Should().NotBeNull();
        result.CancellationDetails!.ActiveScopes.Should().Contain(CancellationScope.EntityType);
        result.Reason.Should().Be("User requested stop");
        result.RequestedBy.Should().Be("admin@test.com");

        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.EntityType, null, null, null), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.GetCancellationStatusAsync(TestMigrationId), Times.Once);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task CheckLiveCancellationAsync_ShouldComplete_WithinPerformanceTarget()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, null, null, null))
            .ReturnsAsync(false);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _activity.CheckLiveCancellationAsync(request);
        stopwatch.Stop();

        // Assert - Performance target is <1000ms
        result.ResponseTimeMs.Should().BeLessThan(1000, 
            "Live cancellation check should complete within 1000ms performance target");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000,
            "Actual execution time should meet performance target");
    }

    [Fact]
    public async Task CheckLiveCancellationAsync_ShouldTrack_ResponseTime()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Batch,
            BatchId = TestBatchId
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Batch, null, null, null))
            .ReturnsAsync(false);

        // Act
        var result = await _activity.CheckLiveCancellationAsync(request);

        // Assert
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0, "Response time should be measured");
        result.ResponseTimeMs.Should().BeLessThan(1000, "Should meet performance target");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CheckLiveCancellationAsync_WhenOperationCancelled_ShouldReturnCancelledResult()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        var cancellationToken = new CancellationToken(true);

        // Act
        var result = await _activity.CheckLiveCancellationAsync(request, cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.IsCancelled.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Operation was cancelled");
    }

    [Fact]
    public async Task CheckLiveCancellationAsync_WhenLiveCancellationManagerThrows_ShouldReturnErrorResult()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        var testException = new InvalidOperationException("Test cancellation manager error");
        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, null, null, null))
            .ThrowsAsync(testException);

        // Act
        var result = await _activity.CheckLiveCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.IsCancelled.Should().BeFalse(); // On error, assume not cancelled to continue processing
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Test cancellation manager error");
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CheckLiveCancellationAsync_WhenGetStatusThrows_ShouldStillReturnBasicResult()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.EntityType,
            EntityType = TestEntityType
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.EntityType, null, null, null))
            .ReturnsAsync(true);

        _mockLiveCancellationManager
            .Setup(x => x.GetCancellationStatusAsync(TestMigrationId))
            .ThrowsAsync(new InvalidOperationException("Status retrieval failed"));

        // Act
        var result = await _activity.CheckLiveCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.IsCancelled.Should().BeFalse(); // On error, assume not cancelled
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Status retrieval failed");
    }

    #endregion

    #region Hierarchical Cancellation Tests

    [Fact]
    public async Task CheckLiveCancellationAsync_WithMigrationScope_ShouldCheckOnlyMigrationScope()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        // Setup mock for single scope checking - not cancelled
        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, null, null, null))
            .ReturnsAsync(false);

        // Act
        var result = await _activity.CheckLiveCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.IsCancelled.Should().BeFalse();
        result.Scope.Should().Be(CancellationScope.Migration);
        result.Success.Should().BeTrue();
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);

        // Verify only migration scope was checked
        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, null, null, null), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.EntityType, null, null, null), Times.Never);
        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Batch, null, null, null), Times.Never);
        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Store, null, null, null), Times.Never);
    }

    [Fact]
    public async Task CheckLiveCancellationAsync_WhenMigrationCancelled_ShouldReturnCancelledWithDetails()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        // Setup migration-level cancellation
        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, null, null, null))
            .ReturnsAsync(true);

        var cancellationStatus = new CancellationStatus
        {
            MigrationId = TestMigrationId,
            ActiveScopes = new List<CancellationScope> { CancellationScope.Migration },
            Reason = "User requested cancellation"
        };

        _mockLiveCancellationManager
            .Setup(x => x.GetCancellationStatusAsync(TestMigrationId))
            .ReturnsAsync(cancellationStatus);

        // Act
        var result = await _activity.CheckLiveCancellationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.IsCancelled.Should().BeTrue();
        result.Scope.Should().Be(CancellationScope.Migration);
        result.Success.Should().BeTrue();
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CancellationDetails.Should().NotBeNull();
        result.CancellationDetails!.Reason.Should().Be("User requested cancellation");

        // Verify only migration scope was checked
        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, null, null, null), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.GetCancellationStatusAsync(TestMigrationId), Times.Once);
    }

    #endregion

    #region Helper Methods

    private CancellationCheckRequest CreateTestRequest(CancellationScope scope = CancellationScope.Migration)
    {
        return new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = scope,
            EntityType = TestEntityType,
            BatchId = TestBatchId,
            StoreId = TestStoreId
        };
    }

    #endregion
}