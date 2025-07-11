using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.UnitTests.TestHelpers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Unit tests for CategoryTreeResolver service implementation
/// Tests category tree resolution logic with new MigrationRequest-based architecture
/// </summary>
public class CategoryTreeResolverTests
{
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;
    private readonly Mock<ILogger<CategoryTreeResolver>> _mockLogger;
    private readonly Mock<IOpenSearchService> _mockOpenSearchService;
    private readonly CategoryTreeResolver _resolver;
    private readonly MigrationRequest _testMigrationRequest;

    public CategoryTreeResolverTests()
    {
        _mockApiClient = new Mock<IBigCommerceApiClient>();
        _mockLogger = new Mock<ILogger<CategoryTreeResolver>>();
        _mockOpenSearchService = new Mock<IOpenSearchService>();
        _testMigrationRequest = TestDataFactory.CreateMigrationRequest();
        _resolver = new CategoryTreeResolver(_mockApiClient.Object, _mockOpenSearchService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithValidRequest_ShouldReturnCategoryTreeContext()
    {
        // Arrange
        var sourceTrees = TestDataFactory.CreateMockCategoryTrees("1", "source-tree-123");
        var destTrees = TestDataFactory.CreateMockCategoryTrees("2", "dest-tree-456");

        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.SourceStore!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceTrees);
        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.DestinationStore!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(destTrees);

        // Act
        var result = await _resolver.ResolveCategoryTreeAsync(_testMigrationRequest);

        // Assert
        result.Should().NotBeNull();
        result!.SourceChannelId.Should().Be("1");
        result.DestinationChannelId.Should().Be("2");
        result.SourceCategoryTreeId.Should().Be("source-tree-123");
        result.DestinationCategoryTreeId.Should().Be("dest-tree-456");
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithNullSourceStore_ShouldReturnNull()
    {
        // Arrange
        var invalidRequest = new MigrationRequest
        {
            SourceStore = null,
            DestinationStore = TestDataFactory.CreateDestinationStoreConfiguration(),
            Entities = new List<string> { "categories" }
        };

        // Act
        var result = await _resolver.ResolveCategoryTreeAsync(invalidRequest);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithNullDestinationStore_ShouldReturnNull()
    {
        // Arrange
        var invalidRequest = new MigrationRequest
        {
            SourceStore = TestDataFactory.CreateSourceStoreConfiguration(),
            DestinationStore = null,
            Entities = new List<string> { "categories" }
        };

        // Act
        var result = await _resolver.ResolveCategoryTreeAsync(invalidRequest);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithInvalidSourceStore_ShouldReturnNull()
    {
        // Arrange
        var invalidRequest = new MigrationRequest
        {
            SourceStore = new StoreConfiguration { StoreId = "", AccessToken = "token", ChannelId = "1" }, // Invalid
            DestinationStore = TestDataFactory.CreateDestinationStoreConfiguration(),
            Entities = new List<string> { "categories" }
        };

        // Act
        var result = await _resolver.ResolveCategoryTreeAsync(invalidRequest);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithNoSourceTrees_ShouldReturnNull()
    {
        // Arrange
        var emptyTrees = new List<Dictionary<string, object>>();
        var destTrees = TestDataFactory.CreateMockCategoryTrees("2", "dest-tree-456");

        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.SourceStore!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyTrees);
        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.DestinationStore!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(destTrees);

        // Act
        var result = await _resolver.ResolveCategoryTreeAsync(_testMigrationRequest);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithNoDestinationTrees_ShouldReturnNull()
    {
        // Arrange
        var sourceTrees = TestDataFactory.CreateMockCategoryTrees("1", "source-tree-123");
        var emptyTrees = new List<Dictionary<string, object>>();

        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.SourceStore!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceTrees);
        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.DestinationStore!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyTrees);

        // Act
        var result = await _resolver.ResolveCategoryTreeAsync(_testMigrationRequest);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithApiException_ShouldReturnNull()
    {
        // Arrange
        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.SourceStore!, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        // Act
        var result = await _resolver.ResolveCategoryTreeAsync(_testMigrationRequest);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithCancellation_ShouldCancelOperation()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);

        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.SourceStore!, cancellationToken))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            _resolver.ResolveCategoryTreeAsync(_testMigrationRequest, cancellationToken));
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithDifferentChannels_ShouldReturnCorrectContext()
    {
        // Arrange
        var sourceStore = TestDataFactory.CreateStoreConfiguration("store1", "token1", "5");
        var destStore = TestDataFactory.CreateStoreConfiguration("store2", "token2", "10");
        var customRequest = TestDataFactory.CreateMigrationRequest(sourceStore, destStore);

        var sourceTrees = TestDataFactory.CreateMockCategoryTrees("5", "tree-5");
        var destTrees = TestDataFactory.CreateMockCategoryTrees("10", "tree-10");

        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(sourceStore, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceTrees);
        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(destStore, It.IsAny<CancellationToken>()))
            .ReturnsAsync(destTrees);

        // Act
        var result = await _resolver.ResolveCategoryTreeAsync(customRequest);

        // Assert
        result.Should().NotBeNull();
        result!.SourceChannelId.Should().Be("5");
        result.DestinationChannelId.Should().Be("10");
        result.SourceCategoryTreeId.Should().Be("tree-5");
        result.DestinationCategoryTreeId.Should().Be("tree-10");
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithMultipleTreesPerChannel_ShouldReturnFirstMatch()
    {
        // Arrange - Create multiple trees for the same channel using proper JSON format
        var jsonData = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "tree-1",
                name = "First Tree",
                channels = new[]
                {
                    new { channel_id = "1" }
                }
            },
            new
            {
                id = "tree-2",
                name = "Second Tree",
                channels = new[]
                {
                    new { channel_id = "1" }
                }
            }
        });
        
        var jsonDocument = System.Text.Json.JsonDocument.Parse(jsonData);
        var sourceTrees = new List<Dictionary<string, object>>();
        
        foreach (var element in jsonDocument.RootElement.EnumerateArray())
        {
            var dict = new Dictionary<string, object>();
            foreach (var property in element.EnumerateObject())
            {
                dict[property.Name] = property.Value.Clone();
            }
            sourceTrees.Add(dict);
        }

        var destTrees = TestDataFactory.CreateMockCategoryTrees("2", "dest-tree");

        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.SourceStore!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceTrees);
        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.DestinationStore!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(destTrees);

        // Act
        var result = await _resolver.ResolveCategoryTreeAsync(_testMigrationRequest);

        // Assert
        result.Should().NotBeNull();
        result!.SourceCategoryTreeId.Should().Be("tree-1"); // Should return first match
        result.DestinationCategoryTreeId.Should().Be("dest-tree");
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithNoMatchingChannels_ShouldReturnNull()
    {
        // Arrange
        var sourceTrees = TestDataFactory.CreateMockCategoryTrees("999", "tree-999"); // Wrong channel
        var destTrees = TestDataFactory.CreateMockCategoryTrees("2", "dest-tree");

        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.SourceStore!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceTrees);
        _mockApiClient.Setup(x => x.GetCategoryTreesAsync(_testMigrationRequest.DestinationStore!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(destTrees);

        // Act
        var result = await _resolver.ResolveCategoryTreeAsync(_testMigrationRequest);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithValidDependencies_ShouldCreateInstance()
    {
        // Act
        var resolver = new CategoryTreeResolver(_mockApiClient.Object, _mockOpenSearchService.Object, _mockLogger.Object);

        // Assert
        resolver.Should().NotBeNull();
        resolver.Should().BeOfType<CategoryTreeResolver>();
    }

    [Fact]
    public void Constructor_WithNullApiClient_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CategoryTreeResolver(null!, _mockOpenSearchService.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullOpenSearchService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CategoryTreeResolver(_mockApiClient.Object, null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CategoryTreeResolver(_mockApiClient.Object, _mockOpenSearchService.Object, null!));
    }
} 