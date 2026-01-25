using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Activities.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace BigCommerce.Migration.Tests.Services;

/// <summary>
/// Unit tests for HierarchicalOptionMappingService to validate options mapping storage and JSON format consistency
/// Tests the critical fixes for variant migration dependency on proper options mapping data
/// </summary>
public class HierarchicalOptionMappingServiceTests
{
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ILogger<HierarchicalOptionMappingService>> _mockLogger;
    private readonly HierarchicalOptionMappingService _service;

    public HierarchicalOptionMappingServiceTests()
    {
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockLogger = new Mock<ILogger<HierarchicalOptionMappingService>>();
        _service = new HierarchicalOptionMappingService(_mockStorageService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task StoreHierarchicalOptionMappingAsync_WithValidBigCommerceResponse_CreatesCorrectJSONFormat()
    {
        // Arrange - Your provided BigCommerce API response payload
        var sourceOption = new Dictionary<string, object>
        {
            ["id"] = "source_123",
            ["option_values"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["id"] = "source_174",
                    ["label"] = "Beige"
                },
                new Dictionary<string, object>
                {
                    ["id"] = "source_175", 
                    ["label"] = "Grey"
                }
            }
        };

        // Simulate the BigCommerce API response as JsonElement (the problematic case we fixed)
        var bigCommerceResponseJson = @"{
            ""id"": 220,
            ""product_id"": 192,
            ""name"": ""Color (Colors only)"",
            ""option_values"": [
                {
                    ""id"": 174,
                    ""label"": ""Beige"",
                    ""sort_order"": 1,
                    ""is_default"": false
                },
                {
                    ""id"": 175,
                    ""label"": ""Grey"", 
                    ""sort_order"": 2,
                    ""is_default"": false
                }
            ]
        }";

        var createdOption = JsonSerializer.Deserialize<Dictionary<string, object>>(bigCommerceResponseJson);

        var existingProductMapping = new EntityMapping
        {
            MigrationId = "test_migration",
            EntityType = "products",
            SourceId = "source_product_123",
            DestinationId = "dest_product_192",
            OptionsMappingData = null // No existing mappings
        };

        _mockStorageService.Setup(x => x.GetEntityMappingAsync("test_migration", "products", "source_product_123"))
            .ReturnsAsync(existingProductMapping);

        // Act
        await _service.StoreHierarchicalOptionMappingAsync(
            sourceOption, createdOption!, "source_123", "220", "source_product_123", "test_migration");

        // Assert
        _mockStorageService.Verify(x => x.UpdateEntityMappingAsync(It.IsAny<EntityMapping>()), Times.Once);
        
        // Verify the JSON format is correct
        _mockStorageService.Verify(x => x.UpdateEntityMappingAsync(It.Is<EntityMapping>(mapping =>
            ValidateOptionsJSONFormat(mapping.OptionsMappingData!, "source_123", "220", 2)
        )), Times.Once);
    }

    [Fact]
    public async Task StoreHierarchicalOptionMappingAsync_WithExistingMappings_AppendsToExistingData()
    {
        // Arrange - Existing options mapping data
        var existingMappingJson = @"{
            ""options"": [
                {
                    ""sourceOptionId"": ""existing_option_1"",
                    ""destinationOptionId"": ""dest_option_1"",
                    ""optionValues"": [
                        {
                            ""sourceId"": ""existing_value_1"",
                            ""destinationId"": ""dest_value_1""
                        }
                    ]
                }
            ]
        }";

        var sourceOption = new Dictionary<string, object>
        {
            ["id"] = "source_456",
            ["option_values"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["id"] = "source_789",
                    ["label"] = "Red"
                }
            }
        };

        var createdOption = new Dictionary<string, object>
        {
            ["id"] = 999,
            ["option_values"] = JsonSerializer.Deserialize<JsonElement>(@"[
                {
                    ""id"": 888,
                    ""label"": ""Red""
                }
            ]")
        };

        var existingProductMapping = new EntityMapping
        {
            MigrationId = "test_migration",
            EntityType = "products",
            SourceId = "source_product_123",
            DestinationId = "dest_product_192",
            OptionsMappingData = existingMappingJson
        };

        _mockStorageService.Setup(x => x.GetEntityMappingAsync("test_migration", "products", "source_product_123"))
            .ReturnsAsync(existingProductMapping);

        // Act
        await _service.StoreHierarchicalOptionMappingAsync(
            sourceOption, createdOption, "source_456", "999", "source_product_123", "test_migration");

        // Assert - Verify we now have 2 options in the mapping
        _mockStorageService.Verify(x => x.UpdateEntityMappingAsync(It.Is<EntityMapping>(mapping =>
            ValidateMultipleOptionsInMapping(mapping.OptionsMappingData!, 2)
        )), Times.Once);
    }

    [Fact]
    public async Task StoreHierarchicalOptionMappingAsync_ProductMappingNotFound_LogsWarningAndReturns()
    {
        // Arrange
        _mockStorageService.Setup(x => x.GetEntityMappingAsync("test_migration", "products", "nonexistent_product"))
            .ReturnsAsync((EntityMapping?)null);

        var sourceOption = new Dictionary<string, object> { ["id"] = "test" };
        var createdOption = new Dictionary<string, object> { ["id"] = "test" };

        // Act
        await _service.StoreHierarchicalOptionMappingAsync(
            sourceOption, createdOption, "source_123", "dest_456", "nonexistent_product", "test_migration");

        // Assert
        _mockStorageService.Verify(x => x.UpdateEntityMappingAsync(It.IsAny<EntityMapping>()), Times.Never);
        
        // Verify warning was logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Product mapping not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StoreHierarchicalOptionMappingAsync_CorruptedExistingJSON_StartsWithFreshMapping()
    {
        // Arrange - Corrupted JSON in existing mapping
        var existingProductMapping = new EntityMapping
        {
            MigrationId = "test_migration",
            EntityType = "products", 
            SourceId = "source_product_123",
            DestinationId = "dest_product_192",
            OptionsMappingData = "{ invalid json }" // Corrupted JSON
        };

        var sourceOption = new Dictionary<string, object>
        {
            ["id"] = "source_123",
            ["option_values"] = new List<object>()
        };

        var createdOption = new Dictionary<string, object>
        {
            ["id"] = 456,
            ["option_values"] = JsonSerializer.Deserialize<JsonElement>("[]")
        };

        _mockStorageService.Setup(x => x.GetEntityMappingAsync("test_migration", "products", "source_product_123"))
            .ReturnsAsync(existingProductMapping);

        // Act
        await _service.StoreHierarchicalOptionMappingAsync(
            sourceOption, createdOption, "source_123", "456", "source_product_123", "test_migration");

        // Assert - Should create fresh mapping with 1 option
        _mockStorageService.Verify(x => x.UpdateEntityMappingAsync(It.Is<EntityMapping>(mapping =>
            ValidateOptionsJSONFormat(mapping.OptionsMappingData!, "source_123", "456", 0)
        )), Times.Once);

        // Verify warning was logged about corrupted JSON
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to parse existing options data")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData("List<object>")]
    [InlineData("JsonElement")]
    public async Task StoreHierarchicalOptionMappingAsync_HandlesMultipleOptionValueTypes_Successfully(string optionValueType)
    {
        // Arrange - Test both List<object> and JsonElement option_values formats
        var sourceOption = new Dictionary<string, object>
        {
            ["id"] = "source_123",
            ["option_values"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["id"] = "source_value_1",
                    ["label"] = "Test Label"
                }
            }
        };

        Dictionary<string, object> createdOption;
        
        if (optionValueType == "JsonElement")
        {
            // Simulate API response with JsonElement (the case we fixed)
            createdOption = new Dictionary<string, object>
            {
                ["id"] = 789,
                ["option_values"] = JsonSerializer.Deserialize<JsonElement>(@"[
                    {
                        ""id"": 999,
                        ""label"": ""Test Label""
                    }
                ]")
            };
        }
        else
        {
            // Standard List<object> format
            createdOption = new Dictionary<string, object>
            {
                ["id"] = 789,
                ["option_values"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = 999,
                        ["label"] = "Test Label"
                    }
                }
            };
        }

        var existingProductMapping = new EntityMapping
        {
            MigrationId = "test_migration",
            EntityType = "products",
            SourceId = "source_product_123", 
            DestinationId = "dest_product_192",
            OptionsMappingData = null
        };

        _mockStorageService.Setup(x => x.GetEntityMappingAsync("test_migration", "products", "source_product_123"))
            .ReturnsAsync(existingProductMapping);

        // Act
        await _service.StoreHierarchicalOptionMappingAsync(
            sourceOption, createdOption, "source_123", "789", "source_product_123", "test_migration");

        // Assert - Should work for both formats
        _mockStorageService.Verify(x => x.UpdateEntityMappingAsync(It.Is<EntityMapping>(mapping =>
            ValidateOptionsJSONFormat(mapping.OptionsMappingData!, "source_123", "789", 1)
        )), Times.Once);
    }

    /// <summary>
    /// Validates the JSON format matches expected structure for variant migration
    /// </summary>
    private static bool ValidateOptionsJSONFormat(string optionsMappingData, string expectedSourceId, string expectedDestId, int expectedOptionValueCount)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsMappingData);
            
            // Must have "options" array at root
            if (!parsed.TryGetValue("options", out var optionsObj) || optionsObj is not JsonElement optionsElement)
                return false;

            var options = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(optionsElement.GetRawText());
            if (options == null || options.Count == 0) return false;

            var option = options.Last(); // Check the most recently added option
            
            // Validate camelCase property names (critical for variant migration)
            if (!option.TryGetValue("sourceOptionId", out var sourceId) || sourceId?.ToString() != expectedSourceId)
                return false;
                
            if (!option.TryGetValue("destinationOptionId", out var destId) || destId?.ToString() != expectedDestId)
                return false;

            if (!option.TryGetValue("optionValues", out var optionValuesObj))
                return false;

            // Validate option values structure
            var optionValues = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
                ((JsonElement)optionValuesObj).GetRawText());
            
            return optionValues?.Count == expectedOptionValueCount;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates multiple options are properly stored in the mapping
    /// </summary>
    private static bool ValidateMultipleOptionsInMapping(string optionsMappingData, int expectedOptionCount)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsMappingData);
            
            if (!parsed.TryGetValue("options", out var optionsObj) || optionsObj is not JsonElement optionsElement)
                return false;

            var options = JsonSerializer.Deserialize<List<object>>(optionsElement.GetRawText());
            return options?.Count == expectedOptionCount;
        }
        catch
        {
            return false;
        }
    }
}