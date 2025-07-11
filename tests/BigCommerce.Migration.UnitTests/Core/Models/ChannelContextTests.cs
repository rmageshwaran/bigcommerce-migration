using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// Unit tests for ChannelContext model
/// Tests complete channel context including category tree resolution
/// </summary>
public class ChannelContextTests
{
    [Fact]
    public void ChannelContext_WithCompleteData_ShouldCreateSuccessfully()
    {
        // Arrange
        var sourceChannelId = "1";
        var destinationChannelId = "2";
        var sourceTreeId = "tree-123";
        var destinationTreeId = "tree-456";

        // Act
        var context = new ChannelContext
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
    public void ChannelContext_WithMinimalData_ShouldCreateSuccessfully()
    {
        // Arrange & Act
        var context = new ChannelContext
        {
            SourceChannelId = "1",
            DestinationChannelId = "2"
        };

        // Assert
        context.SourceChannelId.Should().Be("1");
        context.DestinationChannelId.Should().Be("2");
        context.SourceCategoryTreeId.Should().BeNull();
        context.DestinationCategoryTreeId.Should().BeNull();
    }

    [Fact]
    public void ChannelContext_DefaultConstructor_ShouldInitializeAllPropertiesAsNull()
    {
        // Act
        var context = new ChannelContext();

        // Assert
        context.SourceChannelId.Should().BeNull();
        context.DestinationChannelId.Should().BeNull();
        context.SourceCategoryTreeId.Should().BeNull();
        context.DestinationCategoryTreeId.Should().BeNull();
    }

    [Fact]
    public void ChannelContext_ShouldSupportChannelOnlyMigrations()
    {
        // Arrange - Migration that doesn't involve categories
        var context = new ChannelContext
        {
            SourceChannelId = "5",
            DestinationChannelId = "10"
            // Tree IDs intentionally null - not needed for non-category migrations
        };

        // Assert
        context.SourceChannelId.Should().Be("5");
        context.DestinationChannelId.Should().Be("10");
        context.SourceCategoryTreeId.Should().BeNull();
        context.DestinationCategoryTreeId.Should().BeNull();
    }

    [Fact]
    public void ChannelContext_ShouldSupportSameChannelMigration()
    {
        // Arrange - Migration within the same channel (different stores)
        var sameChannelId = "1";
        var context = new ChannelContext
        {
            SourceChannelId = sameChannelId,
            DestinationChannelId = sameChannelId,
            SourceCategoryTreeId = "tree-abc",
            DestinationCategoryTreeId = "tree-xyz"  // Different tree IDs even with same channel
        };

        // Assert
        context.SourceChannelId.Should().Be(sameChannelId);
        context.DestinationChannelId.Should().Be(sameChannelId);
        context.SourceCategoryTreeId.Should().Be("tree-abc");
        context.DestinationCategoryTreeId.Should().Be("tree-xyz");
    }
} 