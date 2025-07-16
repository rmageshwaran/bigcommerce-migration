using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Services;

/// <summary>
/// Tests for EntityTransformService to verify category transformation fixes
/// </summary>
public class EntityTransformServiceTests
{
    private readonly Mock<ILogger<EntityTransformService>> _loggerMock;
    private readonly EntityTransformService _service;

    public EntityTransformServiceTests()
    {
        _loggerMock = new Mock<ILogger<EntityTransformService>>();
        _service = new EntityTransformService(_loggerMock.Object);
    }

    [Fact]
    public async Task TransformCategoryAsync_WithMissingRequiredFields_AddsRequiredFields()
    {
        // Arrange
        var sourceCategory = new Dictionary<string, object>
        {
            ["name"] = "Test Category",
            ["description"] = "Test Description"
            // Missing parent_id, tree_id, and url.path
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            CategoryTreeContext = new CategoryTreeContext
            {
                DestinationCategoryTreeId = "2"
            }
        };

        // Act
        var result = await _service.TransformCategoryAsync(sourceCategory, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Category", result["name"]);
        Assert.Equal("Test Description", result["description"]);
        
        // Verify required fields are added
        Assert.True(result.TryGetValue("parent_id", out var parentId) && parentId == null); // Should be null for root category
        Assert.Equal(2, result["tree_id"]); // Should use destination tree ID as int
        
        // Verify url.path is generated
        Assert.NotNull(result["url"]);
        var url = result["url"] as Dictionary<string, object>;
        Assert.NotNull(url);
        Assert.Equal("test-category", url["path"]);
    }

    [Fact]
    public async Task TransformCategoryAsync_WithRootCategory_HandlesParentIdCorrectly()
    {
        // Arrange
        var sourceCategory = new Dictionary<string, object>
        {
            ["name"] = "Root Category",
            ["parent_id"] = 0 // Root category
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            CategoryTreeContext = new CategoryTreeContext
            {
                DestinationCategoryTreeId = "1"
            }
        };

        // Act
        var result = await _service.TransformCategoryAsync(sourceCategory, request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TryGetValue("parent_id", out var parentId) && parentId == null); // Should be null for root category
        Assert.Equal(1, result["tree_id"]); // Should be int
    }

    [Fact]
    public async Task TransformCategoryAsync_WithChildCategory_PreservesParentId()
    {
        // Arrange
        var sourceCategory = new Dictionary<string, object>
        {
            ["name"] = "Child Category",
            ["parent_id"] = 123 // Child category
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            CategoryTreeContext = new CategoryTreeContext
            {
                DestinationCategoryTreeId = "3"
            }
        };

        // Act
        var result = await _service.TransformCategoryAsync(sourceCategory, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(123, result["parent_id"]); // Should preserve parent_id
        Assert.Equal(3, result["tree_id"]); // Should be int
    }

    [Fact]
    public async Task TransformCategoryAsync_WithSpecialCharacters_GeneratesValidUrlPath()
    {
        // Arrange
        var sourceCategory = new Dictionary<string, object>
        {
            ["name"] = "Special Category & More!",
            ["parent_id"] = null
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            CategoryTreeContext = new CategoryTreeContext
            {
                DestinationCategoryTreeId = "1"
            }
        };

        // Act
        var result = await _service.TransformCategoryAsync(sourceCategory, request);

        // Assert
        Assert.NotNull(result);
        var url = result["url"] as Dictionary<string, object>;
        Assert.NotNull(url);
        Assert.Equal("special-category-more", url["path"]); // Should be URL-friendly
    }

    [Fact]
    public async Task TransformCategoryAsync_WithoutCategoryTreeContext_UsesDefaultTreeId()
    {
        // Arrange
        var sourceCategory = new Dictionary<string, object>
        {
            ["name"] = "Test Category"
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories"
            // No CategoryTreeContext
        };

        // Act
        var result = await _service.TransformCategoryAsync(sourceCategory, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result["tree_id"]); // Should use default tree ID as int
    }

    [Fact]
    public async Task TransformCategoryAsync_WithExistingUrl_AddsMissingPath()
    {
        // Arrange
        var sourceCategory = new Dictionary<string, object>
        {
            ["name"] = "Test Category",
            ["url"] = new Dictionary<string, object>
            {
                ["other_field"] = "value"
                // Missing path
            }
        };

        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            CategoryTreeContext = new CategoryTreeContext
            {
                DestinationCategoryTreeId = "1"
            }
        };

        // Act
        var result = await _service.TransformCategoryAsync(sourceCategory, request);

        // Assert
        Assert.NotNull(result);
        var url = result["url"] as Dictionary<string, object>;
        Assert.NotNull(url);
        Assert.Equal("value", url["other_field"]);
        Assert.Equal("test-category", url["path"]); // Should add missing path
    }

    [Fact]
    public async Task TransformCategoryAsync_WithStringTreeId_ConvertsToInt()
    {
        // Arrange
        var category = new Dictionary<string, object>
        {
            ["id"] = 100,
            ["name"] = "Test Category",
            ["tree_id"] = "5", // String tree_id
            ["parent_id"] = "10" // String parent_id
        };
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "channel" },
            DestinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "channel" }
        };

        // Act
        var result = await _service.TransformCategoryAsync(category, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result["tree_id"]); // Should be converted to int
        Assert.Equal(10, result["parent_id"]); // Should be converted to int
    }

    [Fact]
    public async Task TransformCategoryAsync_WithInvalidStringIds_HandlesGracefully()
    {
        // Arrange
        var category = new Dictionary<string, object>
        {
            ["id"] = 100,
            ["name"] = "Test Category",
            ["tree_id"] = "invalid", // Invalid string tree_id
            ["parent_id"] = "invalid" // Invalid string parent_id
        };
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "channel" },
            DestinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "channel" }
        };

        // Act
        var result = await _service.TransformCategoryAsync(category, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result["tree_id"]); // Should default to 1
        Assert.Null(result["parent_id"]); // Should default to null for invalid parent_id
    }

    [Fact]
    public async Task TransformCategoryAsync_WithInvalidUrlStructure_FixesUrlStructure()
    {
        // Arrange
        var category = new Dictionary<string, object>
        {
            ["id"] = 100,
            ["name"] = "Test Category",
            ["url"] = "invalid-url-string" // Invalid URL structure
        };
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "channel" },
            DestinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "channel" }
        };

        // Act
        var result = await _service.TransformCategoryAsync(category, request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ContainsKey("url"));
        var url = result["url"] as Dictionary<string, object>;
        Assert.NotNull(url);
        Assert.True(url.ContainsKey("path"));
        Assert.Equal("test-category", url["path"]);
    }

    [Fact]
    public async Task TransformCategoryAsync_AlwaysHasUrlPath_ValidatesFinalStructure()
    {
        // Arrange
        var category = new Dictionary<string, object>
        {
            ["id"] = 100,
            ["name"] = "Test Category"
            // No URL field at all
        };
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "channel" },
            DestinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "channel" }
        };

        // Act
        var result = await _service.TransformCategoryAsync(category, request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ContainsKey("url"), "URL field should always be present");
        var url = result["url"] as Dictionary<string, object>;
        Assert.NotNull(url);
        Assert.True(url.ContainsKey("path"), "URL path should always be present");
        Assert.Equal("test-category", url["path"]);
        
        // Verify the URL structure is correct for BigCommerce API
        Assert.IsType<Dictionary<string, object>>(result["url"]);
        Assert.IsType<string>(url["path"]);
    }

    [Fact]
    public async Task TransformCategoryAsync_HandlesMetaKeywordsCorrectly()
    {
        // Arrange
        var logger = Mock.Of<ILogger<EntityTransformService>>();
        var service = new EntityTransformService(logger);
        var request = CreateMockBatchProcessingRequest();

        // Test cases for different meta_keywords formats
        var testCases = new[]
        {
            // Malformed string that caused the original issue
            new { Input = "[\"[]\"]", ExpectedCount = 0, Description = "Malformed nested brackets" },
            
            // Empty cases
            new { Input = "", ExpectedCount = 0, Description = "Empty string" },
            new { Input = "[]", ExpectedCount = 0, Description = "Empty array string" },
            new { Input = "[\"\"]", ExpectedCount = 0, Description = "Array with empty string" },
            
            // Valid JSON arrays
            new { Input = "[\"keyword1\", \"keyword2\"]", ExpectedCount = 2, Description = "Valid JSON array" },
            new { Input = "[\"single-keyword\"]", ExpectedCount = 1, Description = "Single keyword in array" },
            
            // Comma-separated values (fallback)
            new { Input = "keyword1,keyword2,keyword3", ExpectedCount = 3, Description = "CSV fallback" },
            new { Input = "\"keyword1\",\"keyword2\"", ExpectedCount = 2, Description = "Quoted CSV" },
        };

        foreach (var testCase in testCases)
        {
            // Arrange
            var category = new Dictionary<string, object>
            {
                ["name"] = "Test Category",
                ["meta_keywords"] = testCase.Input
            };

            // Act
            var result = await service.TransformCategoryAsync(category, request);

            // Assert
            Assert.True(result.ContainsKey("meta_keywords"), $"Result should contain meta_keywords for test case: {testCase.Description}");
            var actualKeywords = result["meta_keywords"] as List<string>;
            Assert.NotNull(actualKeywords);
            Assert.Equal(testCase.ExpectedCount, actualKeywords.Count);
        }
    }

    [Fact]
    public async Task TransformCategoryAsync_HandlesMetaKeywordsAsExistingArray()
    {
        // Arrange
        var logger = Mock.Of<ILogger<EntityTransformService>>();
        var service = new EntityTransformService(logger);
        var request = CreateMockBatchProcessingRequest();

        var category = new Dictionary<string, object>
        {
            ["name"] = "Test Category",
            ["meta_keywords"] = new List<string> { "existing", "keywords" }
        };

        // Act
        var result = await service.TransformCategoryAsync(category, request);

        // Assert
        Assert.True(result.ContainsKey("meta_keywords"));
        var keywords = result["meta_keywords"] as List<string>;
        Assert.NotNull(keywords);
        Assert.Equal(2, keywords.Count);
        Assert.Contains("existing", keywords);
        Assert.Contains("keywords", keywords);
    }

    [Fact]
    public async Task TransformCategoryAsync_HandlesInvalidMetaKeywordsType()
    {
        // Arrange
        var logger = Mock.Of<ILogger<EntityTransformService>>();
        var service = new EntityTransformService(logger);
        var request = CreateMockBatchProcessingRequest();

        var category = new Dictionary<string, object>
        {
            ["name"] = "Test Category",
            ["meta_keywords"] = 12345 // Invalid type
        };

        // Act
        var result = await service.TransformCategoryAsync(category, request);

        // Assert
        Assert.True(result.ContainsKey("meta_keywords"));
        var keywords = result["meta_keywords"] as List<string>;
        Assert.NotNull(keywords);
        Assert.Empty(keywords);
    }

    private static BatchProcessingRequest CreateMockBatchProcessingRequest()
    {
        return new BatchProcessingRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            EntityType = "Categories",
            BatchNumber = 1,
            TotalBatches = 1,
            EntityIds = new List<string> { "1", "2", "3" },
            SourceStore = new StoreConfiguration(),
            DestinationStore = new StoreConfiguration(),
            CategoryTreeContext = new CategoryTreeContext()
        };
    }
} 