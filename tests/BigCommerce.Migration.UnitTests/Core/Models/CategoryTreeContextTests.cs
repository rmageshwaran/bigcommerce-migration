using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// Unit tests for CategoryTreeContext model
/// Tests category tree ID resolution for multi-storefront support
/// </summary>
public class CategoryTreeContextTests
{
    [Fact]
    public void CategoryTreeContext_WithValidTreeIds_ShouldCreateSuccessfully()
    {
        // Arrange
        var sourceChannelId = "1";
        var destinationChannelId = "2";
        var sourceTreeId = "tree-123";
        var destinationTreeId = "tree-456";

        // Act
        var context = new CategoryTreeContext
        {
            SourceChannelId = sourceChannelId,
            DestinationChannelId = destinationChannelId,
            SourceCategoryTreeId = sourceTreeId,
            DestinationCategoryTreeId = destinationTreeId
        };

        // Assert
        context.SourceChannelId.Should().Be(sourceChannelId);
        context.DestinationChannelId.Should().Be(destinationChannelId);
        context.SourceCategoryTreeId.Should().Be(sourceTreeId);
        context.DestinationCategoryTreeId.Should().Be(destinationTreeId);
    }

    [Fact]
    public void CategoryTreeContext_WithNullTreeIds_ShouldAllowNullValues()
    {
        // Arrange & Act
        var context = new CategoryTreeContext
        {
            SourceChannelId = "1",
            DestinationChannelId = "2",
            SourceCategoryTreeId = null,
            DestinationCategoryTreeId = null
        };

        // Assert
        context.SourceCategoryTreeId.Should().BeNull();
        context.DestinationCategoryTreeId.Should().BeNull();
        context.SourceChannelId.Should().Be("1");
        context.DestinationChannelId.Should().Be("2");
    }

    [Fact]
    public void CategoryTreeContext_ShouldHaveParameterlessConstructor()
    {
        // Act
        var context = new CategoryTreeContext();

        // Assert
        context.Should().NotBeNull();
        context.SourceChannelId.Should().BeNull();
        context.DestinationChannelId.Should().BeNull();
        context.SourceCategoryTreeId.Should().BeNull();
        context.DestinationCategoryTreeId.Should().BeNull();
    }

    [Theory]
    [InlineData("1", "2", "tree-abc", "tree-xyz")]
    [InlineData("5", "10", "tree-source", "tree-dest")]
    [InlineData("100", "200", "category-tree-1", "category-tree-2")]
    public void CategoryTreeContext_WithDifferentValues_ShouldCreateCorrectly(
        string sourceChannel,
        string destChannel,
        string sourceTree,
        string destTree)
    {
        // Arrange & Act
        var context = new CategoryTreeContext
        {
            SourceChannelId = sourceChannel,
            DestinationChannelId = destChannel,
            SourceCategoryTreeId = sourceTree,
            DestinationCategoryTreeId = destTree
        };

        // Assert
        context.SourceChannelId.Should().Be(sourceChannel);
        context.DestinationChannelId.Should().Be(destChannel);
        context.SourceCategoryTreeId.Should().Be(sourceTree);
        context.DestinationCategoryTreeId.Should().Be(destTree);
    }
} 