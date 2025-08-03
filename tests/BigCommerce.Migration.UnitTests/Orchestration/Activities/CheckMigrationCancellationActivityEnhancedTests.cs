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
/// TDD unit tests for enhanced CheckMigrationCancellationActivity methods
/// Task 7.2.5: Comprehensive testing for enhanced cancellation checking with LiveCancellationManager
/// CRITICAL: Tests backward compatibility, enhanced features, and performance requirements
/// </summary>
[Trait("Category", "LiveCancellation")]
[Trait("Component", "Activities")]
public class CheckMigrationCancellationActivityEnhancedTests
{
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ILiveCancellationManager> _mockLiveCancellationManager;
    private readonly Mock<ILogger<CheckMigrationCancellationActivity>> _mockLogger;
    private readonly CheckMigrationCancellationActivity _activity;
    private const string TestMigrationId = "test-migration-789";
    private const string TestEntityType = "categories";
    private const string TestBatchId = "batch-123";
    private const string TestStoreId = "store-456";

    public CheckMigrationCancellationActivityEnhancedTests()
    {
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockLiveCancellationManager = new Mock<ILiveCancellationManager>();
        _mockLogger = new Mock<ILogger<CheckMigrationCancellationActivity>>();
        _activity = new CheckMigrationCancellationActivity(
            _mockStorageService.Object,
            _mockLiveCancellationManager.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithAllDependencies_ShouldCreateInstance()
    {
        // Act
        var activity = new CheckMigrationCancellationActivity(
            _mockStorageService.Object,
            _mockLiveCancellationManager.Object,
            _mockLogger.Object);

        // Assert
        activity.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullLiveCancellationManager_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CheckMigrationCancellationActivity(_mockStorageService.Object, null!, _mockLogger.Object));

        exception.ParamName.Should().Be("liveCancellationManager");
    }

    #endregion

    #region Enhanced Cancellation Check Tests

    [Fact]
    public void CheckMigrationCancellationActivity_ShouldHave_EnhancedFunctionAttributes()
    {
        // Arrange & Act
        var activityType = typeof(CheckMigrationCancellationActivity);
        
        // Assert - Verify enhanced Azure Functions method structure and attributes
        var methods = activityType.GetMethods();
        var enhancedMethod = methods.FirstOrDefault(m => m.Name == "CheckMigrationCancellationEnhancedAsync");
        var compatMethod = methods.FirstOrDefault(m => m.Name == "CheckMigrationCancellationCompatAsync");
        
        enhancedMethod.Should().NotBeNull("Activity should have CheckMigrationCancellationEnhancedAsync method");
        enhancedMethod!.Should().BeDecoratedWith<Microsoft.Azure.Functions.Worker.FunctionAttribute>(
            "Enhanced method should have Function attribute for Azure Functions");

        compatMethod.Should().NotBeNull("Activity should have CheckMigrationCancellationCompatAsync method");
        compatMethod!.Should().BeDecoratedWith<Microsoft.Azure.Functions.Worker.FunctionAttribute>(
            "Compatibility method should have Function attribute for Azure Functions");
    }

    [Theory]
    [InlineData(CancellationScope.Migration)]
    [InlineData(CancellationScope.EntityType)]
    [InlineData(CancellationScope.Batch)]
    [InlineData(CancellationScope.Store)]
    public async Task CheckMigrationCancellationEnhancedAsync_WithAllScopes_ShouldCallLiveCancellationManager(CancellationScope scope)
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
            .Setup(x => x.IsCancelledAsync(TestMigrationId, scope, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _activity.CheckMigrationCancellationEnhancedAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.Scope.Should().Be(scope);
        result.IsCancelled.Should().BeFalse();
        result.Success.Should().BeTrue();
        
        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, scope, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckMigrationCancellationEnhancedAsync_WhenNotCancelled_ShouldReturnSuccessfulResult()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _activity.CheckMigrationCancellationEnhancedAsync(request);

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
    public async Task CheckMigrationCancellationEnhancedAsync_WhenCancelled_ShouldReturnDetailedResult()
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
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.EntityType, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        _mockLiveCancellationManager
            .Setup(x => x.GetCancellationStatusAsync(TestMigrationId))
            .ReturnsAsync(cancellationStatus);

        // Act
        var result = await _activity.CheckMigrationCancellationEnhancedAsync(request);

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

        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.EntityType, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.GetCancellationStatusAsync(TestMigrationId), Times.Once);
    }

    [Fact]
    public async Task CheckMigrationCancellationEnhancedAsync_ShouldClean_MigrationIdQuotes()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = $"\"{TestMigrationId}\"", // Migration ID with quotes
            Scope = CancellationScope.Migration
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())) // Should be called with cleaned ID
            .ReturnsAsync(false);

        // Act
        var result = await _activity.CheckMigrationCancellationEnhancedAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId); // Result should have cleaned ID
        result.IsCancelled.Should().BeFalse();
        
        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task CheckMigrationCancellationEnhancedAsync_ShouldComplete_WithinPerformanceTarget()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _activity.CheckMigrationCancellationEnhancedAsync(request);
        stopwatch.Stop();

        // Assert - Performance target is <1000ms
        result.ResponseTimeMs.Should().BeLessThan(1000, 
            "Enhanced cancellation check should complete within 1000ms performance target");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000,
            "Actual execution time should meet performance target");
    }

    [Fact]
    public async Task CheckMigrationCancellationEnhancedAsync_ShouldTrack_ResponseTime()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Batch,
            BatchId = TestBatchId
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Batch, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _activity.CheckMigrationCancellationEnhancedAsync(request);

        // Assert
        result.ResponseTimeMs.Should().BeGreaterThan(0, "Response time should be measured");
        result.ResponseTimeMs.Should().BeLessThan(1000, "Should meet performance target");
    }

    #endregion

    #region Backward Compatibility Tests

    [Fact]
    public async Task CheckMigrationCancellationCompatAsync_ShouldUse_EnhancedMethodInternally()
    {
        // Arrange
        var migrationId = TestMigrationId;

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _activity.CheckMigrationCancellationCompatAsync(migrationId);

        // Assert
        result.Should().BeFalse(); // Legacy boolean return
        
        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckMigrationCancellationCompatAsync_WhenCancelled_ShouldReturnTrue()
    {
        // Arrange
        var migrationId = TestMigrationId;

        var cancellationStatus = new CancellationStatus
        {
            MigrationId = TestMigrationId,
            ActiveScopes = new List<CancellationScope> { CancellationScope.Migration }
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        _mockLiveCancellationManager
            .Setup(x => x.GetCancellationStatusAsync(TestMigrationId))
            .ReturnsAsync(cancellationStatus);

        // Act
        var result = await _activity.CheckMigrationCancellationCompatAsync(migrationId);

        // Assert
        result.Should().BeTrue(); // Legacy boolean return indicating cancellation
    }

    [Fact]
    public async Task CheckMigrationCancellationCompatAsync_WhenErrorOccurs_ShouldReturnFalse()
    {
        // Arrange
        var migrationId = TestMigrationId;

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        // Act
        var result = await _activity.CheckMigrationCancellationCompatAsync(migrationId);

        // Assert
        result.Should().BeFalse(); // On error, assume not cancelled to continue processing
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CheckMigrationCancellationEnhancedAsync_WithNullRequest_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _activity.CheckMigrationCancellationEnhancedAsync(null!));

        exception.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task CheckMigrationCancellationEnhancedAsync_WithEmptyMigrationId_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = "",
            Scope = CancellationScope.Migration
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _activity.CheckMigrationCancellationEnhancedAsync(request));

        exception.ParamName.Should().Be("request");
        exception.Message.Should().Contain("Migration ID is required");
    }

    [Fact]
    public async Task CheckMigrationCancellationEnhancedAsync_WhenOperationCancelled_ShouldReturnCancelledResult()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        var cancellationToken = new CancellationToken(true);

        // Act
        var result = await _activity.CheckMigrationCancellationEnhancedAsync(request, cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.IsCancelled.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Enhanced operation was cancelled");
    }

    [Fact]
    public async Task CheckMigrationCancellationEnhancedAsync_WhenLiveCancellationManagerThrows_ShouldReturnErrorResult()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        var testException = new InvalidOperationException("Test enhanced check error");
        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(testException);

        // Act
        var result = await _activity.CheckMigrationCancellationEnhancedAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.IsCancelled.Should().BeFalse(); // On error, assume not cancelled to continue processing
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Test enhanced check error");
        result.ResponseTimeMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CheckMigrationCancellationEnhancedAsync_WhenGetStatusThrows_ShouldStillReturnBasicResult()
    {
        // Arrange
        var request = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.EntityType,
            EntityType = TestEntityType
        };

        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.EntityType, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        _mockLiveCancellationManager
            .Setup(x => x.GetCancellationStatusAsync(TestMigrationId))
            .ThrowsAsync(new InvalidOperationException("Status retrieval failed"));

        // Act
        var result = await _activity.CheckMigrationCancellationEnhancedAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.MigrationId.Should().Be(TestMigrationId);
        result.IsCancelled.Should().BeFalse(); // On error, assume not cancelled
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Status retrieval failed");
    }

    #endregion

    #region Integration with Legacy Tests

    [Fact]
    public async Task EnhancedMethods_ShouldCoexist_WithLegacyMethods()
    {
        // Arrange
        var legacyMigrationId = TestMigrationId;
        var enhancedRequest = new CancellationCheckRequest
        {
            MigrationId = TestMigrationId,
            Scope = CancellationScope.Migration
        };

        // Setup legacy storage service (for original method)
        _mockStorageService
            .Setup(x => x.GetCancellationTokenAsync(TestMigrationId))
            .ReturnsAsync((CancellationTokenEntry?)null);

        // Setup enhanced live cancellation manager
        _mockLiveCancellationManager
            .Setup(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act - Test both legacy and enhanced methods
        var legacyResult = await _activity.CheckMigrationCancellationAsync(legacyMigrationId);
        var enhancedResult = await _activity.CheckMigrationCancellationEnhancedAsync(enhancedRequest);
        var compatResult = await _activity.CheckMigrationCancellationCompatAsync(legacyMigrationId);

        // Assert - All methods should work independently
        legacyResult.Should().BeFalse(); // Legacy method result
        enhancedResult.Should().NotBeNull(); // Enhanced method result
        enhancedResult.IsCancelled.Should().BeFalse();
        compatResult.Should().BeFalse(); // Compatibility wrapper result

        // Verify correct service calls
        _mockStorageService.Verify(x => x.GetCancellationTokenAsync(TestMigrationId), Times.Once);
        _mockLiveCancellationManager.Verify(x => x.IsCancelledAsync(TestMigrationId, CancellationScope.Migration, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2)); // Enhanced + Compat
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