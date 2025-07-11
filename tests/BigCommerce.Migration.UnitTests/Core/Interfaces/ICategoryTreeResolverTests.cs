using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.UnitTests.TestHelpers;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Interfaces;

/// <summary>
/// Unit tests for ICategoryTreeResolver interface contracts
/// Tests method signatures and basic mock behavior with MigrationRequest-based architecture
/// </summary>
public class ICategoryTreeResolverTests
{
    private readonly Mock<ICategoryTreeResolver> _mockResolver;
    private readonly MigrationRequest _testMigrationRequest;

    public ICategoryTreeResolverTests()
    {
        _mockResolver = new Mock<ICategoryTreeResolver>();
        _testMigrationRequest = TestDataFactory.CreateMigrationRequest();
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithValidMigrationRequest_ShouldReturnContext()
    {
        // Arrange
        var expectedContext = TestDataFactory.CreateCategoryTreeContext("1", "2", "tree-1", "tree-2");
        _mockResolver
            .Setup(x => x.ResolveCategoryTreeAsync(_testMigrationRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedContext);

        // Act
        var result = await _mockResolver.Object.ResolveCategoryTreeAsync(_testMigrationRequest);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedContext);
        result!.SourceChannelId.Should().Be("1");
        result.DestinationChannelId.Should().Be("2");
        result.SourceCategoryTreeId.Should().Be("tree-1");
        result.DestinationCategoryTreeId.Should().Be("tree-2");
        _mockResolver.Verify(x => x.ResolveCategoryTreeAsync(_testMigrationRequest, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithInvalidMigrationRequest_ShouldReturnNull()
    {
        // Arrange
        var invalidRequest = new MigrationRequest
        {
            SourceStore = null, // Invalid - missing source store
            DestinationStore = TestDataFactory.CreateDestinationStoreConfiguration(),
            Entities = new List<string> { "categories" }
        };
        
        _mockResolver
            .Setup(x => x.ResolveCategoryTreeAsync(invalidRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CategoryTreeContext?)null);

        // Act
        var result = await _mockResolver.Object.ResolveCategoryTreeAsync(invalidRequest);

        // Assert
        result.Should().BeNull();
        _mockResolver.Verify(x => x.ResolveCategoryTreeAsync(invalidRequest, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithCancellation_ShouldPassCancellationToken()
    {
        // Arrange
        var cancellationToken = new CancellationToken(true);
        _mockResolver
            .Setup(x => x.ResolveCategoryTreeAsync(_testMigrationRequest, cancellationToken))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _mockResolver.Object.ResolveCategoryTreeAsync(_testMigrationRequest, cancellationToken));
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithDifferentChannels_ShouldHandleCorrectly()
    {
        // Arrange
        var sourceStore = TestDataFactory.CreateStoreConfiguration("store1", "token1", "5");
        var destStore = TestDataFactory.CreateStoreConfiguration("store2", "token2", "10");
        var customRequest = TestDataFactory.CreateMigrationRequest(sourceStore, destStore);
        var expectedContext = TestDataFactory.CreateCategoryTreeContext("5", "10", "tree-5", "tree-10");
        
        _mockResolver
            .Setup(x => x.ResolveCategoryTreeAsync(customRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedContext);

        // Act
        var result = await _mockResolver.Object.ResolveCategoryTreeAsync(customRequest);

        // Assert
        result.Should().NotBeNull();
        result!.SourceChannelId.Should().Be("5");
        result.DestinationChannelId.Should().Be("10");
        result.SourceCategoryTreeId.Should().Be("tree-5");
        result.DestinationCategoryTreeId.Should().Be("tree-10");
    }

    [Fact]
    public async Task ResolveCategoryTreeAsync_WithMissingDestinationStore_ShouldReturnNull()
    {
        // Arrange
        var invalidRequest = new MigrationRequest
        {
            SourceStore = TestDataFactory.CreateSourceStoreConfiguration(),
            DestinationStore = null, // Invalid - missing destination store
            Entities = new List<string> { "categories" }
        };
        
        _mockResolver
            .Setup(x => x.ResolveCategoryTreeAsync(invalidRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CategoryTreeContext?)null);

        // Act
        var result = await _mockResolver.Object.ResolveCategoryTreeAsync(invalidRequest);

        // Assert
        result.Should().BeNull();
    }
} 