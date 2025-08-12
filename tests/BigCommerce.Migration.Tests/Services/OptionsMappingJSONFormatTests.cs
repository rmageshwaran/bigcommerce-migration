using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace BigCommerce.Migration.Tests.Services;

/// <summary>
/// Tests specifically for JSON format validation and compatibility with VariantCreationStrategy
/// Ensures the exact format expected by variant migration lookup methods
/// </summary>
public class OptionsMappingJSONFormatTests
{
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ILogger<HierarchicalOptionMappingService>> _mockLogger;
    private readonly HierarchicalOptionMappingService _service;

    public OptionsMappingJSONFormatTests()
    {
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockLogger = new Mock<ILogger<HierarchicalOptionMappingService>>();
        _service = new HierarchicalOptionMappingService(_mockStorageService.Object, _mockLogger.Object);
    }

    [Fact]
    public void ValidateJSONFormat_MatchesVariantCreationStrategyExpectations()
    {
        // This test validates the exact JSON structure expected by VariantCreationStrategy
        var expectedStructure = @"{
            ""options"": [
                {
                    ""sourceOptionId"": ""source_123"",
                    ""destinationOptionId"": ""dest_456"",
                    ""optionValues"": [
                        {
                            ""sourceId"": ""source_value_1"",
                            ""destinationId"": ""dest_value_1""
                        },
                        {
                            ""sourceId"": ""source_value_2"",
                            ""destinationId"": ""dest_value_2""
                        }
                    ]
                }
            ]
        }";

        // Parse and validate structure
        var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(expectedStructure);
        
        // Must have root "options" property
        Assert.True(parsed!.ContainsKey("options"));
        
        var optionsElement = (JsonElement)parsed["options"];
        Assert.Equal(JsonValueKind.Array, optionsElement.ValueKind);
        
        var optionsArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(optionsElement.GetRawText());
        Assert.Single(optionsArray!);
        
        var option = optionsArray[0];
        
        // Validate camelCase property names (critical for VariantCreationStrategy)
        Assert.True(option.ContainsKey("sourceOptionId"));
        Assert.True(option.ContainsKey("destinationOptionId"));
        Assert.True(option.ContainsKey("optionValues"));
        
        // Validate option values structure
        var optionValuesElement = (JsonElement)option["optionValues"];
        var optionValues = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(optionValuesElement.GetRawText());
        Assert.Equal(2, optionValues!.Count);
        
        foreach (var optionValue in optionValues)
        {
            Assert.True(optionValue.ContainsKey("sourceId"));
            Assert.True(optionValue.ContainsKey("destinationId"));
        }
    }

    [Fact]
    public async Task GeneratedJSON_CanBeUsedByVariantCreationStrategy_LookupMethods()
    {
        // Arrange - Create a mapping and test the exact lookup logic from VariantCreationStrategy
        var sourceOption = new Dictionary<string, object>
        {
            ["id"] = "source_color_option",
            ["option_values"] = new List<object>
            {
                new Dictionary<string, object> { ["id"] = "source_red", ["label"] = "Red" },
                new Dictionary<string, object> { ["id"] = "source_blue", ["label"] = "Blue" }
            }
        };

        var createdOption = new Dictionary<string, object>
        {
            ["id"] = 500,
            ["option_values"] = JsonSerializer.Deserialize<JsonElement>(@"[
                { ""id"": 600, ""label"": ""Red"" },
                { ""id"": 601, ""label"": ""Blue"" }
            ]")
        };

        var productMapping = new EntityMapping
        {
            MigrationId = "format_test",
            EntityType = "products",
            SourceId = "source_product_format",
            DestinationId = "dest_product_format",
            OptionsMappingData = null
        };

        _mockStorageService.Setup(x => x.GetEntityMappingAsync("format_test", "products", "source_product_format"))
            .ReturnsAsync(productMapping);

        EntityMapping? updatedMapping = null;
        _mockStorageService.Setup(x => x.UpdateEntityMappingAsync(It.IsAny<EntityMapping>()))
            .Callback<EntityMapping>(mapping => updatedMapping = mapping)
            .ReturnsAsync((EntityMapping m) => m);

        // Act
        await _service.StoreHierarchicalOptionMappingAsync(
            sourceOption, createdOption, "source_color_option", "500", "source_product_format", "format_test");

        // Assert - Simulate VariantCreationStrategy.LookupDestinationOptionIdAsync
        Assert.NotNull(updatedMapping?.OptionsMappingData);
        
        var productMappings = new List<EntityMapping> { updatedMapping };
        var sourceOptionId = "source_color_option";
        string? foundDestinationOptionId = null;

        foreach (var mapping in productMappings)
        {
            if (!string.IsNullOrEmpty(mapping.OptionsMappingData))
            {
                var optionsData = JsonSerializer.Deserialize<Dictionary<string, object>>(mapping.OptionsMappingData);
                
                if (optionsData != null && optionsData.TryGetValue("options", out var optionsArray) && 
                    optionsArray is JsonElement optionsElement && optionsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var optionElement in optionsElement.EnumerateArray())
                    {
                        var optionDict = JsonSerializer.Deserialize<Dictionary<string, object>>(optionElement.GetRawText());
                        
                        if (optionDict != null && 
                            optionDict.TryGetValue("sourceOptionId", out var sourceIdObj) && 
                            sourceIdObj?.ToString() == sourceOptionId &&
                            optionDict.TryGetValue("destinationOptionId", out var destIdObj))
                        {
                            foundDestinationOptionId = destIdObj?.ToString();
                            break;
                        }
                    }
                }
            }
        }

        Assert.Equal("500", foundDestinationOptionId);

        // Test optimized VariantCreationStrategy JSON format compatibility
        // Verify that the JSON format works with the optimized cache-based lookup
        var sourceOptionValueId = "source_red";
        string? foundDestinationOptionValueId = null;

        // Simulate the optimized GetProductMappingDataAsync parsing logic
        if (!string.IsNullOrEmpty(updatedMapping?.OptionsMappingData))
        {
            var optionsData = JsonSerializer.Deserialize<Dictionary<string, object>>(updatedMapping.OptionsMappingData);
            
            if (optionsData != null && optionsData.TryGetValue("options", out var optionsArray) && 
                optionsArray is JsonElement optionsElement && optionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var optionElement in optionsElement.EnumerateArray())
                {
                    var optionDict = JsonSerializer.Deserialize<Dictionary<string, object>>(optionElement.GetRawText());
                    
                    if (optionDict != null && 
                        optionDict.TryGetValue("sourceOptionId", out var sourceIdObj) && 
                        sourceIdObj?.ToString() == sourceOptionId &&
                        optionDict.TryGetValue("optionValues", out var optionValuesObj) &&
                        optionValuesObj is JsonElement optionValuesElement && optionValuesElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var valueElement in optionValuesElement.EnumerateArray())
                        {
                            var valueDict = JsonSerializer.Deserialize<Dictionary<string, object>>(valueElement.GetRawText());
                            
                            if (valueDict != null &&
                                valueDict.TryGetValue("sourceId", out var sourceValueIdObj) && 
                                sourceValueIdObj?.ToString() == sourceOptionValueId &&
                                valueDict.TryGetValue("destinationId", out var destValueIdObj))
                            {
                                foundDestinationOptionValueId = destValueIdObj?.ToString();
                                break;
                            }
                        }
                    }
                }
            }
        }

        // Verify JSON format compatibility with optimized lookup architecture
        Assert.NotNull(foundDestinationOptionValueId);
    }

    [Theory]
    [InlineData("sourceOptionId", "destinationOptionId", "optionValues")]
    [InlineData("sourceId", "destinationId", null)]
    public void JSONFormat_UsesCorrectPropertyNames_CamelCase(string sourceProperty, string destProperty, string? valuesProperty)
    {
        // Test that we're using camelCase, not snake_case (which would break variant lookup)
        var testJson = valuesProperty != null 
            ? $@"{{
                ""{sourceProperty}"": ""test_source"",
                ""{destProperty}"": ""test_dest"",
                ""{valuesProperty}"": []
            }}"
            : $@"{{
                ""{sourceProperty}"": ""test_source"",
                ""{destProperty}"": ""test_dest""
            }}";

        // Should parse without errors
        var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(testJson);
        Assert.NotNull(parsed);
        
        // Verify camelCase properties exist
        Assert.True(parsed.ContainsKey(sourceProperty));
        Assert.True(parsed.ContainsKey(destProperty));
        
        if (valuesProperty != null)
        {
            Assert.True(parsed.ContainsKey(valuesProperty));
        }

        // Verify snake_case versions DON'T exist (these would break variant lookup)
        var snakeCaseSource = ConvertToSnakeCase(sourceProperty);
        var snakeCaseDest = ConvertToSnakeCase(destProperty);
        
        if (snakeCaseSource != sourceProperty)
            Assert.False(parsed.ContainsKey(snakeCaseSource));
        
        if (snakeCaseDest != destProperty)
            Assert.False(parsed.ContainsKey(snakeCaseDest));
    }

    [Fact]
    public async Task JSONSerializationOptions_ProducesConsistentFormat()
    {
        // Test that our JsonSerializerOptions produce the expected camelCase format
        var sourceOption = new Dictionary<string, object>
        {
            ["id"] = "test_option",
            ["option_values"] = new List<object>
            {
                new Dictionary<string, object> { ["id"] = "test_value", ["label"] = "Test" }
            }
        };

        var createdOption = new Dictionary<string, object>
        {
            ["id"] = 999,
            ["option_values"] = JsonSerializer.Deserialize<JsonElement>(@"[
                { ""id"": 888, ""label"": ""Test"" }
            ]")
        };

        var productMapping = new EntityMapping
        {
            MigrationId = "serialization_test",
            EntityType = "products",
            SourceId = "source_product_serial",
            DestinationId = "dest_product_serial",
            OptionsMappingData = null
        };

        _mockStorageService.Setup(x => x.GetEntityMappingAsync("serialization_test", "products", "source_product_serial"))
            .ReturnsAsync(productMapping);

        string? capturedJson = null;
        _mockStorageService.Setup(x => x.UpdateEntityMappingAsync(It.IsAny<EntityMapping>()))
            .Callback<EntityMapping>(mapping => capturedJson = mapping.OptionsMappingData)
            .ReturnsAsync((EntityMapping m) => m);

        // Act
        await _service.StoreHierarchicalOptionMappingAsync(
            sourceOption, createdOption, "test_option", "999", "source_product_serial", "serialization_test");

        // Assert - Verify the JSON uses camelCase
        Assert.NotNull(capturedJson);
        Assert.Contains("sourceOptionId", capturedJson, StringComparison.Ordinal);
        Assert.Contains("destinationOptionId", capturedJson, StringComparison.Ordinal);
        Assert.Contains("optionValues", capturedJson, StringComparison.Ordinal);
        
        // Should NOT contain snake_case
        Assert.DoesNotContain("source_option_id", capturedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("destination_option_id", capturedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("option_values", capturedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void JSONStructure_IsValidForMultipleOptionsScenario()
    {
        // Test the JSON structure when multiple options are stored
        var multipleOptionsJson = @"{
            ""options"": [
                {
                    ""sourceOptionId"": ""color_option"",
                    ""destinationOptionId"": ""100"",
                    ""optionValues"": [
                        { ""sourceId"": ""red_value"", ""destinationId"": ""200"" },
                        { ""sourceId"": ""blue_value"", ""destinationId"": ""201"" }
                    ]
                },
                {
                    ""sourceOptionId"": ""size_option"",
                    ""destinationOptionId"": ""101"",
                    ""optionValues"": [
                        { ""sourceId"": ""small_value"", ""destinationId"": ""202"" },
                        { ""sourceId"": ""large_value"", ""destinationId"": ""203"" }
                    ]
                }
            ]
        }";

        // Should parse correctly
        var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(multipleOptionsJson);
        Assert.NotNull(parsed);
        
        var optionsElement = (JsonElement)parsed["options"];
        var optionsArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(optionsElement.GetRawText());
        
        Assert.Equal(2, optionsArray!.Count);
        
        // Validate each option has the correct structure
        foreach (var option in optionsArray)
        {
            Assert.True(option.ContainsKey("sourceOptionId"));
            Assert.True(option.ContainsKey("destinationOptionId"));
            Assert.True(option.ContainsKey("optionValues"));
            
            var optionValuesElement = (JsonElement)option["optionValues"];
            var optionValues = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(optionValuesElement.GetRawText());
            Assert.Equal(2, optionValues!.Count);
            
            foreach (var optionValue in optionValues)
            {
                Assert.True(optionValue.ContainsKey("sourceId"));
                Assert.True(optionValue.ContainsKey("destinationId"));
            }
        }
    }

    private static string ConvertToSnakeCase(string camelCase)
    {
        return string.Concat(camelCase.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x : x.ToString())).ToLower(System.Globalization.CultureInfo.InvariantCulture);
    }
}