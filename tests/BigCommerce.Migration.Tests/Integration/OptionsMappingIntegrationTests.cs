using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Orchestration.Services.EntityCreation;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// Integration tests for end-to-end options mapping flow
/// Validates the complete pipeline from options creation through variant migration lookup
/// Tests the critical fixes that ensure variant migration can find proper option mappings
/// </summary>
public class OptionsMappingIntegrationTests
{
    private readonly Mock<IApiRequestHandler> _mockApiRequestHandler;
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<ILogger<OptionsCreationStrategy>> _mockOptionsLogger;
    private readonly Mock<ILogger<HierarchicalOptionMappingService>> _mockMappingLogger;
    private readonly Mock<ILogger<VariantCreationStrategy>> _mockVariantLogger;
    private readonly HierarchicalOptionMappingService _hierarchicalMappingService;
    private EntityMapping? _capturedMapping;

    public OptionsMappingIntegrationTests()
    {
        _mockApiRequestHandler = new Mock<IApiRequestHandler>();
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockOptionsLogger = new Mock<ILogger<OptionsCreationStrategy>>();
        _mockMappingLogger = new Mock<ILogger<HierarchicalOptionMappingService>>();
        _mockVariantLogger = new Mock<ILogger<VariantCreationStrategy>>();
        
        _hierarchicalMappingService = new HierarchicalOptionMappingService(
            _mockStorageService.Object, _mockMappingLogger.Object);
            
        // Setup mock to capture updated mappings
        _mockStorageService.Setup(x => x.UpdateEntityMappingAsync(It.IsAny<EntityMapping>()))
            .Callback<EntityMapping>(mapping => _capturedMapping = mapping)
            .ReturnsAsync((EntityMapping mapping) => mapping);
    }

    [Fact]
    public async Task EndToEndOptionsMapping_CreateOptionsThenLookupForVariants_WorksCorrectly()
    {
        // Arrange - Simulate the complete flow from Phase 2 (options) to Phase 3 (variants)
        var migrationId = "test_migration_123";
        var sourceProductId = "source_product_456";
        var destinationProductId = "dest_product_789";

        // Step 1: Setup product mapping (Phase 1 dependency)
        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = destinationProductId,
            OptionsMappingData = null
        };

        _mockStorageService.Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync(productMapping);

        // Step 2: Simulate creating multiple options with your provided BigCommerce response format
        var sourceOptions = new[]
        {
            new Dictionary<string, object>
            {
                ["id"] = "source_option_1",
                ["name"] = "Color",
                ["option_values"] = new List<object>
                {
                    new Dictionary<string, object> { ["id"] = "source_value_1", ["label"] = "Red" },
                    new Dictionary<string, object> { ["id"] = "source_value_2", ["label"] = "Blue" }
                }
            },
            new Dictionary<string, object>
            {
                ["id"] = "source_option_2", 
                ["name"] = "Size",
                ["option_values"] = new List<object>
                {
                    new Dictionary<string, object> { ["id"] = "source_value_3", ["label"] = "Small" },
                    new Dictionary<string, object> { ["id"] = "source_value_4", ["label"] = "Large" }
                }
            }
        };

        var bigCommerceResponses = new[]
        {
            // Response for Color option (your provided format)
            @"{
                ""id"": 220,
                ""product_id"": 789,
                ""name"": ""Color"",
                ""option_values"": [
                    { ""id"": 174, ""label"": ""Red"" },
                    { ""id"": 175, ""label"": ""Blue"" }
                ]
            }",
            // Response for Size option 
            @"{
                ""id"": 221,
                ""product_id"": 789,
                ""name"": ""Size"",
                ""option_values"": [
                    { ""id"": 176, ""label"": ""Small"" },
                    { ""id"": 177, ""label"": ""Large"" }
                ]
            }"
        };

        var createdOptions = bigCommerceResponses.Select(json => 
            JsonSerializer.Deserialize<Dictionary<string, object>>(json)!).ToArray();

        // Step 3: Store option mappings (Phase 2 processing)
        for (int i = 0; i < sourceOptions.Length; i++)
        {
            var sourceOptionId = sourceOptions[i]["id"].ToString()!;
            var destinationOptionId = createdOptions[i]["id"].ToString()!;

            await _hierarchicalMappingService.StoreHierarchicalOptionMappingAsync(
                sourceOptions[i], createdOptions[i], sourceOptionId, destinationOptionId, 
                sourceProductId, migrationId);
        }

        // Step 4: Simulate variant creation looking up the mappings (Phase 3)
        var storedProductMapping = GetStoredProductMapping();
        
        // Verify the mappings can be found by variant creation strategy
        var optionMappingsData = JsonSerializer.Deserialize<Dictionary<string, object>>(storedProductMapping.OptionsMappingData!);
        var optionsArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
            ((JsonElement)optionMappingsData["options"]).GetRawText());

        // Assert - Validate complete end-to-end flow
        Assert.NotNull(optionsArray);
        Assert.Equal(2, optionsArray.Count); // Both options stored

        // Validate Color option mapping
        var colorOption = optionsArray.First(o => o["sourceOptionId"].ToString() == "source_option_1");
        Assert.Equal("220", colorOption["destinationOptionId"].ToString());
        
        var colorOptionValues = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
            ((JsonElement)colorOption["optionValues"]).GetRawText());
        Assert.Equal(2, colorOptionValues!.Count);
        Assert.Contains(colorOptionValues, ov => ov["sourceId"].ToString() == "source_value_1" && ov["destinationId"].ToString() == "174");
        Assert.Contains(colorOptionValues, ov => ov["sourceId"].ToString() == "source_value_2" && ov["destinationId"].ToString() == "175");

        // Validate Size option mapping
        var sizeOption = optionsArray.First(o => o["sourceOptionId"].ToString() == "source_option_2");
        Assert.Equal("221", sizeOption["destinationOptionId"].ToString());
        
        var sizeOptionValues = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
            ((JsonElement)sizeOption["optionValues"]).GetRawText());
        Assert.Equal(2, sizeOptionValues!.Count);
        Assert.Contains(sizeOptionValues, ov => ov["sourceId"].ToString() == "source_value_3" && ov["destinationId"].ToString() == "176");
        Assert.Contains(sizeOptionValues, ov => ov["sourceId"].ToString() == "source_value_4" && ov["destinationId"].ToString() == "177");

        // Step 5: Test variant creation can successfully lookup mappings
        await ValidateVariantMappingLookup(storedProductMapping, migrationId);
    }

    [Fact]
    public async Task OptionsMapping_ConcurrentOptionsCreation_HandlesCorrectly()
    {
        // Arrange - Test concurrent options creation for same product (race condition scenario)
        var migrationId = "concurrent_test";
        var sourceProductId = "source_product_999";

        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products", 
            SourceId = sourceProductId,
            DestinationId = "dest_product_999",
            OptionsMappingData = null
        };

        _mockStorageService.Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync(productMapping);

        // Simulate multiple options being processed concurrently
        var concurrentTasks = new List<Task>();
        for (int i = 1; i <= 5; i++)
        {
            var optionIndex = i;
            var task = Task.Run(async () =>
            {
                var sourceOption = new Dictionary<string, object>
                {
                    ["id"] = $"source_option_{optionIndex}",
                    ["option_values"] = new List<object>
                    {
                        new Dictionary<string, object> { ["id"] = $"source_value_{optionIndex}", ["label"] = $"Value {optionIndex}" }
                    }
                };

                var createdOption = new Dictionary<string, object>
                {
                    ["id"] = 200 + optionIndex,
                    ["option_values"] = JsonSerializer.Deserialize<JsonElement>($@"[
                        {{ ""id"": {170 + optionIndex}, ""label"": ""Value {optionIndex}"" }}
                    ]")
                };

                await _hierarchicalMappingService.StoreHierarchicalOptionMappingAsync(
                    sourceOption, createdOption, $"source_option_{optionIndex}", $"{200 + optionIndex}",
                    sourceProductId, migrationId);
            });
            
            concurrentTasks.Add(task);
        }

        // Act - Wait for all concurrent operations to complete
        await Task.WhenAll(concurrentTasks);

        // Assert - All options should be stored correctly
        var finalMapping = GetStoredProductMapping();
        var optionMappingsData = JsonSerializer.Deserialize<Dictionary<string, object>>(finalMapping.OptionsMappingData!);
        var optionsArray = JsonSerializer.Deserialize<List<object>>(
            ((JsonElement)optionMappingsData["options"]).GetRawText());

        Assert.Equal(5, optionsArray!.Count);
    }

    [Fact]
    public async Task OptionsMapping_CompatibilityWithVariantCreationStrategy_ValidatesFormat()
    {
        // Arrange - Test the exact scenario that variant creation strategy expects
        var migrationId = "variant_compat_test";
        var sourceProductId = "source_product_111";

        var productMapping = new EntityMapping
        {
            MigrationId = migrationId,
            EntityType = "products",
            SourceId = sourceProductId,
            DestinationId = "dest_product_111",
            OptionsMappingData = null
        };

        _mockStorageService.Setup(x => x.GetEntityMappingAsync(migrationId, "products", sourceProductId))
            .ReturnsAsync(productMapping);

        // Create option mapping with your exact BigCommerce response format
        var sourceOption = CreateSourceOption();
        var bigCommerceResponse = CreateBigCommerceAPIResponse();

        await _hierarchicalMappingService.StoreHierarchicalOptionMappingAsync(
            sourceOption, bigCommerceResponse, "source_opt_123", "220", sourceProductId, migrationId);

        var storedMapping = GetStoredProductMapping();

        // Act & Assert - Simulate VariantCreationStrategy lookup logic
        await SimulateVariantCreationLookup(storedMapping, migrationId);
    }

    private static Dictionary<string, object> CreateSourceOption()
    {
        return new Dictionary<string, object>
        {
            ["id"] = "source_opt_123",
            ["name"] = "Color (Colors only)",
            ["option_values"] = new List<object>
            {
                new Dictionary<string, object> { ["id"] = "source_174", ["label"] = "Beige" },
                new Dictionary<string, object> { ["id"] = "source_175", ["label"] = "Grey" },
                new Dictionary<string, object> { ["id"] = "source_176", ["label"] = "Black" },
                new Dictionary<string, object> { ["id"] = "source_189", ["label"] = "Black-Walnut" }
            }
        };
    }

    private static Dictionary<string, object> CreateBigCommerceAPIResponse()
    {
        // Your exact provided BigCommerce API response
        var responseJson = @"{
            ""id"": 220,
            ""product_id"": 192,
            ""name"": ""Color (Colors only)"",
            ""display_name"": ""Color"",
            ""type"": ""swatch"",
            ""sort_order"": 0,
            ""option_values"": [
                {
                    ""id"": 174,
                    ""label"": ""Beige"",
                    ""sort_order"": 1,
                    ""value_data"": { ""colors"": [""#FAFAEB""] },
                    ""is_default"": false
                },
                {
                    ""id"": 175,
                    ""label"": ""Grey"",
                    ""sort_order"": 2,
                    ""value_data"": { ""colors"": [""#BDBDBD""] },
                    ""is_default"": false
                },
                {
                    ""id"": 176,
                    ""label"": ""Black"",
                    ""sort_order"": 3,
                    ""value_data"": { ""colors"": [""#000000""] },
                    ""is_default"": false
                },
                {
                    ""id"": 189,
                    ""label"": ""Black-Walnut"",
                    ""sort_order"": 4,
                    ""value_data"": { ""colors"": [""#e80ee8""] },
                    ""is_default"": false
                }
            ],
            ""config"": {}
        }";

        return JsonSerializer.Deserialize<Dictionary<string, object>>(responseJson)!;
    }

    private async Task SimulateVariantCreationLookup(EntityMapping productMapping, string migrationId)
    {
        // Simulate the optimized lookup logic from VariantCreationStrategy.GetProductMappingDataAsync
        // Uses single GetEntityMappingAsync call instead of GetEntityMappingsAsync for better performance
        _mockStorageService.Setup(x => x.GetEntityMappingAsync(migrationId, "products", productMapping.SourceId))
            .ReturnsAsync(productMapping);

        // Test lookup of destination option ID (compatible with optimized VariantCreationStrategy)
        var sourceOptionId = "source_opt_123";
        string? foundDestinationOptionId = null;

        // Simulate the JSON parsing logic used in GetProductMappingDataAsync
        if (!string.IsNullOrEmpty(productMapping.OptionsMappingData))
        {
            var optionsData = JsonSerializer.Deserialize<Dictionary<string, object>>(productMapping.OptionsMappingData);
            
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

        // Assert the lookup succeeded
        Assert.NotNull(foundDestinationOptionId);
        Assert.Equal("220", foundDestinationOptionId);

        // Test lookup of destination option value ID
        var sourceOptionValueId = "source_174";
        string? foundDestinationOptionValueId = null;

        // Continue using the same productMapping for option value lookup
        if (!string.IsNullOrEmpty(productMapping.OptionsMappingData))
        {
            var optionsData = JsonSerializer.Deserialize<Dictionary<string, object>>(productMapping.OptionsMappingData);
            
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

        // Assert the option value lookup succeeded
        Assert.NotNull(foundDestinationOptionValueId);
        Assert.Equal("174", foundDestinationOptionValueId);
    }

    private async Task ValidateVariantMappingLookup(EntityMapping productMapping, string migrationId)
    {
        // Test that variant creation can successfully find the mappings
        await SimulateVariantCreationLookup(productMapping, migrationId);
    }

    private EntityMapping GetStoredProductMapping()
    {
        return _capturedMapping ?? throw new InvalidOperationException("No mapping was stored");
    }
}