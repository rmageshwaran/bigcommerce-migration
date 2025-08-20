using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Utilities;
using BigCommerce.Migration.Core.Interfaces;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Orchestrators;

public class EntityMigrationOrchestratorTests
{
    // Note: These tests focus on pure logic without mocking complex orchestration framework types
    // The actual orchestrator logic will be tested through integration tests

    [Fact]
    public void GetBooleanValue_WithTrue_ReturnsTrue()
    {
        // Arrange
        var jsonElement = JsonSerializer.SerializeToElement(true);
        
        // Act
        var result = JsonElementHelper.GetBooleanValue(jsonElement);
        
        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetBooleanValue_WithFalse_ReturnsFalse()
    {
        // Arrange
        var jsonElement = JsonSerializer.SerializeToElement(false);
        
        // Act
        var result = JsonElementHelper.GetBooleanValue(jsonElement);
        
        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetBooleanValue_WithTrueString_ReturnsTrue()
    {
        // Arrange
        var jsonElement = JsonSerializer.SerializeToElement("true");
        
        // Act
        var result = JsonElementHelper.GetBooleanValue(jsonElement);
        
        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetBooleanValue_WithBooleanObject_ReturnsCorrectValue()
    {
        // Arrange - Direct boolean
        bool value = true;
        
        // Act
        var result = JsonElementHelper.GetBooleanValue(value);
        
        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetBooleanValue_WithNull_ReturnsFalse()
    {
        // Act
        var result = JsonElementHelper.GetBooleanValue(null);
        
        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    public void GetBooleanValue_WithStringValues_ReturnsExpected(string input, bool expected)
    {
        // Act
        var result = JsonElementHelper.GetBooleanValue(input);
        
        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ProgressiveDiscovery_ChunkSizeCalculation_ShouldUseProductCount()
    {
        // Arrange
        var discoveryResult = new EntityDiscoveryResult
        {
            EntityType = "options",
            TotalCount = 0, // Progressive discovery returns 0
            EntityIds = new List<string>(),
            PaginationMetadata = new Dictionary<string, object>
            {
                { "ProgressiveDiscovery", true },
                { "TotalProducts", 4791 },
                { "TotalPages", 480 },
                { "PageSize", 250 }
            }
        };

        var entityConfig = new EntityConfiguration
        {
            EntityType = "options",
            ChunkSize = 250
        };

        // Act
        var (useDirectPagination, totalEntities, shouldUseChunking, batchSize) = 
            CalculateChunkingParameters(discoveryResult, entityConfig);

        // Assert
        useDirectPagination.Should().BeTrue("Progressive discovery should use direct pagination");
        totalEntities.Should().Be(4791, "Should use TotalProducts from metadata for progressive discovery");
        shouldUseChunking.Should().BeTrue("Large product count should enable chunking");
        batchSize.Should().Be(250, "Should use configured chunk size for progressive discovery");
    }

    [Fact]
    public void StandardDiscovery_ChunkSizeCalculation_ShouldUseTotalCount()
    {
        // Arrange
        var discoveryResult = new EntityDiscoveryResult
        {
            EntityType = "products",
            TotalCount = 4791,
            EntityIds = new List<string>(),
            PaginationMetadata = new Dictionary<string, object>
            {
                { "ApiVersion", "V3" }
            }
        };

        var entityConfig = new EntityConfiguration
        {
            EntityType = "products",
            ChunkSize = 250
        };

        // Act
        var (useDirectPagination, totalEntities, shouldUseChunking, batchSize) = 
            CalculateChunkingParameters(discoveryResult, entityConfig);

        // Assert
        useDirectPagination.Should().BeTrue("Should use direct pagination when EntityIds is empty and TotalCount > 0");
        totalEntities.Should().Be(4791, "Should use TotalCount from discovery result");
        shouldUseChunking.Should().BeTrue("Large entity count should enable chunking");
        batchSize.Should().Be(250, "Should use configured chunk size");
    }

    [Fact]
    public void SmallDataset_ChunkSizeCalculation_ShouldDisableChunking()
    {
        // Arrange
        var discoveryResult = new EntityDiscoveryResult
        {
            EntityType = "brands",
            TotalCount = 50,
            EntityIds = new List<string>(),
            PaginationMetadata = new Dictionary<string, object>()
        };

        var entityConfig = new EntityConfiguration
        {
            EntityType = "brands",
            ChunkSize = 250
        };

        // Act
        var (useDirectPagination, totalEntities, shouldUseChunking, batchSize) = 
            CalculateChunkingParameters(discoveryResult, entityConfig);

        // Assert
        useDirectPagination.Should().BeTrue("Should use direct pagination");
        totalEntities.Should().Be(50, "Should use TotalCount");
        shouldUseChunking.Should().BeFalse("Small dataset should not use chunking");
        batchSize.Should().Be(50, "Should use totalEntities for small datasets");
    }

    [Fact]
    public void ProgressiveDiscovery_WithZeroProducts_ShouldNotProcess()
    {
        // Arrange
        var discoveryResult = new EntityDiscoveryResult
        {
            EntityType = "options",
            TotalCount = 0,
            EntityIds = new List<string>(),
            PaginationMetadata = new Dictionary<string, object>
            {
                { "ProgressiveDiscovery", true },
                { "TotalProducts", 0 }, // No products to process
                { "TotalPages", 0 }
            }
        };

        var entityConfig = new EntityConfiguration
        {
            EntityType = "options",
            ChunkSize = 250
        };

        // Act
        var (useDirectPagination, totalEntities, shouldUseChunking, batchSize) = 
            CalculateChunkingParameters(discoveryResult, entityConfig);

        // Assert
        useDirectPagination.Should().BeTrue("Progressive discovery should use direct pagination");
        totalEntities.Should().Be(0, "No products means no processing needed");
        shouldUseChunking.Should().BeFalse("Zero entities should not use chunking");
        batchSize.Should().Be(0, "Batch size should be zero when no entities to process");
    }

    // Helper method to simulate the chunking calculation logic
    private static (bool useDirectPagination, int totalEntities, bool shouldUseChunking, int batchSize) 
        CalculateChunkingParameters(EntityDiscoveryResult discoveryResult, EntityConfiguration entityConfig)
    {
        var entityIds = discoveryResult.EntityIds ?? new List<string>();
        
        // Progressive discovery check
        var isProgressiveDiscovery = discoveryResult.PaginationMetadata?.ContainsKey("ProgressiveDiscovery") == true &&
                                   JsonElementHelper.GetBooleanValue(discoveryResult.PaginationMetadata["ProgressiveDiscovery"]);
        
        // Handle efficient pagination strategy
        var useDirectPagination = !entityIds.Any() && ((discoveryResult.TotalCount) > 0 || isProgressiveDiscovery);
        var totalEntities = useDirectPagination ? discoveryResult.TotalCount : entityIds.Count;
        
        // Progressive discovery override
        if (isProgressiveDiscovery && totalEntities == 0)
        {
            var totalProducts = 0;
            if (discoveryResult.PaginationMetadata?.ContainsKey("TotalProducts") == true)
            {
                totalProducts = JsonElementHelper.GetIntegerValue(discoveryResult.PaginationMetadata["TotalProducts"]);
            }
            totalEntities = totalProducts;
        }
        
        // Chunking logic
        var chunkingThreshold = 100;
        var chunkSize = entityConfig.ChunkSize;
        var shouldUseChunking = totalEntities > chunkingThreshold;
        var batchSize = shouldUseChunking ? chunkSize : totalEntities;
        
        return (useDirectPagination, totalEntities, shouldUseChunking, batchSize);
    }


}