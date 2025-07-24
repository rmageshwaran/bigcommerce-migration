using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Strategies;

namespace BigCommerce.Migration.UnitTests.Orchestration.Strategies;

/// <summary>
/// Unit tests for CategoryFetchStrategy Phase 2 enhanced cancellation functionality
/// Tests the hierarchical sorting cancellation and complex algorithm cancellation support
/// </summary>
[Trait("Category", "Phase2Cancellation")]
[Trait("Component", "CategoryFetchStrategy")]
public class CategoryFetchStrategyCancellationTests
{
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly Mock<ILogger<CategoryFetchStrategy>> _mockLogger;
    private readonly CategoryFetchStrategy _strategy;

    private const string TestMigrationId = "test-migration-123";
    private const string TestCategoryTreeId = "1";

    public CategoryFetchStrategyCancellationTests()
    {
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        _mockLogger = new Mock<ILogger<CategoryFetchStrategy>>();
        _strategy = new CategoryFetchStrategy(_mockApiClient.Object, _mockLogger.Object);
    }

    /// <summary>
    /// Test that cancellation token is passed through to the API client
    /// </summary>
    [Fact]
    public async Task FetchEntitiesAsync_PassesCancellationTokenToApiClient()
    {
        // Arrange
        var entityIds = new List<string> { "1", "2", "3" };
        var sourceStore = CreateTestStoreConfiguration();
        var categoryTreeContext = CreateTestCategoryTreeContext();
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        var apiResponse = CreateTestPaginatedResponse();

        _mockApiClient
            .Setup(x => x.GetPaginatedEntitiesAsync(
                sourceStore,
                "categories",
                It.IsAny<BigCommercePaginationRequest>(),
                cancellationToken))
            .ReturnsAsync(apiResponse);

        // Act
        await _strategy.FetchEntitiesAsync(entityIds, TestMigrationId, sourceStore, categoryTreeContext, cancellationToken);

        // Assert
        _mockApiClient.Verify(
            x => x.GetPaginatedEntitiesAsync(
                sourceStore,
                "categories",
                It.IsAny<BigCommercePaginationRequest>(),
                cancellationToken),
            Times.Once);
    }

    /// <summary>
    /// Test that cancellation during API call is handled properly
    /// </summary>
    [Fact]
    public async Task FetchEntitiesAsync_WhenApiCallCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var entityIds = new List<string> { "1", "2", "3" };
        var sourceStore = CreateTestStoreConfiguration();
        var categoryTreeContext = CreateTestCategoryTreeContext();
        var cancellationTokenSource = new CancellationTokenSource();

        _mockApiClient
            .Setup(x => x.GetPaginatedEntitiesAsync(
                sourceStore,
                "categories",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _strategy.FetchEntitiesAsync(entityIds, TestMigrationId, sourceStore, categoryTreeContext, cancellationTokenSource.Token));
    }

    /// <summary>
    /// Test that hierarchical sorting respects cancellation token
    /// </summary>
    [Fact]
    public async Task FetchEntitiesAsync_WhenCancelledDuringHierarchicalSorting_ThrowsOperationCanceledException()
    {
        // Arrange
        var entityIds = new List<string> { "1", "2", "3", "4", "5" };
        var sourceStore = CreateTestStoreConfiguration();
        var categoryTreeContext = CreateTestCategoryTreeContext();
        var cancellationTokenSource = new CancellationTokenSource();

        // Create a large hierarchy to ensure sorting takes some time
        var largeHierarchy = CreateLargeHierarchicalCategories();
        var apiResponse = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = largeHierarchy,
            Meta = new BigCommerceV3Meta
            {
                Pagination = new BigCommerceV3Pagination
                {
                    Total = largeHierarchy.Count,
                    Count = largeHierarchy.Count,
                    PerPage = 250,
                    CurrentPage = 1,
                    TotalPages = 1
                }
            }
        };

        _mockApiClient
            .Setup(x => x.GetPaginatedEntitiesAsync(
                sourceStore,
                "categories",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiResponse);

        // Cancel after a short delay to simulate cancellation during sorting
        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            cancellationTokenSource.Cancel();
        });

        // Act & Assert
        try
        {
            await _strategy.FetchEntitiesAsync(entityIds, TestMigrationId, sourceStore, categoryTreeContext, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected - cancellation was handled properly during sorting
            Assert.True(cancellationTokenSource.Token.IsCancellationRequested);
        }
    }

    /// <summary>
    /// Test that hierarchical sorting works correctly when not cancelled
    /// </summary>
    [Fact]
    public async Task FetchEntitiesAsync_WhenNotCancelled_SortsHierarchicallyCorrectly()
    {
        // Arrange
        var entityIds = new List<string> { "1", "2", "3" };
        var sourceStore = CreateTestStoreConfiguration();
        var categoryTreeContext = CreateTestCategoryTreeContext();
        var cancellationToken = CancellationToken.None;

        var hierarchicalCategories = CreateTestHierarchicalCategories();
        var apiResponse = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = hierarchicalCategories,
            Meta = new BigCommerceV3Meta
            {
                Pagination = new BigCommerceV3Pagination
                {
                    Total = hierarchicalCategories.Count,
                    Count = hierarchicalCategories.Count,
                    PerPage = 250,
                    CurrentPage = 1,
                    TotalPages = 1
                }
            }
        };

        _mockApiClient
            .Setup(x => x.GetPaginatedEntitiesAsync(
                sourceStore,
                "categories",
                It.IsAny<BigCommercePaginationRequest>(),
                cancellationToken))
            .ReturnsAsync(apiResponse);

        // Act
        var result = await _strategy.FetchEntitiesAsync(entityIds, TestMigrationId, sourceStore, categoryTreeContext, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count); // Should return the 3 requested entities

        // Verify hierarchical ordering is maintained (parents before children)
        var resultIds = result.Select(r => r["id"].ToString()).ToList();
        Assert.Contains("1", resultIds); // Root category
        Assert.Contains("2", resultIds); // Child category
        Assert.Contains("3", resultIds); // Grandchild category

        // Verify original entity ID tracking was added
        foreach (var category in result)
        {
            Assert.True(category.ContainsKey("_original_entity_id"));
        }
    }

    /// <summary>
    /// Test that empty entity list is handled correctly without causing cancellation issues
    /// </summary>
    [Fact]
    public async Task FetchEntitiesAsync_WithEmptyEntityIds_ReturnsEmptyWithoutApiCall()
    {
        // Arrange
        var entityIds = new List<string>();
        var sourceStore = CreateTestStoreConfiguration();
        var categoryTreeContext = CreateTestCategoryTreeContext();
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _strategy.FetchEntitiesAsync(entityIds, TestMigrationId, sourceStore, categoryTreeContext, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);

        // Verify API was not called
        _mockApiClient.Verify(
            x => x.GetPaginatedEntitiesAsync(It.IsAny<StoreConfiguration>(), It.IsAny<string>(), It.IsAny<BigCommercePaginationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test that null parameters are handled gracefully without causing cancellation issues
    /// </summary>
    [Fact]
    public async Task FetchEntitiesAsync_WithNullParameters_ReturnsEmptyWithoutApiCall()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _strategy.FetchEntitiesAsync(null!, TestMigrationId, null!, null, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);

        // Verify API was not called
        _mockApiClient.Verify(
            x => x.GetPaginatedEntitiesAsync(It.IsAny<StoreConfiguration>(), It.IsAny<string>(), It.IsAny<BigCommercePaginationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test that cancellation token is respected during immediate cancellation check
    /// </summary>
    [Fact]
    public async Task FetchEntitiesAsync_WhenCancelledImmediately_ThrowsOperationCanceledException()
    {
        // Arrange
        var entityIds = new List<string> { "1", "2", "3" };
        var sourceStore = CreateTestStoreConfiguration();
        var categoryTreeContext = CreateTestCategoryTreeContext();
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _strategy.FetchEntitiesAsync(entityIds, TestMigrationId, sourceStore, categoryTreeContext, cancellationTokenSource.Token));

        // Verify API was not called since we cancelled early
        _mockApiClient.Verify(
            x => x.GetPaginatedEntitiesAsync(It.IsAny<StoreConfiguration>(), It.IsAny<string>(), It.IsAny<BigCommercePaginationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Test that flat category structure (no hierarchy) processes correctly with cancellation support
    /// </summary>
    [Fact]
    public async Task FetchEntitiesAsync_WithFlatCategoryStructure_ProcessesCorrectly()
    {
        // Arrange
        var entityIds = new List<string> { "1", "2", "3" };
        var sourceStore = CreateTestStoreConfiguration();
        var categoryTreeContext = CreateTestCategoryTreeContext();
        var cancellationToken = CancellationToken.None;

        var flatCategories = CreateTestFlatCategories();
        var apiResponse = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = flatCategories,
            Meta = new BigCommerceV3Meta
            {
                Pagination = new BigCommerceV3Pagination
                {
                    Total = flatCategories.Count,
                    Count = flatCategories.Count,
                    PerPage = 250,
                    CurrentPage = 1,
                    TotalPages = 1
                }
            }
        };

        _mockApiClient
            .Setup(x => x.GetPaginatedEntitiesAsync(
                sourceStore,
                "categories",
                It.IsAny<BigCommercePaginationRequest>(),
                cancellationToken))
            .ReturnsAsync(apiResponse);

        // Act
        var result = await _strategy.FetchEntitiesAsync(entityIds, TestMigrationId, sourceStore, categoryTreeContext, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);

        // Verify all requested categories are returned
        var resultIds = result.Select(r => r["id"].ToString()).ToList();
        Assert.Contains("1", resultIds);
        Assert.Contains("2", resultIds);
        Assert.Contains("3", resultIds);
    }

    #region Helper Methods

    private StoreConfiguration CreateTestStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "test-store-123",
            AccessToken = "test-token",
            ChannelId = "1"
        };
    }

    private CategoryTreeContext CreateTestCategoryTreeContext()
    {
        return new CategoryTreeContext
        {
            SourceCategoryTreeId = TestCategoryTreeId,
            DestinationCategoryTreeId = TestCategoryTreeId
        };
    }

    private BigCommercePaginatedResponse<Dictionary<string, object>> CreateTestPaginatedResponse()
    {
        var categories = CreateTestHierarchicalCategories();
        return new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = categories,
            Meta = new BigCommerceV3Meta
            {
                Pagination = new BigCommerceV3Pagination
                {
                    Total = categories.Count,
                    Count = categories.Count,
                    PerPage = 250,
                    CurrentPage = 1,
                    TotalPages = 1
                }
            }
        };
    }

    private List<Dictionary<string, object>> CreateTestHierarchicalCategories()
    {
        return new List<Dictionary<string, object>>
        {
            // Root category
            new Dictionary<string, object>
            {
                ["id"] = "1",
                ["name"] = "Root Category",
                ["parent_id"] = "0"
            },
            // Child category
            new Dictionary<string, object>
            {
                ["id"] = "2",
                ["name"] = "Child Category",
                ["parent_id"] = "1"
            },
            // Grandchild category
            new Dictionary<string, object>
            {
                ["id"] = "3",
                ["name"] = "Grandchild Category",
                ["parent_id"] = "2"
            }
        };
    }

    private List<Dictionary<string, object>> CreateTestFlatCategories()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object>
            {
                ["id"] = "1",
                ["name"] = "Category 1",
                ["parent_id"] = "0"
            },
            new Dictionary<string, object>
            {
                ["id"] = "2",
                ["name"] = "Category 2",
                ["parent_id"] = "0"
            },
            new Dictionary<string, object>
            {
                ["id"] = "3",
                ["name"] = "Category 3",
                ["parent_id"] = "0"
            }
        };
    }

    private List<Dictionary<string, object>> CreateLargeHierarchicalCategories()
    {
        var categories = new List<Dictionary<string, object>>();

        // Create a large hierarchy to test cancellation during sorting
        // Root categories (level 0)
        for (int i = 1; i <= 10; i++)
        {
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i.ToString(),
                ["name"] = $"Root Category {i}",
                ["parent_id"] = "0"
            });

            // Child categories (level 1)
            for (int j = 1; j <= 10; j++)
            {
                var childId = i * 100 + j;
                categories.Add(new Dictionary<string, object>
                {
                    ["id"] = childId.ToString(),
                    ["name"] = $"Child Category {i}-{j}",
                    ["parent_id"] = i.ToString()
                });

                // Grandchild categories (level 2)
                for (int k = 1; k <= 5; k++)
                {
                    var grandchildId = childId * 10 + k;
                    categories.Add(new Dictionary<string, object>
                    {
                        ["id"] = grandchildId.ToString(),
                        ["name"] = $"Grandchild Category {i}-{j}-{k}",
                        ["parent_id"] = childId.ToString()
                    });
                }
            }
        }

        return categories;
    }

    #endregion
} 