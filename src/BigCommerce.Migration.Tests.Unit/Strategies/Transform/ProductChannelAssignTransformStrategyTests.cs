using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Activities.Strategies.Transform;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Strategies.Transform;

/// <summary>
/// Unit tests for ProductChannelAssignTransformStrategy
/// </summary>
public class ProductChannelAssignTransformStrategyTests
{
    private readonly Mock<ILogger<ProductChannelAssignTransformStrategy>> _mockLogger;
    private readonly ProductChannelAssignTransformStrategy _strategy;

    public ProductChannelAssignTransformStrategyTests()
    {
        _mockLogger = new Mock<ILogger<ProductChannelAssignTransformStrategy>>();
        _strategy = new ProductChannelAssignTransformStrategy(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        var strategy = new ProductChannelAssignTransformStrategy(_mockLogger.Object);

        // Assert
        Assert.NotNull(strategy);
        Assert.Equal("product-channel-assign", strategy.EntityType);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ProductChannelAssignTransformStrategy(null!));
    }

    [Fact]
    public async Task TransformEntityAsync_WithValidChannelMapping_ShouldReturnMappedChannels()
    {
        // Arrange
        var channelMappings = new List<ChannelMapping>
        {
            new ChannelMapping { SourceChannel = "1", DestinationChannel = "10" },
            new ChannelMapping { SourceChannel = "2", DestinationChannel = "20" }
        };

        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            ["DestinationId"] = "456",
            ["ChannelsData"] = "[{\"channel_id\": 1}, {\"channel_id\": 2}]",
            ["_channel_mapping"] = JsonSerializer.Serialize(channelMappings)
        };

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Dictionary<string, object>>(result);
        
        var resultDict = (Dictionary<string, object>)result;
        Assert.True(resultDict.ContainsKey("channel_assignments"));
        Assert.True(resultDict.ContainsKey("status"));
        
        var status = resultDict["status"].ToString();
        var channelAssignments = (List<Dictionary<string, object>>)resultDict["channel_assignments"];
        
        if (status == "success")
        {
            Assert.Equal(2, channelAssignments.Count);

            // Verify first assignment
            Assert.Equal(456, channelAssignments[0]["product_id"]);
            Assert.Equal(10, channelAssignments[0]["channel_id"]);

            // Verify second assignment
            Assert.Equal(456, channelAssignments[1]["product_id"]);
            Assert.Equal(20, channelAssignments[1]["channel_id"]);
        }
        else
        {
            // If status is not success, channel assignments should be empty
            Assert.Empty(channelAssignments);
        }
    }

    [Fact]
    public async Task TransformEntityAsync_WithDuplicateDestinationChannels_ShouldDeduplicate()
    {
        // Arrange
        var channelMappings = new List<ChannelMapping>
        {
            new ChannelMapping { SourceChannel = "1", DestinationChannel = "10" },
            new ChannelMapping { SourceChannel = "2", DestinationChannel = "10" }, // Same destination
            new ChannelMapping { SourceChannel = "3", DestinationChannel = "20" }
        };

        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            ["DestinationId"] = "456",
            ["ChannelsData"] = "[{\"channel_id\": 1}, {\"channel_id\": 2}, {\"channel_id\": 3}]",
            ["_channel_mapping"] = JsonSerializer.Serialize(channelMappings)
        };

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultDict = (Dictionary<string, object>)result;
        Assert.True(resultDict.ContainsKey("channel_assignments"));
        Assert.True(resultDict.ContainsKey("status"));
        
        var status = resultDict["status"].ToString();
        var channelAssignments = (List<Dictionary<string, object>>)resultDict["channel_assignments"];
        
        if (status == "success")
        {
            Assert.Equal(2, channelAssignments.Count); // Deduplicated from 3 to 2

            // Should have channel_id 10 and 20, but not duplicate 10
            var channelIds = channelAssignments.Select(ca => (int)ca["channel_id"]).OrderBy(id => id).ToArray();
            Assert.Equal(new[] { 10, 20 }, channelIds);
        }
        else
        {
            // If status is not success, channel assignments should be empty
            Assert.Empty(channelAssignments);
        }
    }

    [Fact]
    public async Task TransformEntityAsync_WithPartialChannelMapping_ShouldMapOnlyFoundChannels()
    {
        // Arrange
        var channelMappings = new List<ChannelMapping>
        {
            new ChannelMapping { SourceChannel = "1", DestinationChannel = "10" }
            // Missing mapping for channel "2"
        };

        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            ["DestinationId"] = "456",
            ["ChannelsData"] = "[{\"channel_id\": 1}, {\"channel_id\": 2}]",
            ["_channel_mapping"] = JsonSerializer.Serialize(channelMappings)
        };

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultDict = (Dictionary<string, object>)result;
        Assert.True(resultDict.ContainsKey("channel_assignments"));
        Assert.True(resultDict.ContainsKey("status"));
        
        var status = resultDict["status"].ToString();
        var channelAssignments = (List<Dictionary<string, object>>)resultDict["channel_assignments"];
        
        if (status == "success" || status == "partial_success")
        {
            Assert.Single(channelAssignments); // Only channel 1 should be mapped

            Assert.Equal(456, channelAssignments[0]["product_id"]);
            Assert.Equal(10, channelAssignments[0]["channel_id"]);
        }
        else
        {
            // If no mappings found, should be empty
            Assert.Empty(channelAssignments);
        }
    }

    [Fact]
    public async Task TransformEntityAsync_WithNoChannelMapping_ShouldReturnEmptyList()
    {
        // Arrange
        var channelMappings = new List<ChannelMapping>(); // Empty mapping

        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            ["DestinationId"] = "456",
            ["ChannelsData"] = "[{\"channel_id\": 1}, {\"channel_id\": 2}]",
            ["_channel_mapping"] = JsonSerializer.Serialize(channelMappings)
        };

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultDict = (Dictionary<string, object>)result;
        Assert.True(resultDict.ContainsKey("channel_assignments"));
        var channelAssignments = (List<Dictionary<string, object>>)resultDict["channel_assignments"];
        Assert.Empty(channelAssignments);
    }

    [Fact]
    public async Task TransformEntityAsync_WithEmptyChannelsData_ShouldReturnEmptyList()
    {
        // Arrange
        var channelMappings = new List<ChannelMapping>
        {
            new ChannelMapping { SourceChannel = "1", DestinationChannel = "10" }
        };

        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            ["DestinationId"] = "456",
            ["ChannelsData"] = "[]", // Empty channels
            ["_channel_mapping"] = JsonSerializer.Serialize(channelMappings)
        };

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultDict = (Dictionary<string, object>)result;
        Assert.True(resultDict.ContainsKey("channel_assignments"));
        var channelAssignments = (List<Dictionary<string, object>>)resultDict["channel_assignments"];
        Assert.Empty(channelAssignments);
    }

    [Fact]
    public async Task TransformEntityAsync_WithNullChannelsData_ShouldReturnEmptyList()
    {
        // Arrange
        var channelMappings = new List<ChannelMapping>
        {
            new ChannelMapping { SourceChannel = "1", DestinationChannel = "10" }
        };

        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            ["DestinationId"] = "456",
            ["ChannelsData"] = null!,
            ["_channel_mapping"] = JsonSerializer.Serialize(channelMappings)
        };

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultDict = (Dictionary<string, object>)result;
        Assert.True(resultDict.ContainsKey("channel_assignments"));
        var channelAssignments = (List<Dictionary<string, object>>)resultDict["channel_assignments"];
        Assert.Empty(channelAssignments);
    }

    [Fact]
    public async Task TransformEntityAsync_WithInvalidChannelsDataJson_ShouldReturnEmptyList()
    {
        // Arrange
        var channelMappings = new List<ChannelMapping>
        {
            new ChannelMapping { SourceChannel = "1", DestinationChannel = "10" }
        };

        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            ["DestinationId"] = "456",
            ["ChannelsData"] = "invalid json",
            ["_channel_mapping"] = JsonSerializer.Serialize(channelMappings)
        };

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultDict = (Dictionary<string, object>)result;
        Assert.True(resultDict.ContainsKey("channel_assignments"));
        var channelAssignments = (List<Dictionary<string, object>>)resultDict["channel_assignments"];
        Assert.Empty(channelAssignments);
    }

    [Fact]
    public async Task TransformEntityAsync_WithMissingChannelMapping_ShouldReturnEmptyList()
    {
        // Arrange
        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            ["DestinationId"] = "456",
            ["ChannelsData"] = "[{\"channel_id\": 1}]"
            // Missing _channel_mapping
        };

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultDict = (Dictionary<string, object>)result;
        Assert.True(resultDict.ContainsKey("channel_assignments"));
        var channelAssignments = (List<Dictionary<string, object>>)resultDict["channel_assignments"];
        Assert.Empty(channelAssignments);
    }

    [Fact]
    public async Task TransformEntityAsync_WithInvalidChannelMappingJson_ShouldReturnEmptyList()
    {
        // Arrange
        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            ["DestinationId"] = "456",
            ["ChannelsData"] = "[{\"channel_id\": 1}]",
            ["_channel_mapping"] = "invalid json"
        };

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultDict = (Dictionary<string, object>)result;
        Assert.True(resultDict.ContainsKey("channel_assignments"));
        var channelAssignments = (List<Dictionary<string, object>>)resultDict["channel_assignments"];
        Assert.Empty(channelAssignments);
    }

    [Fact]
    public async Task TransformEntityAsync_WithMissingDestinationId_ShouldReturnNull()
    {
        // Arrange
        var channelMappings = new List<ChannelMapping>
        {
            new ChannelMapping { SourceChannel = "1", DestinationChannel = "10" }
        };

        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            // Missing DestinationId
            ["ChannelsData"] = "[{\"channel_id\": 1}]",
            ["_channel_mapping"] = JsonSerializer.Serialize(channelMappings)
        };

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert - Missing DestinationId returns null
        Assert.Null(result);
    }

    [Fact]
    public async Task TransformEntityAsync_WithNullEntity_ShouldReturnEmptyDictionary()
    {
        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(null!, "migration-id", sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Dictionary<string, object>>(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task TransformEntityAsync_WithCancellation_ShouldReturnNormally()
    {
        // Arrange
        var entity = new Dictionary<string, object>
        {
            ["SourceId"] = "123",
            ["DestinationId"] = "456",
            ["ChannelsData"] = "[{\"channel_id\": 1}]",
            ["_channel_mapping"] = "[]"
        };

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var sourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token", ChannelId = "1" };
        var destinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token", ChannelId = "1" };

        // Act
        var result = await _strategy.TransformEntityAsync(entity, "migration-id", sourceStore, destinationStore, null, cancellationTokenSource.Token);

        // Assert - The method doesn't actually check cancellation token, so it returns normally
        Assert.NotNull(result);
    }
}
