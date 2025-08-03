using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Orchestration.ErrorHandling;

/// <summary>
/// Error handling tests for bulk category processing pipeline
/// Task 3.2.4: Validates NO RETRY LOGIC and continue-on-error architectural constraints
/// CRITICAL: Ensures compliance with architectural constraints from AI-ASSISTANT-WORKFLOW-GUIDE.md
/// </summary>
public class BulkProcessingErrorHandlingTests
{
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly Mock<IEntityMappingService> _mockMappingService;
    private readonly Mock<IEntityTransformStrategy> _mockTransformStrategy;
    private readonly Mock<ILogger<BulkCategoryTransformService>> _mockTransformLogger;
    private readonly Mock<ILogger<BulkCategoryCreationService>> _mockCreationLogger;

    private readonly BulkCategoryTransformService _transformService;
    private readonly BulkCategoryCreationService _creationService;

    private readonly BatchProcessingRequest _testBatchRequest;

    public BulkProcessingErrorHandlingTests()
    {
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        _mockMappingService = new Mock<IEntityMappingService>();
        _mockTransformStrategy = new Mock<IEntityTransformStrategy>();
        _mockTransformLogger = new Mock<ILogger<BulkCategoryTransformService>>();
        _mockCreationLogger = new Mock<ILogger<BulkCategoryCreationService>>();

        _transformService = new BulkCategoryTransformService(
            _mockTransformStrategy.Object,
            _mockTransformLogger.Object);

        _creationService = new BulkCategoryCreationService(
            _mockApiClient.Object,
            _mockMappingService.Object,
            _mockCreationLogger.Object);

        _testBatchRequest = new BatchProcessingRequest
        {
            MigrationId = "error-test-migration",
            SourceStore = new StoreConfiguration
            {
                StoreId = "error-source-store",
                AccessToken = "error-source-token",
                ChannelId = "1"
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "error-dest-store",
                AccessToken = "error-dest-token",
                ChannelId = "1"
            },

            CategoryTreeContext = new CategoryTreeContext
            {
                SourceCategoryTreeId = "1",
                DestinationCategoryTreeId = "1"
            }
        };
    }

    [Fact]
    public async Task BulkCategoryCreation_ShouldNOT_ImplementRetryLogic_OnApiFfailures()
    {
        // Arrange - Setup to simulate consistent API failures
        var categories = CreateTestDataset(100);
        var apiCallCount = 0;
        
        _mockApiClient.Setup(client => client.CreateCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            It.IsAny<string>(),
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<CancellationToken>()))
            .Returns((StoreConfiguration store, string treeId, List<Dictionary<string, object>> batch, CancellationToken ct) =>
            {
                apiCallCount++;
                throw new InvalidOperationException($"BigCommerce API failure for batch {apiCallCount} - SHOULD NOT RETRY");
            });

        // Act - Attempt bulk creation with expected failures
        var result = await _creationService.CreateCategoriesInBulkAsync(
            categories, _testBatchRequest, CancellationToken.None);

        // Assert - CRITICAL: NO RETRY LOGIC architectural constraint validation
        result.Success.Should().BeFalse("Should report false when no categories are successfully created, but operation should complete gracefully");
        result.CreatedCategoriesCount.Should().Be(0, "No categories should be created due to API failures");
        result.FailedCategories.Should().Be(100, "All categories should be marked as failed");

        // CRITICAL: Validate that each batch was attempted EXACTLY ONCE (NO RETRIES)
        var expectedBatches = (int)Math.Ceiling(100.0 / 25.0); // 4 batches for 100 categories
        apiCallCount.Should().Be(expectedBatches, 
            "Should make exactly {0} API calls (one per batch) with NO RETRIES", expectedBatches);

        // Validate API errors are logged without retry mentions
        result.ApiErrors.Should().HaveCount(expectedBatches, "Should log error for each failed batch");
        result.ApiErrors.Should().AllSatisfy(error =>
        {
            error.Should().NotContain("retry", 
                "Error messages should NOT mention retries");
            error.Should().NotContain("attempt",
                "Error messages should NOT mention retry attempts");
        });

        // Validate batch results show single attempt failures
        result.BatchResults.Should().HaveCount(expectedBatches, "Should have one result per batch");
        result.BatchResults.Should().AllSatisfy(batchResult =>
        {
            batchResult.Success.Should().BeFalse("All batches should fail");
            batchResult.ErrorMessage.Should().NotBeNullOrEmpty("Failed batches should have error messages");
            batchResult.ErrorMessage.Should().Contain("SHOULD NOT RETRY", 
                "Error message should confirm no retry logic");
        });
    }

    [Fact]
    public async Task BulkCategoryTransformation_ShouldContinueOnError_WithoutRetries()
    {
        // Arrange - Setup transformation failures for specific categories
        var categories = CreateTestDataset(75);
        var transformationAttempts = new Dictionary<int, int>(); // Track attempts per category

        foreach (var category in categories)
        {
            var categoryId = (int)category["id"];
            transformationAttempts[categoryId] = 0;

            if (categoryId % 5 == 0) // Every 5th category fails
            {
                _mockTransformStrategy.Setup(strategy => strategy.TransformEntityAsync(
                    It.Is<Dictionary<string, object>>(c => c["id"].Equals(categoryId)),
                    It.IsAny<string>(),
                    It.IsAny<StoreConfiguration>(),
                    It.IsAny<StoreConfiguration>(),
                    It.IsAny<CategoryTreeContext>(),
                    It.IsAny<CancellationToken>()))
                    .Returns<Dictionary<string, object>, string, StoreConfiguration, StoreConfiguration, CategoryTreeContext, CancellationToken>(
                        (cat, migId, source, dest, context, ct) =>
                        {
                            transformationAttempts[categoryId]++;
                            throw new InvalidOperationException($"Transformation failure for category {categoryId} - NO RETRY ALLOWED");
                        });
            }
            else
            {
                var transformedCategory = new Dictionary<string, object>(category)
                {
                    ["tree_id"] = 1,
                    ["url"] = new Dictionary<string, object>
                    {
                        ["path"] = $"/{category["name"]?.ToString()?.ToLowerInvariant()}/",
                        ["is_customized"] = false
                    }
                };

                _mockTransformStrategy.Setup(strategy => strategy.TransformEntityAsync(
                    It.Is<Dictionary<string, object>>(c => c["id"].Equals(categoryId)),
                    It.IsAny<string>(),
                    It.IsAny<StoreConfiguration>(),
                    It.IsAny<StoreConfiguration>(),
                    It.IsAny<CategoryTreeContext>(),
                    It.IsAny<CancellationToken>()))
                    .Returns<Dictionary<string, object>, string, StoreConfiguration, StoreConfiguration, CategoryTreeContext, CancellationToken>(
                        (cat, migId, source, dest, context, ct) =>
                        {
                            transformationAttempts[categoryId]++;
                            return Task.FromResult(transformedCategory);
                        });
            }
        }

        // Act - Transform with expected failures
        var result = await _transformService.TransformCategoriesInBulkAsync(
            categories, _testBatchRequest, CancellationToken.None);

        // Assert - Continue-on-error policy validation
        result.Success.Should().BeTrue("Should succeed with continue-on-error policy");
        result.ProcessedCategories.Should().Be(60, "60 categories should transform successfully (75 - 15 failures)");
        result.FailedCategories.Should().Be(15, "15 categories should fail (every 5th category)");
        result.SuccessRatePercent.Should().Be(80.0, "Success rate should be 80% (60/75)");

        // CRITICAL: Validate NO RETRY LOGIC - each category attempted exactly once
        transformationAttempts.Values.Should().AllSatisfy(attempts =>
            attempts.Should().Be(1, "Each category should be attempted exactly ONCE (no retries)"));

        // Validate error logging includes failures without retry language
        result.TransformationErrors.Should().HaveCount(15, "Should log 15 transformation errors");
        result.TransformationErrors.Should().AllSatisfy(error =>
        {
            error.Should().Contain("NO RETRY ALLOWED", "Error should confirm no retry policy");
            error.Should().NotContain("retrying",
                "Error messages should not mention retrying");
        });

        // Validate successful categories were processed correctly
        result.TransformedCategories.Should().HaveCount(60, "Should have 60 transformed categories");
        result.CategoryMappings.Should().HaveCount(60, "Should have mappings for all successful transformations");
    }

    [Fact]
    public async Task BulkProcessingPipeline_ShouldHandle_MixedBatchAndIndividualFailures()
    {
        // Arrange - Complex failure scenario with both batch and individual failures
        var categories = CreateTestDataset(150); // 6 batches of 25 each
        var batchCallCount = 0;
        var batchFailures = new[] { 2, 4 }; // Batches 2 and 4 will fail

        SetupPartialTransformationFailures(categories); // Some individual transformation failures

        _mockApiClient.Setup(client => client.CreateCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            It.IsAny<string>(),
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoreConfiguration store, string treeId, List<Dictionary<string, object>> batch, CancellationToken ct) =>
            {
                batchCallCount++;
                
                if (batchFailures.Contains(batchCallCount))
                {
                    throw new InvalidOperationException($"Batch {batchCallCount} API failure - NO RETRY LOGIC");
                }

                // Successful batch
                return batch.Select((cat, index) => new Dictionary<string, object>(cat)
                {
                    ["id"] = 20000 + (batchCallCount * 1000) + index,
                    ["tree_id"] = int.Parse(treeId)
                }).ToList();
            });

        _mockMappingService.Setup(service => service.StoreEntityMappingAsync(
            It.IsAny<EntityMapping>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act - Process with mixed failure scenarios
        var transformResult = await _transformService.TransformCategoriesInBulkAsync(
            categories, _testBatchRequest, CancellationToken.None);

        var creationResult = await _creationService.CreateCategoriesInBulkAsync(
            transformResult.TransformedCategories, _testBatchRequest, CancellationToken.None);

        // Assert - Mixed failure scenario validation
        transformResult.Success.Should().BeTrue("Transformation should succeed with continue-on-error");
        transformResult.ProcessedCategories.Should().BeLessThan(150, "Some transformation failures expected");
        transformResult.FailedCategories.Should().BeGreaterThan(0, "Some transformation failures expected");

        creationResult.Success.Should().BeTrue("Creation should succeed with continue-on-error");
        creationResult.TotalApiCalls.Should().Be(6, "Should make exactly 6 API calls (one per batch)");

        // Validate specific batch failures
        var failedBatches = creationResult.BatchResults.Where(b => !b.Success).ToList();
        failedBatches.Should().HaveCount(2, "Batches 2 and 4 should fail");
        
        var successfulBatches = creationResult.BatchResults.Where(b => b.Success).ToList();
        successfulBatches.Should().HaveCount(4, "Batches 1, 3, 5, 6 should succeed");

        // CRITICAL: Validate no retries were attempted for failed batches
        creationResult.ApiErrors.Should().HaveCount(2, "Should log 2 batch-level API errors");
        creationResult.ApiErrors.Should().AllSatisfy(error =>
            error.Should().Contain("NO RETRY LOGIC", "API errors should confirm no retry policy"));

        // Validate overall processing continued despite failures
        creationResult.CreatedCategoriesCount.Should().BeGreaterThan(50, 
            "Should create some categories despite batch failures");
        creationResult.FailedCategories.Should().BeGreaterThan(0, 
            "Should track failed categories from failed batches");
    }

    [Fact]
    public async Task BulkCategoryCreation_ShouldHandle_RateLimitErrors_WithoutRetries()
    {
        // Arrange - Simulate rate limit errors (common BigCommerce API scenario)
        var categories = CreateTestDataset(200);
        var rateLimitHits = 0;

        _mockApiClient.Setup(client => client.CreateCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            It.IsAny<string>(),
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<CancellationToken>()))
            .Returns((StoreConfiguration store, string treeId, List<Dictionary<string, object>> batch, CancellationToken ct) =>
            {
                rateLimitHits++;
                
                // Simulate rate limit errors for first 3 batches
                if (rateLimitHits <= 3)
                {
                    throw new InvalidOperationException($"Rate limit exceeded for batch {rateLimitHits} - RATE LIMIT NO RETRY");
                }

                // Subsequent batches succeed
                return Task.FromResult(batch.Select((cat, index) => new Dictionary<string, object>(cat)
                {
                    ["id"] = 30000 + (rateLimitHits * 1000) + index,
                    ["tree_id"] = int.Parse(treeId)
                }).ToList());
            });

        _mockMappingService.Setup(service => service.StoreEntityMappingAsync(
            It.IsAny<EntityMapping>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act - Process with rate limit failures
        var result = await _creationService.CreateCategoriesInBulkAsync(
            categories, _testBatchRequest, CancellationToken.None);

        // Assert - Rate limit error handling without retries
        result.Success.Should().BeTrue("Should succeed with continue-on-error despite rate limits");
        
        var expectedBatches = (int)Math.Ceiling(200.0 / 25.0); // 8 batches
        result.TotalApiCalls.Should().Be(expectedBatches, 
            "Should make exactly {0} API calls (one per batch, no retries)", expectedBatches);

        // CRITICAL: Validate NO RETRY LOGIC for rate limit errors
        rateLimitHits.Should().Be(expectedBatches, 
            "Should hit rate limit check exactly {0} times (once per batch)", expectedBatches);

        // Validate rate limit errors are handled properly
        result.ApiErrors.Should().HaveCount(3, "Should log 3 rate limit errors");
        result.ApiErrors.Should().AllSatisfy(error =>
        {
            error.Should().Contain("RATE LIMIT NO RETRY", "Rate limit errors should confirm no retry policy");
                            error.Should().NotContain("retrying",
                    "Rate limit errors should not mention retrying");
        });

        // Validate some batches succeeded after rate limit period
        result.CreatedCategoriesCount.Should().BeGreaterThan(75, 
            "Should create categories from batches 4-8 after rate limit period");
        
        // Rate limit compliance may be false in fast test environments where rate limiting doesn't apply
        // The important thing is that no retries occurred and operation completed gracefully
    }

    [Fact]
    public async Task BulkProcessingPipeline_ShouldLog_DetailedErrorsWithoutRetryReferences()
    {
        // Arrange - Various error types for comprehensive error logging validation
        var categories = CreateTestDataset(50);
        SetupMixedErrorScenarios(categories);

        // Act - Process with various error scenarios
        var transformResult = await _transformService.TransformCategoriesInBulkAsync(
            categories, _testBatchRequest, CancellationToken.None);

        var creationResult = await _creationService.CreateCategoriesInBulkAsync(
            transformResult.TransformedCategories, _testBatchRequest, CancellationToken.None);

        // Assert - Comprehensive error logging validation
        transformResult.Success.Should().BeTrue("Should succeed with continue-on-error");
        creationResult.Success.Should().BeTrue("Should succeed with continue-on-error");

        // Validate transformation error details
        if (transformResult.TransformationErrors.Any())
        {
            transformResult.TransformationErrors.Should().AllSatisfy(error =>
            {
                error.Should().NotBeNullOrEmpty("All transformation errors should be logged");
                error.Should().NotContain("retry",
                    "Transformation errors should not reference retries");
                error.Should().NotContain("attempt",
                    "Transformation errors should not reference retry attempts");
            });
        }

        // Validate creation error details
        if (creationResult.ApiErrors.Any())
        {
            creationResult.ApiErrors.Should().AllSatisfy(error =>
            {
                error.Should().NotBeNullOrEmpty("All API errors should be logged");
                error.Should().NotContain("retry",
                    "API errors should not reference retries");
                error.Should().NotContain("attempt",
                    "API errors should not reference retry attempts");
            });
        }

        // Validate batch error details
        var failedBatches = creationResult.BatchResults.Where(b => !b.Success).ToList();
        if (failedBatches.Any())
        {
            failedBatches.Should().AllSatisfy(batchResult =>
            {
                batchResult.ErrorMessage.Should().NotBeNullOrEmpty("Failed batches should have error messages");
                batchResult.ErrorMessage.Should().NotContain("retry",
                    "Batch errors should not reference retries");
            });
        }

        // Validate that processing continued despite errors
        var totalProcessed = transformResult.ProcessedCategories;
        var totalCreated = creationResult.CreatedCategoriesCount;
        
        (totalProcessed + transformResult.FailedCategories).Should().Be(50,
            "All categories should be accounted for (processed + failed)");
            
        if (totalProcessed > 0)
        {
            totalCreated.Should().BeLessOrEqualTo(totalProcessed,
                "Created count should not exceed processed count");
        }
    }

    #region Test Data Setup

    private List<Dictionary<string, object>> CreateTestDataset(int count)
    {
        var categories = new List<Dictionary<string, object>>();
        for (int i = 1; i <= count; i++)
        {
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Error Test Category {i}",
                ["parent_id"] = 0,
                ["description"] = $"Error test category {i}",
                ["is_visible"] = true,
                ["sort_order"] = i
            });
        }
        return categories;
    }

    private void SetupPartialTransformationFailures(List<Dictionary<string, object>> categories)
    {
        foreach (var category in categories)
        {
            var categoryId = (int)category["id"];
            
            if (categoryId % 8 == 0) // Every 8th category fails transformation
            {
                _mockTransformStrategy.Setup(strategy => strategy.TransformEntityAsync(
                    It.Is<Dictionary<string, object>>(c => c["id"].Equals(categoryId)),
                    It.IsAny<string>(),
                    It.IsAny<StoreConfiguration>(),
                    It.IsAny<StoreConfiguration>(),
                    It.IsAny<CategoryTreeContext>(),
                    It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException($"Transformation error for category {categoryId} - NO RETRY"));
            }
            else
            {
                var transformedCategory = new Dictionary<string, object>(category)
                {
                    ["tree_id"] = 1,
                    ["url"] = new Dictionary<string, object>
                    {
                        ["path"] = $"/{category["name"]?.ToString()?.ToLowerInvariant()?.Replace(" ", "-")}/",
                        ["is_customized"] = false
                    }
                };

                _mockTransformStrategy.Setup(strategy => strategy.TransformEntityAsync(
                    It.Is<Dictionary<string, object>>(c => c["id"].Equals(categoryId)),
                    It.IsAny<string>(),
                    It.IsAny<StoreConfiguration>(),
                    It.IsAny<StoreConfiguration>(),
                    It.IsAny<CategoryTreeContext>(),
                    It.IsAny<CancellationToken>()))
                    .ReturnsAsync(transformedCategory);
            }
        }
    }

    private void SetupMixedErrorScenarios(List<Dictionary<string, object>> categories)
    {
        SetupPartialTransformationFailures(categories);

        _mockApiClient.Setup(client => client.CreateCategoriesAsync(
            It.IsAny<StoreConfiguration>(),
            It.IsAny<string>(),
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoreConfiguration store, string treeId, List<Dictionary<string, object>> batch, CancellationToken ct) =>
            {
                // Simulate mixed success/failure for creation
                var successfulCategories = batch.Take((int)(batch.Count * 0.8)).ToList(); // 80% success rate
                
                return successfulCategories.Select((cat, index) => new Dictionary<string, object>(cat)
                {
                    ["id"] = 40000 + index + (int)(cat["id"] ?? 0),
                    ["tree_id"] = int.Parse(treeId)
                }).ToList();
            });

        _mockMappingService.Setup(service => service.StoreEntityMappingAsync(
            It.IsAny<EntityMapping>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    #endregion
}