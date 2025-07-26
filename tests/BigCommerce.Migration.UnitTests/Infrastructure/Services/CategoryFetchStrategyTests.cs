using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Strategies;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for CategoryFetchStrategy pagination fixes
/// Tests the pagination bug fixes that ensure all categories are discovered
/// </summary>
public class CategoryFetchStrategyTests
{
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly Mock<ILogger<CategoryFetchStrategy>> _mockLogger;
    private readonly CategoryFetchStrategy _strategy;

    public CategoryFetchStrategyTests()
    {
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        _mockLogger = new Mock<ILogger<CategoryFetchStrategy>>();
        _strategy = new CategoryFetchStrategy(_mockApiClient.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task FetchEntitiesAsync_WithMultiplePages_ShouldFetchAllCategories()
    {
        // Arrange: 288 categories across 2 pages (250 + 38)
        var entityIds = Enumerable.Range(1, 288).Select(i => i.ToString()).ToList();
        var migrationId = "test-migration";
        var sourceStore = new StoreConfiguration { StoreId = "source-store" };
        var categoryTreeContext = new CategoryTreeContext { SourceCategoryTreeId = "1" };

        // Mock first page (250 categories)
        var page1Categories = CreateMockCategories(1, 250);
        var page1Response = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = page1Categories,
            TotalPages = 2,
            PerPage = 250,
            Meta = new BigCommerceV3Meta
            {
                Pagination = new BigCommerceV3Pagination { Total = 288 }
            }
        };

        // Mock second page (38 categories)
        var page2Categories = CreateMockCategories(251, 288);
        var page2Response = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = page2Categories,
            TotalPages = 2,
            PerPage = 38
        };

        // Setup API client mock for pagination
        _mockApiClient.SetupSequence(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                "categories",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(page1Response)
            .ReturnsAsync(page2Response);

        // Act
        var result = await _strategy.FetchEntitiesAsync(entityIds, migrationId, sourceStore, categoryTreeContext);

        // Assert
        Assert.Equal(288, result.Count);
        
        // Verify all requested categories are returned
        var returnedIds = result.Select(c => c.GetValueOrDefault("id")?.ToString()).ToList();
        Assert.Equal(entityIds.Count, returnedIds.Count);
        
        // Verify pagination was called for both pages
        _mockApiClient.Verify(x => x.GetPaginatedEntitiesAsync(
            It.IsAny<StoreConfiguration>(),
            "categories",
            It.Is<BigCommercePaginationRequest>(req => req.Page == 1),
            It.IsAny<CancellationToken>()), Times.Once);
            
        _mockApiClient.Verify(x => x.GetPaginatedEntitiesAsync(
            It.IsAny<StoreConfiguration>(),
            "categories",
            It.Is<BigCommercePaginationRequest>(req => req.Page == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FetchEntitiesAsync_WithExactly250Categories_ShouldNotTriggerSecondPage()
    {
        // Arrange: Exactly 250 categories (edge case)
        var entityIds = Enumerable.Range(1, 250).Select(i => i.ToString()).ToList();
        var migrationId = "test-migration";
        var sourceStore = new StoreConfiguration { StoreId = "source-store" };
        var categoryTreeContext = new CategoryTreeContext { SourceCategoryTreeId = "1" };

        var categories = CreateMockCategories(1, 250);
        var response = new BigCommercePaginatedResponse<Dictionary<string, object>>
        {
            Data = categories,
            TotalPages = 1,
            PerPage = 250
        };

        _mockApiClient.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                "categories",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _strategy.FetchEntitiesAsync(entityIds, migrationId, sourceStore, categoryTreeContext);

        // Assert
        Assert.Equal(250, result.Count);
        
        // Verify only one page was called
        _mockApiClient.Verify(x => x.GetPaginatedEntitiesAsync(
            It.IsAny<StoreConfiguration>(),
            "categories",
            It.Is<BigCommercePaginationRequest>(req => req.Page == 1),
            It.IsAny<CancellationToken>()), Times.Once);
            
        _mockApiClient.Verify(x => x.GetPaginatedEntitiesAsync(
            It.IsAny<StoreConfiguration>(),
            "categories",
            It.Is<BigCommercePaginationRequest>(req => req.Page == 2),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static List<Dictionary<string, object>> CreateMockCategories(int startId, int endId)
    {
        var categories = new List<Dictionary<string, object>>();
        
        for (int i = startId; i <= endId; i++)
        {
            categories.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"Category {i}",
                ["parent_id"] = i <= 10 ? 0 : (i % 10) + 1, // Create some hierarchy
                ["is_visible"] = true,
                ["sort_order"] = i
            });
        }
        
        return categories;
    }
} 