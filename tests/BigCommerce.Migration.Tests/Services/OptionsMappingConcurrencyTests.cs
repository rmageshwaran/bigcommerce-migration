using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Concurrent;
using System.Text.Json;
using Xunit;

namespace BigCommerce.Migration.Tests.Services;

/// <summary>
/// Tests for concurrent options mapping scenarios to ensure thread safety and data integrity
/// Validates that multiple options can be created simultaneously without data corruption
/// </summary>
public class OptionsMappingConcurrencyTests
{
    private readonly Mock<ILogger<HierarchicalOptionMappingService>> _mockLogger;
    private readonly ConcurrentDictionary<string, EntityMapping> _inMemoryStorage;
    private readonly HierarchicalOptionMappingService _service;

    public OptionsMappingConcurrencyTests()
    {
        _mockLogger = new Mock<ILogger<HierarchicalOptionMappingService>>();
        _inMemoryStorage = new ConcurrentDictionary<string, EntityMapping>();
        
        var mockStorageService = new Mock<IMigrationStorageService>();
        
        // Setup thread-safe in-memory storage simulation
        mockStorageService.Setup(x => x.GetEntityMappingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string migrationId, string entityType, string sourceId) =>
            {
                var key = $"{migrationId}_{entityType}_{sourceId}";
                return _inMemoryStorage.GetValueOrDefault(key);
            });

        mockStorageService.Setup(x => x.UpdateEntityMappingAsync(It.IsAny<EntityMapping>()))
            .ReturnsAsync((EntityMapping mapping) =>
            {
                var key = $"{mapping.MigrationId}_{mapping.EntityType}_{mapping.SourceId}";
                _inMemoryStorage.AddOrUpdate(key, mapping, (k, v) => mapping);
                return mapping;
            });

        _service = new HierarchicalOptionMappingService(mockStorageService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ConcurrentOptionsMapping_MultipleOptionsForSameProduct_AllStoredCorrectly()
    {
        // Arrange
        var migrationId = "concurrent_test_123";
        var sourceProductId = "source_product_456";
        
        // Setup initial product mapping
        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = "dest_product_456",
            OptionsMappingData = null
        };
        
        var key = $"{migrationId}_products_{sourceProductId}";
        _inMemoryStorage[key] = productMapping;

        // Create 10 options concurrently
        var concurrentTasks = new List<Task>();
        var optionCount = 10;

        for (int i = 1; i <= optionCount; i++)
        {
            var optionIndex = i;
            var task = Task.Run(async () =>
            {
                var sourceOption = new Dictionary<string, object>
                {
                    ["id"] = $"source_option_{optionIndex}",
                    ["option_values"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["id"] = $"source_value_{optionIndex}_1",
                            ["label"] = $"Value {optionIndex}-1"
                        },
                        new Dictionary<string, object>
                        {
                            ["id"] = $"source_value_{optionIndex}_2", 
                            ["label"] = $"Value {optionIndex}-2"
                        }
                    }
                };

                var createdOption = new Dictionary<string, object>
                {
                    ["id"] = 200 + optionIndex,
                    ["option_values"] = JsonSerializer.Deserialize<JsonElement>($@"[
                        {{ ""id"": {300 + (optionIndex * 2) - 1}, ""label"": ""Value {optionIndex}-1"" }},
                        {{ ""id"": {300 + (optionIndex * 2)}, ""label"": ""Value {optionIndex}-2"" }}
                    ]")
                };

                await _service.StoreHierarchicalOptionMappingAsync(
                    sourceOption, createdOption, $"source_option_{optionIndex}", $"{200 + optionIndex}",
                    sourceProductId, migrationId);
            });
            
            concurrentTasks.Add(task);
        }

        // Act
        await Task.WhenAll(concurrentTasks);

        // Assert
        var finalMapping = _inMemoryStorage[key];
        Assert.NotNull(finalMapping.OptionsMappingData);

        var optionMappingsData = JsonSerializer.Deserialize<Dictionary<string, object>>(finalMapping.OptionsMappingData);
        var optionsArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
            ((JsonElement)optionMappingsData!["options"]).GetRawText());

        // All options should be stored
        Assert.Equal(optionCount, optionsArray!.Count);

        // Verify each option has correct structure
        for (int i = 1; i <= optionCount; i++)
        {
            var option = optionsArray.FirstOrDefault(o => 
                o["sourceOptionId"].ToString() == $"source_option_{i}");
            
            Assert.NotNull(option);
            Assert.Equal($"{200 + i}", option["destinationOptionId"].ToString());
            
            var optionValues = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
                ((JsonElement)option["optionValues"]).GetRawText());
            Assert.Equal(2, optionValues!.Count); // Each option has 2 values
        }
    }

    [Fact]
    public async Task ConcurrentOptionsMapping_RaceConditionOnUpdate_HandledGracefully()
    {
        // Arrange - Simulate race condition where multiple threads try to update the same mapping
        var migrationId = "race_condition_test";
        var sourceProductId = "source_product_race";
        
        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = "dest_product_race",
            OptionsMappingData = null
        };
        
        var key = $"{migrationId}_products_{sourceProductId}";
        _inMemoryStorage[key] = productMapping;

        // Create tasks that will all try to update at the same time
        var simultaneousTasks = new List<Task>();
        var taskCount = 20;

        for (int i = 1; i <= taskCount; i++)
        {
            var taskIndex = i;
            var task = Task.Run(async () =>
            {
                // Add a small random delay to increase chance of race conditions
                await Task.Delay(Random.Shared.Next(1, 10));

                var sourceOption = new Dictionary<string, object>
                {
                    ["id"] = $"race_option_{taskIndex}",
                    ["option_values"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["id"] = $"race_value_{taskIndex}",
                            ["label"] = $"Race Value {taskIndex}"
                        }
                    }
                };

                var createdOption = new Dictionary<string, object>
                {
                    ["id"] = 1000 + taskIndex,
                    ["option_values"] = JsonSerializer.Deserialize<JsonElement>($@"[
                        {{ ""id"": {2000 + taskIndex}, ""label"": ""Race Value {taskIndex}"" }}
                    ]")
                };

                await _service.StoreHierarchicalOptionMappingAsync(
                    sourceOption, createdOption, $"race_option_{taskIndex}", $"{1000 + taskIndex}",
                    sourceProductId, migrationId);
            });
            
            simultaneousTasks.Add(task);
        }

        // Act
        await Task.WhenAll(simultaneousTasks);

        // Assert - All tasks should complete successfully despite race conditions
        var finalMapping = _inMemoryStorage[key];
        Assert.NotNull(finalMapping.OptionsMappingData);

        var optionMappingsData = JsonSerializer.Deserialize<Dictionary<string, object>>(finalMapping.OptionsMappingData);
        var optionsArray = JsonSerializer.Deserialize<List<object>>(
            ((JsonElement)optionMappingsData!["options"]).GetRawText());

        // All options should be stored (may not be in order due to concurrency)
        Assert.Equal(taskCount, optionsArray!.Count);
    }

    [Theory]
    [InlineData(5, 50)]   // 5 options, 50ms max delay
    [InlineData(15, 100)] // 15 options, 100ms max delay
    [InlineData(25, 200)] // 25 options, 200ms max delay
    public async Task ConcurrentOptionsMapping_VariousLoadScenarios_MaintainsDataIntegrity(int optionCount, int maxDelayMs)
    {
        // Arrange
        var migrationId = $"load_test_{optionCount}_{maxDelayMs}";
        var sourceProductId = "source_product_load";
        
        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = "dest_product_load",
            OptionsMappingData = null
        };
        
        var key = $"{migrationId}_products_{sourceProductId}";
        _inMemoryStorage[key] = productMapping;

        // Create options with varying delays to simulate real-world timing
        var tasks = new List<Task>();
        var createdOptions = new ConcurrentBag<string>();

        for (int i = 1; i <= optionCount; i++)
        {
            var optionIndex = i;
            var task = Task.Run(async () =>
            {
                // Random delay to simulate varying API response times
                await Task.Delay(Random.Shared.Next(1, maxDelayMs));

                var sourceOptionId = $"load_option_{optionIndex}";
                var destinationOptionId = $"{5000 + optionIndex}";

                var sourceOption = new Dictionary<string, object>
                {
                    ["id"] = sourceOptionId,
                    ["option_values"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["id"] = $"load_value_{optionIndex}",
                            ["label"] = $"Load Value {optionIndex}"
                        }
                    }
                };

                var createdOption = new Dictionary<string, object>
                {
                    ["id"] = int.Parse(destinationOptionId, System.Globalization.CultureInfo.InvariantCulture),
                    ["option_values"] = JsonSerializer.Deserialize<JsonElement>($@"[
                        {{ ""id"": {6000 + optionIndex}, ""label"": ""Load Value {optionIndex}"" }}
                    ]")
                };

                await _service.StoreHierarchicalOptionMappingAsync(
                    sourceOption, createdOption, sourceOptionId, destinationOptionId,
                    sourceProductId, migrationId);

                createdOptions.Add(sourceOptionId);
            });
            
            tasks.Add(task);
        }

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        Assert.Equal(optionCount, createdOptions.Count); // All tasks completed

        var finalMapping = _inMemoryStorage[key];
        Assert.NotNull(finalMapping.OptionsMappingData);

        var optionMappingsData = JsonSerializer.Deserialize<Dictionary<string, object>>(finalMapping.OptionsMappingData);
        var optionsArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
            ((JsonElement)optionMappingsData!["options"]).GetRawText());

        Assert.Equal(optionCount, optionsArray!.Count);

        // Verify data integrity - each option should have unique IDs
        var sourceOptionIds = optionsArray.Select(o => o["sourceOptionId"].ToString()).ToList();
        var destinationOptionIds = optionsArray.Select(o => o["destinationOptionId"].ToString()).ToList();

        Assert.Equal(optionCount, sourceOptionIds.Distinct().Count()); // No duplicates
        Assert.Equal(optionCount, destinationOptionIds.Distinct().Count()); // No duplicates

        // Performance assertion (should complete reasonably quickly)
        Assert.True(stopwatch.ElapsedMilliseconds < maxDelayMs * 2, 
            $"Concurrent processing took too long: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ConcurrentOptionsMapping_WithExistingData_PreservesExistingMappings()
    {
        // Arrange - Start with existing options mapping
        var migrationId = "preserve_test";
        var sourceProductId = "source_product_preserve";
        
        var existingMappingJson = @"{
            ""options"": [
                {
                    ""sourceOptionId"": ""existing_option_1"",
                    ""destinationOptionId"": ""existing_dest_1"",
                    ""optionValues"": [
                        {
                            ""sourceId"": ""existing_value_1"",
                            ""destinationId"": ""existing_dest_value_1""
                        }
                    ]
                }
            ]
        }";

        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = "dest_product_preserve",
            OptionsMappingData = existingMappingJson
        };
        
        var key = $"{migrationId}_products_{sourceProductId}";
        _inMemoryStorage[key] = productMapping;

        // Add new options concurrently
        var newOptionsCount = 5;
        var tasks = new List<Task>();

        for (int i = 1; i <= newOptionsCount; i++)
        {
            var optionIndex = i;
            var task = Task.Run(async () =>
            {
                var sourceOption = new Dictionary<string, object>
                {
                    ["id"] = $"new_option_{optionIndex}",
                    ["option_values"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["id"] = $"new_value_{optionIndex}",
                            ["label"] = $"New Value {optionIndex}"
                        }
                    }
                };

                var createdOption = new Dictionary<string, object>
                {
                    ["id"] = 7000 + optionIndex,
                    ["option_values"] = JsonSerializer.Deserialize<JsonElement>($@"[
                        {{ ""id"": {8000 + optionIndex}, ""label"": ""New Value {optionIndex}"" }}
                    ]")
                };

                await _service.StoreHierarchicalOptionMappingAsync(
                    sourceOption, createdOption, $"new_option_{optionIndex}", $"{7000 + optionIndex}",
                    sourceProductId, migrationId);
            });
            
            tasks.Add(task);
        }

        // Act
        await Task.WhenAll(tasks);

        // Assert
        var finalMapping = _inMemoryStorage[key];
        var optionMappingsData = JsonSerializer.Deserialize<Dictionary<string, object>>(finalMapping.OptionsMappingData!);
        var optionsArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
            ((JsonElement)optionMappingsData!["options"]).GetRawText());

        // Should have existing + new options
        Assert.Equal(1 + newOptionsCount, optionsArray!.Count);

        // Verify existing option is preserved
        var existingOption = optionsArray.FirstOrDefault(o => 
            o["sourceOptionId"].ToString() == "existing_option_1");
        Assert.NotNull(existingOption);
        Assert.Equal("existing_dest_1", existingOption["destinationOptionId"].ToString());

        // Verify new options are added
        for (int i = 1; i <= newOptionsCount; i++)
        {
            var newOption = optionsArray.FirstOrDefault(o => 
                o["sourceOptionId"].ToString() == $"new_option_{i}");
            Assert.NotNull(newOption);
            Assert.Equal($"{7000 + i}", newOption["destinationOptionId"].ToString());
        }
    }
}