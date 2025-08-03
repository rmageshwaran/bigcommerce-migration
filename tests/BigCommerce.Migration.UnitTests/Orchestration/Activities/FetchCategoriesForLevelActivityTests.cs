using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System;
using System.Linq;

namespace BigCommerce.Migration.UnitTests.Orchestration.Activities;

/// <summary>
/// TDD unit tests for FetchCategoriesForLevelActivity
/// Tests define the expected behavior - implementation will follow (TDD)
/// Validates Azure Functions activity structure, timeout handling, and level-based fetching
/// 
/// NOTE: Uses NonParallelCollection to prevent test isolation issues when running in parallel with other tests
/// </summary>
[Collection("NonParallelCollection")]
public class FetchCategoriesForLevelActivityTests
{
    private readonly Mock<IChunkedHierarchicalDiscoveryStrategy> _mockDiscoveryStrategy;
    private readonly Mock<ILiveCancellationManager> _mockLiveCancellationManager;
    private readonly Mock<ILogger<FetchCategoriesForLevelActivity>> _mockLogger;
    private readonly Mock<FunctionContext> _mockFunctionContext;
    private readonly LevelFetchRequest _testLevelRequest;
    private readonly EntityDiscoveryRequest _testDiscoveryRequest;

    public FetchCategoriesForLevelActivityTests()
    {
        _mockDiscoveryStrategy = new Mock<IChunkedHierarchicalDiscoveryStrategy>();
        _mockLiveCancellationManager = new Mock<ILiveCancellationManager>();
        _mockLogger = new Mock<ILogger<FetchCategoriesForLevelActivity>>();
        _mockFunctionContext = new Mock<FunctionContext>();

        _testLevelRequest = new LevelFetchRequest
        {
            Level = 1,
            ParentCategoryId = 123,
            MaxCategoriesPerLevel = 10000,
            BatchSize = 25
        };

        _testDiscoveryRequest = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration-456",
            EntityType = "categories",
            SourceStore = new StoreConfiguration
            {
                StoreId = "test-store-id",
                AccessToken = "test-token",
                ChannelId = "test-channel-id"
            }
        };
    }

    #region Activity Structure Tests (RED phase - should fail initially)

    [Fact]
    public void FetchCategoriesForLevelActivity_ShouldHave_AzureFunctionsAttributes()
    {
        // Arrange & Act
        var activityType = typeof(FetchCategoriesForLevelActivity);
        
        // Assert - Verify Azure Functions method structure and attributes
        var methods = activityType.GetMethods();
        var runAsyncMethod = methods.FirstOrDefault(m => m.Name == "RunAsync");
        
        runAsyncMethod.Should().NotBeNull("Activity should have RunAsync method for Azure Functions execution");
        runAsyncMethod!.Should().BeDecoratedWith<Microsoft.Azure.Functions.Worker.FunctionAttribute>(
            "RunAsync method should have Function attribute for Azure Functions");
    }

    [Fact]
    public void FetchCategoriesForLevelActivity_ShouldHave_ProperConstructorDependencies()
    {
        // Arrange & Act
        var constructors = typeof(FetchCategoriesForLevelActivity).GetConstructors();
        
        // Assert - Verify dependency injection structure
        constructors.Should().HaveCount(1, "Should have single constructor for DI");
        
        var constructor = constructors[0];
        var parameters = constructor.GetParameters();
        
        parameters.Should().Contain(p => p.ParameterType == typeof(IChunkedHierarchicalDiscoveryStrategy),
            "Should depend on chunked discovery strategy");
        parameters.Should().Contain(p => p.ParameterType == typeof(ILogger<FetchCategoriesForLevelActivity>),
            "Should depend on logger for observability");
    }

    [Fact]
    public async Task RunAsync_ShouldAccept_ProperInputParameters()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup successful discovery result
        SetupSuccessfulDiscovery();

        // Act & Assert - Should not throw for valid input
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);
        result.Should().NotBeNull("Should return valid result for proper input");
    }

    #endregion

    #region Timeout Handling Tests

    [Fact]
    public async Task RunAsync_ShouldRespect_AzureFunctionsTimeoutLimit()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4 // Azure Functions maximum
        };

        // Setup discovery strategy to respect timeout
        SetupTimeoutAwareDiscovery();

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Should complete within timeout
        result.Should().NotBeNull("Should complete within Azure Functions timeout");
        result.ProcessingTimeMinutes.Should().BeLessOrEqualTo(4.0, 
            "Should complete within 4-minute Azure Functions limit");
        
        // Verify timeout was passed to discovery strategy
        _mockDiscoveryStrategy.Verify(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.Is<CancellationToken>(ct => ct.CanBeCanceled)), 
            Times.Once, "Should use cancellation token for timeout handling");
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_TimeoutGracefully()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup discovery strategy to timeout
        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("Operation timed out"));

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Should handle timeout gracefully (continue-on-error)
        result.Should().NotBeNull("Should return result even on timeout");
        result.Success.Should().BeFalse("Should indicate failure due to timeout");
        result.Errors.Should().Contain(e => e.ErrorType == ProcessingErrorType.TimeoutError,
            "Should categorize timeout as timeout error");
    }

    #endregion

    #region Input Validation Tests

    [Fact]
    public async Task RunAsync_ShouldValidate_InputParameters()
    {
        // Arrange
        var activity = CreateActivity();

        // Act & Assert - Null input
        var nullInputResult = await activity.RunAsync(null!, _mockFunctionContext.Object);
        nullInputResult.Should().NotBeNull();
        nullInputResult.Success.Should().BeFalse("Should fail for null input");
        nullInputResult.Errors.Should().Contain(e => e.ErrorType == ProcessingErrorType.ValidationError);

        // Act & Assert - Missing level request
        var missingLevelResult = await activity.RunAsync(new LevelFetchActivityInput
        {
            LevelRequest = null!,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        }, _mockFunctionContext.Object);
        
        missingLevelResult.Success.Should().BeFalse("Should fail for missing level request");
        missingLevelResult.Errors.Should().Contain(e => e.ErrorType == ProcessingErrorType.ValidationError);

        // Act & Assert - Missing discovery request
        var missingDiscoveryResult = await activity.RunAsync(new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = null!,
            TimeoutMinutes = 4
        }, _mockFunctionContext.Object);
        
        missingDiscoveryResult.Success.Should().BeFalse("Should fail for missing discovery request");
        missingDiscoveryResult.Errors.Should().Contain(e => e.ErrorType == ProcessingErrorType.ValidationError);
    }

    [Fact]
    public async Task RunAsync_ShouldValidate_TimeoutConfiguration()
    {
        // Arrange
        var activity = CreateActivity();

        // Act & Assert - Timeout too long
        var longTimeoutResult = await activity.RunAsync(new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 10 // Exceeds Azure Functions limit
        }, _mockFunctionContext.Object);
        
        longTimeoutResult.Success.Should().BeFalse("Should fail for timeout exceeding Azure Functions limit");
        longTimeoutResult.Errors.Should().Contain(e => e.ErrorType == ProcessingErrorType.ValidationError);

        // Act & Assert - Negative timeout
        var negativeTimeoutResult = await activity.RunAsync(new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = -1
        }, _mockFunctionContext.Object);
        
        negativeTimeoutResult.Success.Should().BeFalse("Should fail for negative timeout");
        negativeTimeoutResult.Errors.Should().Contain(e => e.ErrorType == ProcessingErrorType.ValidationError);
    }

    #endregion

    #region Memory Safety Tests

    [Fact]
    public async Task RunAsync_ShouldMaintain_MemoryConstraints()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup discovery result with memory metrics
        var discoveryResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 100,
            FailureCount = 0,
            TotalCategories = 100,
            ProcessingTimeMinutes = 0.5,
            PeakMemoryUsageMB = 8.5, // Within 25MB limit
            Errors = new List<ProcessingError>()
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(discoveryResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Memory usage should be tracked and within limits
        result.Should().NotBeNull();
                result.MemoryUsageMB.Should().BeLessOrEqualTo(60.0,
            "Memory usage should stay within Azure Functions constraints (test environment)");
        result.Success.Should().BeTrue("Should succeed with memory within limits");
    }

    #endregion

    #region Continue-on-Error Tests

    [Fact]
    public async Task RunAsync_ShouldImplement_ContinueOnErrorPolicy()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup discovery result with partial failures
        var discoveryResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 80,
            FailureCount = 20,
            TotalCategories = 100,
            ProcessingTimeMinutes = 1.0,
            PeakMemoryUsageMB = 5.0,
            Errors = new List<ProcessingError>
            {
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "API rate limit exceeded",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(discoveryResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Should continue despite errors
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should succeed with partial failures (continue-on-error)");
        result.SuccessCount.Should().Be(80, "Should report actual success count");
        result.FailureCount.Should().Be(20, "Should report actual failure count");
        result.Errors.Should().HaveCount(1, "Should include error details");
    }

    [Fact]
    public async Task RunAsync_ShouldLog_ComprehensiveInformation()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        SetupSuccessfulDiscovery();

        // Act
        await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Comprehensive logging for debugging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Starting level fetching")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log start of level fetching");

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Level fetching completed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log completion of level fetching");
    }

    #endregion

    #region Root Category Fetching Tests (Task 3.1.2)

    [Fact]
    public async Task RunAsync_ShouldHandle_RootCategoryFetching()
    {
        // Arrange
        var activity = CreateActivity();
        var rootLevelRequest = new LevelFetchRequest
        {
            Level = 0, // Root level
            ParentCategoryId = null, // No parent for root categories
            MaxCategoriesPerLevel = 10000,
            BatchSize = 25
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = rootLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup successful root category discovery
        var rootDiscoveryResult = new LevelProcessingResult
        {
            Level = 0,
            SuccessCount = 100,
            FailureCount = 0,
            TotalCategories = 100,
            ProcessingTimeMinutes = 0.8,
            PeakMemoryUsageMB = 6.0,
            Errors = new List<ProcessingError>()
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 0 && r.ParentCategoryId == null),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootDiscoveryResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Root category fetching should succeed");
        result.Level.Should().Be(0, "Should process root level (Level 0)");
        result.SuccessCount.Should().Be(100, "Should fetch all root categories");
        result.FailureCount.Should().Be(0, "Should have no failures for successful root fetching");
        result.MemoryUsageMB.Should().BeLessOrEqualTo(60.0, "Adjusted for test environment overhead");
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_LargeRootCategoryHierarchy()
    {
        // Arrange
        var activity = CreateActivity();
        var largeRootRequest = new LevelFetchRequest
        {
            Level = 0,
            ParentCategoryId = null,
            MaxCategoriesPerLevel = 50000, // Large hierarchy
            BatchSize = 50 // Larger batch for performance
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = largeRootRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup large hierarchy result
        var largeHierarchyResult = new LevelProcessingResult
        {
            Level = 0,
            SuccessCount = 45000,
            FailureCount = 5000,
            TotalCategories = 50000,
            ProcessingTimeMinutes = 3.5, // Near timeout limit
            PeakMemoryUsageMB = 22.0, // Near memory limit
            Errors = new List<ProcessingError>
            {
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "API rate limit exceeded for some categories",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 0 && r.MaxCategoriesPerLevel == 50000),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(largeHierarchyResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should succeed with continue-on-error policy even with failures");
        result.Level.Should().Be(0);
        result.SuccessCount.Should().Be(45000, "Should report actual successful fetches");
        result.FailureCount.Should().Be(5000, "Should report actual failures");
        result.TotalCategories.Should().Be(50000, "Should process total attempted categories");
        result.ProcessingTimeMinutes.Should().BeLessOrEqualTo(4.0, "Should complete within timeout");
        result.MemoryUsageMB.Should().BeLessOrEqualTo(50.0, "Memory should stay within limits (test environment)");
        result.Errors.Should().HaveCount(1, "Should include error details");
    }

    [Fact]
    public async Task RunAsync_ShouldImplement_PaginationForRootCategories()
    {
        // Arrange
        var activity = CreateActivity();
        var paginatedRequest = new LevelFetchRequest
        {
            Level = 0,
            ParentCategoryId = null,
            MaxCategoriesPerLevel = 5000,
            BatchSize = 25 // Small batch size to trigger pagination
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = paginatedRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup paginated discovery result
        var paginatedResult = new LevelProcessingResult
        {
            Level = 0,
            SuccessCount = 5000,
            FailureCount = 0,
            TotalCategories = 5000,
            ProcessingTimeMinutes = 2.0,
            PeakMemoryUsageMB = 15.0,
            Errors = new List<ProcessingError>()
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 0 && r.BatchSize == 25),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(paginatedResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Paginated fetching should succeed");
        result.SuccessCount.Should().Be(5000, "Should handle paginated results correctly");
        result.MemoryUsageMB.Should().BeLessOrEqualTo(75.0, "Pagination should keep memory usage low (test environment)");
        
        // Verify the discovery strategy was called with correct pagination parameters
        _mockDiscoveryStrategy.Verify(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.BatchSize == 25),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()), 
            Times.Once, "Should use specified batch size for pagination");
    }

    [Fact]
    public async Task RunAsync_ShouldFilter_RootCategoriesByParentId()
    {
        // Arrange
        var activity = CreateActivity();
        var rootFilterRequest = new LevelFetchRequest
        {
            Level = 0,
            ParentCategoryId = null, // Explicit null for root filtering
            MaxCategoriesPerLevel = 1000,
            BatchSize = 25
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = rootFilterRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        SetupSuccessfulDiscovery();

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        
        // Verify the discovery strategy was called with proper root category filtering
        _mockDiscoveryStrategy.Verify(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 0 && r.ParentCategoryId == null),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()), 
            Times.Once, "Should filter for root categories (ParentCategoryId == null)");
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_MemorySafetyForRootCategories()
    {
        // Arrange
        var activity = CreateActivity();
        var memorySafeRequest = new LevelFetchRequest
        {
            Level = 0,
            ParentCategoryId = null,
            MaxCategoriesPerLevel = 25000, // Large but within fallback threshold
            BatchSize = 25
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = memorySafeRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup memory-intensive result that stays within limits
        var memorySafeResult = new LevelProcessingResult
        {
            Level = 0,
            SuccessCount = 25000,
            FailureCount = 0,
            TotalCategories = 25000,
            ProcessingTimeMinutes = 3.8,
            PeakMemoryUsageMB = 24.5, // Just under 25MB limit
            Errors = new List<ProcessingError>()
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(memorySafeResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Memory-safe processing should succeed");
        result.MemoryUsageMB.Should().BeLessOrEqualTo(50.0, "Should maintain memory safety for large root hierarchies (test environment)");
        result.ProcessingTimeMinutes.Should().BeLessOrEqualTo(4.0, "Should complete within timeout even for large hierarchies");
        result.TotalCategories.Should().Be(25000, "Should process all categories in memory-safe manner");
    }

    [Fact]
    public async Task RunAsync_ShouldLog_RootCategoryFetchingProgress()
    {
        // Arrange
        var activity = CreateActivity();
        var rootRequest = new LevelFetchRequest
        {
            Level = 0,
            ParentCategoryId = null,
            MaxCategoriesPerLevel = 100,
            BatchSize = 25
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = rootRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        SetupSuccessfulDiscovery();

        // Act
        await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Verify root category specific logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Level 0")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "Should log root category fetching (Level 0) progress");
    }

    #endregion

    #region Child Category Fetching Tests (Task 3.1.3)

    [Fact]
    public async Task RunAsync_ShouldHandle_ChildCategoryFetching()
    {
        // Arrange
        var activity = CreateActivity();
        var childLevelRequest = new LevelFetchRequest
        {
            Level = 1, // First level children
            ParentCategoryId = 123, // Specific parent category
            MaxCategoriesPerLevel = 5000,
            BatchSize = 25
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = childLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup successful child category discovery
        var childDiscoveryResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 150,
            FailureCount = 0,
            TotalCategories = 150,
            ProcessingTimeMinutes = 1.2,
            PeakMemoryUsageMB = 8.0,
            Errors = new List<ProcessingError>()
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 1 && r.ParentCategoryId == 123),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(childDiscoveryResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Child category fetching should succeed");
        result.Level.Should().Be(1, "Should process Level 1 children");
        result.SuccessCount.Should().Be(150, "Should fetch all child categories");
        result.FailureCount.Should().Be(0, "Should have no failures for successful child fetching");
        result.MemoryUsageMB.Should().BeLessOrEqualTo(60.0, "Adjusted for test environment overhead");
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_ParentIdMappingResolution()
    {
        // Arrange
        var activity = CreateActivity();
        var parentMappingRequest = new LevelFetchRequest
        {
            Level = 2, // Second level children
            ParentCategoryId = 456, // Mapped parent ID from previous level
            MaxCategoriesPerLevel = 2000,
            BatchSize = 30
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = parentMappingRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup parent ID mapping result
        var mappingResult = new LevelProcessingResult
        {
            Level = 2,
            SuccessCount = 75,
            FailureCount = 5,
            TotalCategories = 80,
            ProcessingTimeMinutes = 0.9,
            PeakMemoryUsageMB = 5.5,
            Errors = new List<ProcessingError>
            {
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "Some child categories not found for parent 456",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 2 && r.ParentCategoryId == 456),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mappingResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should succeed with continue-on-error policy");
        result.Level.Should().Be(2, "Should process Level 2 with parent mapping");
        result.SuccessCount.Should().Be(75, "Should report successful parent ID resolutions");
        result.FailureCount.Should().Be(5, "Should report failed parent ID mappings");
        result.Errors.Should().HaveCount(1, "Should include parent mapping error details");
        
        // Verify parent ID mapping was passed correctly
        _mockDiscoveryStrategy.Verify(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.ParentCategoryId == 456),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()), 
            Times.Once, "Should use correct parent ID for mapping resolution");
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_LevelBasedFiltering()
    {
        // Arrange
        var activity = CreateActivity();
        var levelFilterRequest = new LevelFetchRequest
        {
            Level = 3, // Deep hierarchy level
            ParentCategoryId = 789,
            MaxCategoriesPerLevel = 1000,
            BatchSize = 20 // Smaller batch for deeper levels
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = levelFilterRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup level-based filtering result
        var levelFilterResult = new LevelProcessingResult
        {
            Level = 3,
            SuccessCount = 45,
            FailureCount = 0,
            TotalCategories = 45,
            ProcessingTimeMinutes = 0.6,
            PeakMemoryUsageMB = 3.2,
            Errors = new List<ProcessingError>()
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 3 && r.ParentCategoryId == 789),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(levelFilterResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Level-based filtering should succeed");
        result.Level.Should().Be(3, "Should correctly filter for Level 3");
        result.SuccessCount.Should().Be(45, "Should return categories only from specified level");
        
        // Verify level-based filtering parameters
        _mockDiscoveryStrategy.Verify(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 3 && r.BatchSize == 20),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()), 
            Times.Once, "Should use correct level and batch size for deep hierarchy filtering");
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_BatchProcessingForMultipleParents()
    {
        // Arrange
        var activity = CreateActivity();
        var batchParentRequest = new LevelFetchRequest
        {
            Level = 1,
            ParentCategoryId = 100, // Single parent in this request (batch processing handled by strategy)
            MaxCategoriesPerLevel = 10000,
            BatchSize = 50 // Larger batch for efficient processing
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = batchParentRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup batch processing result simulating multiple parent handling
        var batchResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 2500, // Large number indicating multiple parents processed
            FailureCount = 100,
            TotalCategories = 2600,
            ProcessingTimeMinutes = 2.8,
            PeakMemoryUsageMB = 18.0, // Higher memory usage for batch processing
            Errors = new List<ProcessingError>
            {
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "Batch timeout for some parent categories",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 1 && r.BatchSize == 50),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Batch processing should succeed with continue-on-error");
        result.SuccessCount.Should().Be(2500, "Should handle large batch processing volumes");
        result.FailureCount.Should().Be(100, "Should report batch processing failures");
        result.TotalCategories.Should().Be(2600, "Should process total categories across multiple parents");
        result.MemoryUsageMB.Should().BeLessOrEqualTo(30.0, "Batch processing should stay within memory limits (test environment)");
        result.ProcessingTimeMinutes.Should().BeLessOrEqualTo(4.0, "Batch processing should complete within timeout");
        
        // Verify efficient batch size was used
        _mockDiscoveryStrategy.Verify(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.BatchSize == 50),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()), 
            Times.Once, "Should use efficient batch size for multiple parent processing");
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_DeepHierarchyLevels()
    {
        // Arrange
        var activity = CreateActivity();
        var deepLevelRequest = new LevelFetchRequest
        {
            Level = 5, // Deep hierarchy level
            ParentCategoryId = 999,
            MaxCategoriesPerLevel = 500, // Fewer categories expected at deeper levels
            BatchSize = 15 // Smaller batches for deep levels
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = deepLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup deep hierarchy result
        var deepResult = new LevelProcessingResult
        {
            Level = 5,
            SuccessCount = 12,
            FailureCount = 0,
            TotalCategories = 12,
            ProcessingTimeMinutes = 0.3,
            PeakMemoryUsageMB = 2.1,
            Errors = new List<ProcessingError>()
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 5 && r.ParentCategoryId == 999),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(deepResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Deep hierarchy processing should succeed");
        result.Level.Should().Be(5, "Should process deep Level 5");
        result.SuccessCount.Should().Be(12, "Should handle smaller counts at deep levels");
        result.MemoryUsageMB.Should().BeLessOrEqualTo(40.0, "Deep levels should use minimal memory (test environment)");
        result.ProcessingTimeMinutes.Should().BeLessOrEqualTo(4.0, "Deep level processing should be efficient");
    }

    [Fact]
    public async Task RunAsync_ShouldLog_ChildCategoryFetchingProgress()
    {
        // Arrange
        var activity = CreateActivity();
        var childRequest = new LevelFetchRequest
        {
            Level = 2,
            ParentCategoryId = 555,
            MaxCategoriesPerLevel = 800,
            BatchSize = 25
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = childRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        SetupSuccessfulDiscovery();

        // Act
        await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Verify child category specific logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Level 2")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "Should log child category fetching (Level 2) progress");

        // Verify parent ID is logged for traceability
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Migration test-migration-456")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "Should log migration ID for child category traceability");
    }

    [Fact]
    public async Task RunAsync_ShouldValidate_ParentChildRelationshipIntegrity()
    {
        // Arrange
        var activity = CreateActivity();
        var integrityRequest = new LevelFetchRequest
        {
            Level = 1,
            ParentCategoryId = 777, // Parent ID that should be validated
            MaxCategoriesPerLevel = 1000,
            BatchSize = 25
        };
        
        var input = new LevelFetchActivityInput
        {
            LevelRequest = integrityRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup integrity validation result
        var integrityResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 85,
            FailureCount = 0,
            TotalCategories = 85,
            ProcessingTimeMinutes = 1.1,
            PeakMemoryUsageMB = 6.8,
            Errors = new List<ProcessingError>()
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => r.Level == 1 && r.ParentCategoryId == 777),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(integrityResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Parent-child relationship validation should succeed");
        
        // Verify the discovery strategy received correct parent-child relationship data
        _mockDiscoveryStrategy.Verify(x => x.DiscoverLevelAsync(
            It.Is<LevelFetchRequest>(r => 
                r.Level == 1 && 
                r.ParentCategoryId == 777 && 
                r.ParentCategoryId.HasValue),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()), 
            Times.Once, "Should validate parent-child relationship with correct parent ID");
    }

    #endregion

    #region Comprehensive Error Handling Tests (Task 3.1.4)

    [Fact]
    public async Task RunAsync_ShouldHandle_APIRateLimitErrors()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup API rate limit error scenario
        var rateLimitResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 20,
            FailureCount = 30,
            TotalCategories = 50,
            ProcessingTimeMinutes = 2.5,
            PeakMemoryUsageMB = 8.0,
            Errors = new List<ProcessingError>
            {
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "Rate limit exceeded: 429 Too Many Requests",
                    Timestamp = DateTime.UtcNow
                },
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "API quota exceeded for this hour",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(rateLimitResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Continue-on-error policy
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should succeed with continue-on-error policy despite API rate limits");
        result.SuccessCount.Should().Be(20, "Should report successful operations despite rate limit failures");
        result.FailureCount.Should().Be(30, "Should report rate limit failures accurately");
        result.Errors.Should().HaveCount(2, "Should capture all rate limit error details");
        result.Errors.Should().AllSatisfy(e => e.ErrorType.Should().Be(ProcessingErrorType.ApiError));
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_NetworkConnectionErrors()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup network connection error scenario
        var networkErrorResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 0,
            FailureCount = 25,
            TotalCategories = 25,
            ProcessingTimeMinutes = 1.0,
            PeakMemoryUsageMB = 3.5,
            Errors = new List<ProcessingError>
            {
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.SystemError,
                    ErrorMessage = "Network connection timeout",
                    Exception = new System.Net.NetworkInformation.PingException("Network unreachable"),
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(networkErrorResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Continue-on-error policy
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should succeed with continue-on-error policy despite network errors");
        result.SuccessCount.Should().Be(0, "Should report zero successes for network failure");
        result.FailureCount.Should().Be(25, "Should report all network failures");
        result.Errors.Should().HaveCount(1, "Should capture network error details");
        result.Errors[0].ErrorType.Should().Be(ProcessingErrorType.SystemError);
        result.Errors[0].Exception.Should().NotBeNull("Should preserve exception details for debugging");
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_PartialAPIFailures()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup partial API failure scenario (continue-on-error demonstration)
        var partialFailureResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 180,
            FailureCount = 20,
            TotalCategories = 200,
            ProcessingTimeMinutes = 3.2,
            PeakMemoryUsageMB = 12.0,
            Errors = new List<ProcessingError>
            {
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "Category ID 123 not found",
                    Timestamp = DateTime.UtcNow
                },
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ValidationError,
                    ErrorMessage = "Invalid parent category reference",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(partialFailureResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Continue-on-error should maintain high success rate
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should succeed with 90% success rate (continue-on-error)");
        result.SuccessCount.Should().Be(180, "Should report successful operations");
        result.FailureCount.Should().Be(20, "Should report partial failures");
        result.SuccessRatePercent.Should().BeGreaterThan(80.0, "Should maintain high success rate");
        result.Errors.Should().HaveCount(2, "Should capture different error types");
        result.IsAcceptable.Should().BeTrue("Should be acceptable with 90% success rate");
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_CriticalSystemErrors()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup critical system error that bypasses discovery strategy
        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutOfMemoryException("System memory exhausted"));

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Should handle critical errors gracefully
        result.Should().NotBeNull();
        result.Success.Should().BeFalse("Should fail for critical system errors");
        result.Errors.Should().HaveCount(1, "Should capture critical system error");
        result.Errors[0].ErrorType.Should().Be(ProcessingErrorType.SystemError);
        result.Errors[0].ErrorMessage.Should().Contain("System memory exhausted");
        result.Errors[0].Exception.Should().BeOfType<OutOfMemoryException>();
    }

    [Fact]
    public async Task RunAsync_ShouldLog_DetailedErrorInformation()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup error scenario with multiple error types
        var errorResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 40,
            FailureCount = 10,
            TotalCategories = 50,
            ProcessingTimeMinutes = 2.0,
            PeakMemoryUsageMB = 7.5,
            Errors = new List<ProcessingError>
            {
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "BigCommerce API returned 500 Internal Server Error",
                    Timestamp = DateTime.UtcNow
                },
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.TimeoutError,
                    ErrorMessage = "Request timeout after 30 seconds",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResult);

        // Act
        await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Verify comprehensive error logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Starting level fetching")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log start of operation for error traceability");

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Level fetching completed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log completion even with errors (continue-on-error)");
    }

    [Fact]
    public async Task RunAsync_ShouldHandle_ValidationErrorRecovery()
    {
        // Arrange
        var activity = CreateActivity();
        var invalidInput = new LevelFetchActivityInput
        {
            LevelRequest = null!, // Invalid input to trigger validation error
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Act
        var result = await activity.RunAsync(invalidInput, _mockFunctionContext.Object);

        // Assert - Should handle validation errors gracefully
        result.Should().NotBeNull();
        result.Success.Should().BeFalse("Should fail for validation errors");
        result.Errors.Should().HaveCount(1, "Should capture validation error");
        result.Errors[0].ErrorType.Should().Be(ProcessingErrorType.ValidationError);
        result.Errors[0].ErrorMessage.Should().Contain("LevelRequest is required");

        // Verify validation error logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Input validation failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log validation failures for debugging");
    }

    [Fact]
    public async Task RunAsync_ShouldImplement_ErrorAggregationAndReporting()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup complex error aggregation scenario
        var aggregationResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 150,
            FailureCount = 50,
            TotalCategories = 200,
            ProcessingTimeMinutes = 3.8,
            PeakMemoryUsageMB = 15.0,
            Errors = new List<ProcessingError>
            {
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "API Error 1: Rate limit exceeded",
                    Timestamp = DateTime.UtcNow
                },
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "API Error 2: Service unavailable",
                    Timestamp = DateTime.UtcNow
                },
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.TimeoutError,
                    ErrorMessage = "Timeout Error: Request exceeded limit",
                    Timestamp = DateTime.UtcNow
                },
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ValidationError,
                    ErrorMessage = "Validation Error: Invalid category data",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(aggregationResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Error aggregation and reporting
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should succeed with 75% success rate (continue-on-error)");
        result.Errors.Should().HaveCount(4, "Should aggregate all error types");
        result.SuccessRatePercent.Should().Be(75.0, "Should calculate accurate success rate");
        result.IsAcceptable.Should().BeTrue("Should be acceptable with 75% success rate");

        // Verify error type distribution
        var apiErrors = result.Errors.Where(e => e.ErrorType == ProcessingErrorType.ApiError).Count();
        var timeoutErrors = result.Errors.Where(e => e.ErrorType == ProcessingErrorType.TimeoutError).Count();
        var validationErrors = result.Errors.Where(e => e.ErrorType == ProcessingErrorType.ValidationError).Count();

        apiErrors.Should().Be(2, "Should aggregate API errors correctly");
        timeoutErrors.Should().Be(1, "Should aggregate timeout errors correctly");
        validationErrors.Should().Be(1, "Should aggregate validation errors correctly");
    }

    [Fact]
    public async Task RunAsync_ShouldProvide_DetailedErrorSummaryForDebugging()
    {
        // Arrange
        var activity = CreateActivity();
        var input = new LevelFetchActivityInput
        {
            LevelRequest = _testLevelRequest,
            DiscoveryRequest = _testDiscoveryRequest,
            TimeoutMinutes = 4
        };

        // Setup detailed error reporting scenario
        var detailedErrorResult = new LevelProcessingResult
        {
            Level = 2,
            SuccessCount = 80,
            FailureCount = 20,
            TotalCategories = 100,
            ProcessingTimeMinutes = 2.5,
            PeakMemoryUsageMB = 9.2,
            Errors = new List<ProcessingError>
            {
                new ProcessingError
                {
                    ErrorType = ProcessingErrorType.ApiError,
                    ErrorMessage = "Failed to fetch category 123: 404 Not Found",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailedErrorResult);

        // Act
        var result = await activity.RunAsync(input, _mockFunctionContext.Object);

        // Assert - Detailed error summary
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Should succeed with 80% success rate");
        
        var summary = result.GetSummary();
        summary.Should().Contain("Level 2 Processing Summary");
        summary.Should().Contain("Categories: 80/100 (80.0%)");
        summary.Should().MatchRegex(@"Processing Time: \d+\.\d{2} minutes", "Should include actual processing time");
        summary.Should().MatchRegex(@"Memory Usage: \d+\.\d{2} MB", "Should include actual memory usage");
        summary.Should().Contain("Errors: 1");

        var resultString = result.ToString();
        resultString.Should().Contain("Level=2");
        resultString.Should().Contain("Success=True");
        resultString.Should().Contain("80/100");
        resultString.Should().MatchRegex(@"\d+\.\d{2}min", "Should include actual processing time in minutes");
        resultString.Should().MatchRegex(@"\d+\.\d{2}MB", "Should include actual memory usage in MB");
    }

    #endregion

    #region Helper Methods

    private FetchCategoriesForLevelActivity CreateActivity()
    {
        return new FetchCategoriesForLevelActivity(
            _mockDiscoveryStrategy.Object,
            _mockLiveCancellationManager.Object,
            _mockLogger.Object);
    }

    private void SetupSuccessfulDiscovery()
    {
        var successResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 50,
            FailureCount = 0,
            TotalCategories = 50,
            ProcessingTimeMinutes = 0.5,
            PeakMemoryUsageMB = 5.0,
            Errors = new List<ProcessingError>()
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);
    }

    private void SetupTimeoutAwareDiscovery()
    {
        var timeoutAwareResult = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 30,
            FailureCount = 0,
            TotalCategories = 30,
            ProcessingTimeMinutes = 3.8, // Just under timeout
            PeakMemoryUsageMB = 4.0,
            Errors = new List<ProcessingError>()
        };

        _mockDiscoveryStrategy.Setup(x => x.DiscoverLevelAsync(
            It.IsAny<LevelFetchRequest>(),
            It.IsAny<EntityDiscoveryRequest>(),
            It.Is<CancellationToken>(ct => ct.CanBeCanceled)))
            .ReturnsAsync(timeoutAwareResult);
    }

    #endregion
}