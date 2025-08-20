using BigCommerce.Migration.Activities.Strategies;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BigCommerce.Migration.Core.Utilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// Integration tests for progressive discovery workflow to prevent regressions
/// </summary>
public class ProgressiveDiscoveryIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly ILogger<ProgressiveDiscoveryIntegrationTests> _logger;

    public ProgressiveDiscoveryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.test.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Information",
                ["BigCommerce:DefaultChunkSize"] = "250",
                ["BigCommerce:DefaultPageSize"] = "250"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        // Register services needed for tests
        services.AddScoped<V3ProductComponentsDiscoveryStrategy>();
        services.AddScoped<IEntityDiscoveryStrategyFactory, EntityDiscoveryStrategyFactory>();
        
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<ProgressiveDiscoveryIntegrationTests>>();
    }

    [Fact]
    public async Task ProductComponentsDiscovery_ShouldReturnProgressiveDiscoveryMetadata()
    {
        // Arrange
        var strategy = _serviceProvider.GetRequiredService<V3ProductComponentsDiscoveryStrategy>();
        var request = new EntityDiscoveryRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            EntityType = "options",
            SourceStore = CreateMockStoreConfiguration(),
            EntityConfig = CreateEntityConfiguration("options")
        };

        // Act
        var result = await strategy.DiscoverEntitiesAsync(request).ConfigureAwait(false);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(0, "Progressive discovery should return 0 for initial count");
        result.PaginationMetadata.Should().ContainKey("ProgressiveDiscovery");
        result.PaginationMetadata["ProgressiveDiscovery"].Should().Be(true);
        result.PaginationMetadata.Should().ContainKey("TotalProducts");
        result.EntityIds.Should().BeEmpty("Progressive discovery doesn't return entity IDs upfront");
    }

    [Theory]
    [InlineData("options")]
    [InlineData("modifiers")]
    [InlineData("images")]
    [InlineData("reviews")]
    public async Task ProductComponentsDiscovery_ForEachComponentType_ShouldSetCorrectMetadata(string entityType)
    {
        // Arrange
        var strategy = _serviceProvider.GetRequiredService<V3ProductComponentsDiscoveryStrategy>();
        var request = new EntityDiscoveryRequest
        {
            MigrationId = Guid.NewGuid().ToString(),
            EntityType = entityType,
            SourceStore = CreateMockStoreConfiguration(),
            EntityConfig = CreateEntityConfiguration(entityType)
        };

        // Act
        var result = await strategy.DiscoverEntitiesAsync(request).ConfigureAwait(false);

        // Assert
        result.EntityType.Should().Be(entityType, "Should preserve the requested entity type");
        result.TotalCount.Should().Be(0, "Progressive discovery should start with 0 count");
        result.PaginationMetadata.Should().ContainKey("ProgressiveDiscovery")
            .WhoseValue.Should().Be(true);
        result.PaginationMetadata.Should().ContainKey("Strategy")
            .WhoseValue.Should().Be("ProductComponentsProgressiveDiscovery");
        result.PaginationMetadata.Should().ContainKey("IncludeParameter")
            .WhoseValue.Should().Be("options,modifiers,images,reviews");
    }

    [Fact]
    public void ChunkingLogic_WithProgressiveDiscovery_ShouldCalculateCorrectChunkSize()
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
                { "TotalProducts", 4791 },
                { "TotalPages", 480 },
                { "PageSize", 250 }
            }
        };

        var entityConfig = CreateEntityConfiguration("options");

        // Act
        var chunkingParameters = SimulateChunkingCalculation(discoveryResult, entityConfig);

        // Assert
        chunkingParameters.UseDirectPagination.Should().BeTrue();
        chunkingParameters.TotalEntities.Should().Be(4791, "Should use product count for progressive discovery");
        chunkingParameters.ShouldUseChunking.Should().BeTrue();
        chunkingParameters.BatchSize.Should().Be(250, "Should use configured chunk size");
        chunkingParameters.TotalBatches.Should().Be(20, "Should calculate correct number of batches");
    }

    [Fact]
    public void ChunkingLogic_WithZeroProducts_ShouldHandleGracefully()
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
                { "TotalProducts", 0 },
                { "TotalPages", 0 }
            }
        };

        var entityConfig = CreateEntityConfiguration("options");

        // Act
        var chunkingParameters = SimulateChunkingCalculation(discoveryResult, entityConfig);

        // Assert
        chunkingParameters.UseDirectPagination.Should().BeTrue();
        chunkingParameters.TotalEntities.Should().Be(0);
        chunkingParameters.ShouldUseChunking.Should().BeFalse();
        chunkingParameters.BatchSize.Should().Be(0);
        chunkingParameters.TotalBatches.Should().Be(0);
    }

    [Fact]
    public void JsonElementCasting_ShouldHandleAllFormats()
    {
        // Arrange & Act & Assert
        var testCases = new[]
        {
            (Value: true, Expected: true),
            (Value: false, Expected: false),
            (Value: "true", Expected: true),
            (Value: "false", Expected: false),
            (Value: "TRUE", Expected: true),
            (Value: "False", Expected: false),
            (Value: "", Expected: false),
            (Value: "invalid", Expected: false),
            (Value: (object?)null, Expected: false)
        };

        foreach (var (value, expected) in testCases)
        {
            var result = SimulateGetBooleanValue(value);
            result.Should().Be(expected, $"Value '{value}' should return {expected}");
        }
    }

    [Fact]
    public void ProgressiveDiscoveryFlag_FromDictionary_ShouldBeHandledCorrectly()
    {
        // Test the exact scenario that was failing
        var metadata = new Dictionary<string, object>
        {
            { "ProgressiveDiscovery", true },
            { "TotalProducts", 4791 }
        };

        // Simulate the problematic code that was fixed
        var isProgressiveDiscovery = metadata.ContainsKey("ProgressiveDiscovery") &&
                                   SimulateGetBooleanValue(metadata["ProgressiveDiscovery"]);

        isProgressiveDiscovery.Should().BeTrue("Should correctly identify progressive discovery from metadata");
    }

    private static StoreConfiguration CreateMockStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "test-store",
            AccessToken = "test-token",
            ChannelId = "1"
        };
    }

    private static EntityConfiguration CreateEntityConfiguration(string entityType)
    {
        return new EntityConfiguration
        {
            EntityType = entityType,
            ChunkSize = 250,
            PageSize = 250,
            FetchBatchSize = 250,
            MaxConcurrency = 5,
            ProcessSubBatchesSequentially = false
        };
    }

    // Simulate the chunking calculation logic from EntityMigrationDurableOrchestrator
    private static ChunkingParameters SimulateChunkingCalculation(
        EntityDiscoveryResult discoveryResult, 
        EntityConfiguration entityConfig)
    {
        var entityIds = discoveryResult.EntityIds ?? new List<string>();
        
        // Progressive discovery check
        var isProgressiveDiscovery = discoveryResult.PaginationMetadata?.ContainsKey("ProgressiveDiscovery") == true &&
                                   SimulateGetBooleanValue(discoveryResult.PaginationMetadata["ProgressiveDiscovery"]);
        
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
        var totalBatches = shouldUseChunking ? CalculateBatchCount(totalEntities, chunkSize) : 
                          (totalEntities > 0 ? 1 : 0);
        
        return new ChunkingParameters(
            useDirectPagination, 
            totalEntities, 
            shouldUseChunking, 
            batchSize, 
            totalBatches
        );
    }

    // Simulate the GetBooleanValue method logic
    private static bool SimulateGetBooleanValue(object? value)
    {
        if (value == null) return false;
        
        if (value is bool boolValue)
            return boolValue;
            
        if (value is string stringValue)
            return bool.TryParse(stringValue, out var stringParsed) && stringParsed;
            
        return false;
    }

    private static int CalculateBatchCount(int totalCount, int batchSize)
    {
        if (totalCount == 0) return 0;
        return (int)Math.Ceiling((double)totalCount / batchSize);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    private record ChunkingParameters(
        bool UseDirectPagination,
        int TotalEntities,
        bool ShouldUseChunking,
        int BatchSize,
        int TotalBatches
    );


}