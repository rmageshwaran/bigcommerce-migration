using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using Xunit;
using System.Collections.Generic;
using System;
using System.Linq;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// TDD unit tests for Level Processing Result Models
/// Tests define the expected behavior - implementation will follow (TDD)
/// Validates continue-on-error policy compliance and performance metrics collection
/// </summary>
public class LevelProcessingModelsTests
{
    #region LevelProcessingResult Class Tests (RED phase - should fail initially)

    [Fact]
    public void LevelProcessingResult_ShouldHave_RequiredProperties()
    {
        // Act - Create instance
        var result = new LevelProcessingResult();
        
        // Assert - Verify all required properties exist
        result.Should().NotBeNull();
        
        // Core processing properties
        var levelProperty = result.GetType().GetProperty("Level");
        levelProperty.Should().NotBeNull("Level property should exist");
        
        var successCountProperty = result.GetType().GetProperty("SuccessCount");
        successCountProperty.Should().NotBeNull("SuccessCount property should exist");
        
        var failureCountProperty = result.GetType().GetProperty("FailureCount");
        failureCountProperty.Should().NotBeNull("FailureCount property should exist");
        
        var totalCategoriesProperty = result.GetType().GetProperty("TotalCategories");
        totalCategoriesProperty.Should().NotBeNull("TotalCategories property should exist");
        
        var errorsProperty = result.GetType().GetProperty("Errors");
        errorsProperty.Should().NotBeNull("Errors property should exist");
        
        // Performance metrics properties
        var processingTimeProperty = result.GetType().GetProperty("ProcessingTimeMinutes");
        processingTimeProperty.Should().NotBeNull("ProcessingTimeMinutes property should exist");
        
        var throughputProperty = result.GetType().GetProperty("ThroughputCategoriesPerMinute");
        throughputProperty.Should().NotBeNull("ThroughputCategoriesPerMinute property should exist");
        
        var memoryUsageProperty = result.GetType().GetProperty("PeakMemoryUsageMB");
        memoryUsageProperty.Should().NotBeNull("PeakMemoryUsageMB property should exist");
    }

    [Fact]
    public void LevelProcessingResult_ShouldCalculate_SuccessRate()
    {
        // Arrange
        var result = new LevelProcessingResult
        {
            SuccessCount = 75,
            FailureCount = 25,
            TotalCategories = 100
        };

        // Act & Assert
        var successRate = result.SuccessRate;
        successRate.Should().Be(0.75, "Success rate should be 75/100 = 0.75");
        
        // Test edge cases
        var emptyResult = new LevelProcessingResult
        {
            SuccessCount = 0,
            FailureCount = 0,
            TotalCategories = 0
        };
        emptyResult.SuccessRate.Should().Be(1.0, "Empty result should have 100% success rate (no failures)");
    }

    [Fact]
    public void LevelProcessingResult_ShouldCalculate_FailureRate()
    {
        // Arrange
        var result = new LevelProcessingResult
        {
            SuccessCount = 80,
            FailureCount = 20,
            TotalCategories = 100
        };

        // Act & Assert
        var failureRate = result.FailureRate;
        failureRate.Should().Be(0.20, "Failure rate should be 20/100 = 0.20");
    }

    [Fact]
    public void LevelProcessingResult_ShouldSupport_ContinueOnErrorPolicy()
    {
        // Arrange - Simulate processing with some failures
        var result = new LevelProcessingResult
        {
            Level = 1,
            SuccessCount = 950,
            FailureCount = 50,
            TotalCategories = 1000,
            ProcessingTimeMinutes = 2.5
        };

        // Act & Assert - Continue-on-error compliance
        result.IsCompleted.Should().BeTrue("Processing should complete despite failures (continue-on-error)");
        result.HasFailures.Should().BeTrue("Should track that failures occurred");
        result.SuccessCount.Should().BeGreaterThan(0, "Should have some successes despite failures");
        result.FailureCount.Should().BeGreaterThan(0, "Should track failure count");
        
        // Verify it doesn't stop processing
        (result.SuccessCount + result.FailureCount).Should().Be(result.TotalCategories, 
            "All categories should be processed (success + failure = total)");
    }

    [Fact]
    public void LevelProcessingResult_ShouldCalculate_PerformanceMetrics()
    {
        // Arrange
        var result = new LevelProcessingResult
        {
            SuccessCount = 1000,
            FailureCount = 0,
            TotalCategories = 1000,
            ProcessingTimeMinutes = 2.0,
            PeakMemoryUsageMB = 45.5
        };

        // Act & Assert - Performance calculations
        result.ThroughputCategoriesPerMinute.Should().Be(500.0, "Throughput should be 1000 categories / 2 minutes = 500/min");
        result.PeakMemoryUsageMB.Should().BeLessThan(50.0, "Memory usage should be within safe limits");
        result.IsWithinPerformanceTargets.Should().BeTrue("Should meet performance targets");
    }

    [Fact]
    public void LevelProcessingResult_ShouldHave_HelperMethods()
    {
        // Act - Verify helper methods exist
        var resultType = typeof(LevelProcessingResult);
        
        // Assert - Verify required helper methods
        var addErrorMethod = resultType.GetMethod("AddError");
        addErrorMethod.Should().NotBeNull("AddError method should exist");
        
        var addSuccessMethod = resultType.GetMethod("AddSuccess");
        addSuccessMethod.Should().NotBeNull("AddSuccess method should exist");
        
        var addFailureMethod = resultType.GetMethod("AddFailure");
        addFailureMethod.Should().NotBeNull("AddFailure method should exist");
        
        var getSummaryMethod = resultType.GetMethod("GetSummary");
        getSummaryMethod.Should().NotBeNull("GetSummary method should exist");
    }

    [Fact]
    public void LevelProcessingResult_AddError_ShouldUpdate_Counters()
    {
        // Arrange
        var result = new LevelProcessingResult();
        var error = new ProcessingError
        {
            CategoryId = "123",
            ErrorMessage = "Test error",
            ErrorType = ProcessingErrorType.ApiError
        };

        // Act
        result.AddError(error);
        result.AddError(error); // Add second error

        // Assert
        result.FailureCount.Should().Be(2, "Should track failure count");
        result.Errors.Should().HaveCount(2, "Should store all errors");
        result.HasFailures.Should().BeTrue("Should indicate failures exist");
    }

    [Fact]
    public void LevelProcessingResult_AddSuccess_ShouldUpdate_SuccessCount()
    {
        // Arrange
        var result = new LevelProcessingResult();

        // Act
        result.AddSuccess();
        result.AddSuccess();
        result.AddSuccess();

        // Assert
        result.SuccessCount.Should().Be(3, "Should track success count");
        result.FailureCount.Should().Be(0, "Should have no failures");
    }

    #endregion

    #region ProcessingError Model Tests (RED phase - should fail initially)

    [Fact]
    public void ProcessingError_ShouldHave_RequiredProperties()
    {
        // Act - Create instance
        var error = new ProcessingError();
        
        // Assert - Verify all required properties exist
        error.Should().NotBeNull();
        
        // Core error properties
        var categoryIdProperty = error.GetType().GetProperty("CategoryId");
        categoryIdProperty.Should().NotBeNull("CategoryId property should exist");
        
        var errorMessageProperty = error.GetType().GetProperty("ErrorMessage");
        errorMessageProperty.Should().NotBeNull("ErrorMessage property should exist");
        
        var errorTypeProperty = error.GetType().GetProperty("ErrorType");
        errorTypeProperty.Should().NotBeNull("ErrorType property should exist");
        
        var timestampProperty = error.GetType().GetProperty("Timestamp");
        timestampProperty.Should().NotBeNull("Timestamp property should exist");
        
        // Debugging context properties
        var exceptionProperty = error.GetType().GetProperty("Exception");
        exceptionProperty.Should().NotBeNull("Exception property should exist");
        
        var apiResponseProperty = error.GetType().GetProperty("ApiResponse");
        apiResponseProperty.Should().NotBeNull("ApiResponse property should exist");
        
        var categoryDataProperty = error.GetType().GetProperty("CategoryData");
        categoryDataProperty.Should().NotBeNull("CategoryData property should exist");
        
        var retryCountProperty = error.GetType().GetProperty("RetryCount");
        retryCountProperty.Should().NotBeNull("RetryCount property should exist");
    }

    [Fact]
    public void ProcessingError_ShouldSupport_ErrorCategorization()
    {
        // Arrange & Act - Test different error types
        var apiError = new ProcessingError
        {
            ErrorType = ProcessingErrorType.ApiError,
            ErrorMessage = "API rate limit exceeded"
        };
        
        var validationError = new ProcessingError
        {
            ErrorType = ProcessingErrorType.ValidationError,
            ErrorMessage = "Invalid category name"
        };
        
        var systemError = new ProcessingError
        {
            ErrorType = ProcessingErrorType.SystemError,
            ErrorMessage = "Out of memory"
        };

        // Assert - Verify error categorization works
        apiError.ErrorType.Should().Be(ProcessingErrorType.ApiError);
        validationError.ErrorType.Should().Be(ProcessingErrorType.ValidationError);
        systemError.ErrorType.Should().Be(ProcessingErrorType.SystemError);
        
        // Verify categorization is helpful for analysis
        apiError.IsRetryable.Should().BeTrue("API errors should be retryable");
        validationError.IsRetryable.Should().BeFalse("Validation errors should not be retryable");
        systemError.IsRetryable.Should().BeFalse("System errors should not be retryable");
    }

    [Fact]
    public void ProcessingError_ShouldPreserve_FullDebuggingContext()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var categoryData = new Dictionary<string, object>
        {
            { "name", "Test Category" },
            { "parent_id", 123 }
        };

        // Act
        var error = new ProcessingError
        {
            CategoryId = "456",
            ErrorMessage = "Failed to create category",
            ErrorType = ProcessingErrorType.ApiError,
            Exception = exception,
            ApiResponse = "{ \"error\": \"validation_failed\" }",
            CategoryData = categoryData,
            RetryCount = 2,
            Timestamp = DateTime.UtcNow
        };

        // Assert - Verify all debugging context is preserved
        error.CategoryId.Should().Be("456");
        error.Exception.Should().Be(exception);
        error.ApiResponse.Should().Contain("validation_failed");
        error.CategoryData.Should().ContainKey("name");
        error.CategoryData.Should().ContainKey("parent_id");
        error.RetryCount.Should().Be(2);
        error.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ProcessingError_ShouldHave_HelperMethods()
    {
        // Act - Verify helper methods exist
        var errorType = typeof(ProcessingError);
        
        // Assert - Verify required helper methods
        var toStringMethod = errorType.GetMethod("ToString");
        toStringMethod.Should().NotBeNull("ToString method should exist for logging");
        
        var getDetailedInfoMethod = errorType.GetMethod("GetDetailedInfo");
        getDetailedInfoMethod.Should().NotBeNull("GetDetailedInfo method should exist for debugging");
    }

    #endregion

    #region ProcessingErrorType Enum Tests

    [Fact]
    public void ProcessingErrorType_ShouldHave_RequiredValues()
    {
        // Act & Assert - Verify enum values exist
        var errorTypes = Enum.GetValues<ProcessingErrorType>();
        
        errorTypes.Should().Contain(ProcessingErrorType.ApiError, "Should have ApiError type");
        errorTypes.Should().Contain(ProcessingErrorType.ValidationError, "Should have ValidationError type");
        errorTypes.Should().Contain(ProcessingErrorType.SystemError, "Should have SystemError type");
        errorTypes.Should().Contain(ProcessingErrorType.TimeoutError, "Should have TimeoutError type");
        errorTypes.Should().Contain(ProcessingErrorType.NetworkError, "Should have NetworkError type");
    }

    #endregion

    #region Error Aggregation and Analysis Tests

    [Fact]
    public void LevelProcessingResult_ShouldSupport_ErrorAggregation()
    {
        // Arrange
        var result = new LevelProcessingResult();
        
        // Add various types of errors
        result.AddError(new ProcessingError { ErrorType = ProcessingErrorType.ApiError, CategoryId = "1" });
        result.AddError(new ProcessingError { ErrorType = ProcessingErrorType.ApiError, CategoryId = "2" });
        result.AddError(new ProcessingError { ErrorType = ProcessingErrorType.ValidationError, CategoryId = "3" });
        result.AddError(new ProcessingError { ErrorType = ProcessingErrorType.SystemError, CategoryId = "4" });

        // Act
        var errorsByType = result.GetErrorsByType();
        var errorSummary = result.GetErrorSummary();

        // Assert - Verify error aggregation works
        errorsByType.Should().ContainKey(ProcessingErrorType.ApiError);
        errorsByType[ProcessingErrorType.ApiError].Should().HaveCount(2, "Should have 2 API errors");
        errorsByType[ProcessingErrorType.ValidationError].Should().HaveCount(1, "Should have 1 validation error");
        
        errorSummary.Should().Contain("API Error: 2");
        errorSummary.Should().Contain("Validation Error: 1");
        errorSummary.Should().Contain("System Error: 1");
    }

    [Fact]
    public void LevelProcessingResult_ShouldCalculate_AccurateSuccessRates()
    {
        // Arrange - Test various scenarios
        var scenarios = new[]
        {
            new { Success = 1000, Failure = 0, Expected = 1.0 },
            new { Success = 950, Failure = 50, Expected = 0.95 },
            new { Success = 0, Failure = 100, Expected = 0.0 },
            new { Success = 500, Failure = 500, Expected = 0.5 }
        };

        foreach (var scenario in scenarios)
        {
            // Act
            var result = new LevelProcessingResult
            {
                SuccessCount = scenario.Success,
                FailureCount = scenario.Failure,
                TotalCategories = scenario.Success + scenario.Failure
            };

            // Assert
            result.SuccessRate.Should().BeApproximately(scenario.Expected, 0.001, 
                $"Success rate should be accurate for {scenario.Success}S/{scenario.Failure}F");
        }
    }

    [Fact]
    public void LevelProcessingResult_ShouldMeet_PerformanceTargets()
    {
        // Arrange - Test performance target validation
        var goodPerformance = new LevelProcessingResult
        {
            TotalCategories = 1000,
            ProcessingTimeMinutes = 1.0,  // 1000 categories/minute - excellent
            PeakMemoryUsageMB = 20.0,     // Low memory usage
            FailureCount = 5              // <1% failure rate
        };
        
        var badPerformance = new LevelProcessingResult
        {
            TotalCategories = 1000,
            ProcessingTimeMinutes = 10.0, // 100 categories/minute - slow
            PeakMemoryUsageMB = 80.0,     // High memory usage
            FailureCount = 100            // 10% failure rate
        };

        // Act & Assert
        goodPerformance.IsWithinPerformanceTargets.Should().BeTrue("Good performance should meet targets");
        goodPerformance.ThroughputCategoriesPerMinute.Should().BeGreaterThan(500, "Should have good throughput");
        
        badPerformance.IsWithinPerformanceTargets.Should().BeFalse("Bad performance should not meet targets");
        badPerformance.ThroughputCategoriesPerMinute.Should().BeLessThan(200, "Should have poor throughput");
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public void LevelProcessingResult_ShouldHandle_EdgeCases()
    {
        // Test zero categories
        var emptyResult = new LevelProcessingResult
        {
            TotalCategories = 0,
            ProcessingTimeMinutes = 0
        };
        
        emptyResult.SuccessRate.Should().Be(1.0, "Empty result should have 100% success rate");
        emptyResult.ThroughputCategoriesPerMinute.Should().Be(0, "Zero categories should have zero throughput");
        emptyResult.IsCompleted.Should().BeTrue("Empty processing should be considered complete");

        // Test very small processing time
        var fastResult = new LevelProcessingResult
        {
            TotalCategories = 10,
            ProcessingTimeMinutes = 0.01 // 6 seconds
        };
        
        fastResult.ThroughputCategoriesPerMinute.Should().Be(1000, "Fast processing should have high throughput");
    }

    [Fact]
    public void ProcessingError_ShouldHandle_NullValues()
    {
        // Act - Create error with minimal information
        var minimalError = new ProcessingError
        {
            CategoryId = "123",
            ErrorMessage = "Basic error",
            ErrorType = ProcessingErrorType.ApiError
        };

        // Assert - Should handle null optional fields gracefully
        minimalError.CategoryId.Should().Be("123");
        minimalError.ErrorMessage.Should().Be("Basic error");
        minimalError.Exception.Should().BeNull("Exception can be null");
        minimalError.ApiResponse.Should().BeNull("API response can be null");
        minimalError.CategoryData.Should().BeNull("Category data can be null");
    }

    #endregion
}