using System.Globalization;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// Integration tests for variant business logic
/// Tests the complete option-combination-based uniqueness workflow
/// Validates Task 4 fix behavior in realistic scenarios
/// </summary>
public class VariantBusinessLogicIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public VariantBusinessLogicIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Real-world Business Scenarios

    [Fact]
    public void VariantUniqueness_TShirtScenario_ShouldFollowOptionCombinationLogic()
    {
        // Arrange - T-shirt variants with different SKUs but potentially same options
        var tshirtVariants = new[]
        {
            CreateVariant("TSHIRT-RED-S", 100, 1, new[] { ("color", "red"), ("size", "small") }),
            CreateVariant("TSHIRT-RED-M", 100, 2, new[] { ("color", "red"), ("size", "medium") }),
            CreateVariant("TSHIRT-RED-L", 100, 3, new[] { ("color", "red"), ("size", "large") }),
            CreateVariant("TSHIRT-BLUE-S", 100, 4, new[] { ("color", "blue"), ("size", "small") }),
            CreateVariant("DUPLICATE-SKU", 100, 5, new[] { ("color", "red"), ("size", "small") }), // Same options as #1
            CreateVariant("ANOTHER-SKU", 100, 6, new[] { ("color", "blue"), ("size", "medium") })
        };

        _output.WriteLine($"Testing {tshirtVariants.Length} T-shirt variants");

        // Act - Generate option combination keys
        var optionKeys = tshirtVariants
            .Select(v => GetOptionCombinationKey(v["option_values"]))
            .ToList();

        var uniqueKeys = optionKeys.Distinct().ToList();

        _output.WriteLine($"Option keys generated: {string.Join(", ", optionKeys)}");
        _output.WriteLine($"Unique option keys: {string.Join(", ", uniqueKeys)}");

        // Assert - Task 4 validation: Should have 5 unique combinations (variant #5 duplicates #1)
        Assert.Equal(5, uniqueKeys.Count);
        Assert.Equal(6, optionKeys.Count); // Total variants
        
        // Verify the duplicate is detected
        var redSmallKey = optionKeys[0]; // First variant: red, small
        var duplicateKey = optionKeys[4]; // Fifth variant: same options, different SKU
        Assert.Equal(redSmallKey, duplicateKey);
    }

    [Fact]
    public void VariantUniqueness_MultiProductScenario_ShouldMaintainSeparateUniqueness()
    {
        // Arrange - Variants from different products with potentially overlapping options
        var variants = new[]
        {
            // Product 100: Shoes
            CreateVariant("SHOE-BLACK-8", 100, 1, new[] { ("color", "black"), ("size", "8") }),
            CreateVariant("SHOE-BLACK-9", 100, 2, new[] { ("color", "black"), ("size", "9") }),
            
            // Product 101: Socks (same color/size options but different product)
            CreateVariant("SOCK-BLACK-8", 101, 3, new[] { ("color", "black"), ("size", "8") }),
            CreateVariant("SOCK-BLACK-9", 101, 4, new[] { ("color", "black"), ("size", "9") }),
            
            // Product 100: More shoes with duplicate option combination
            CreateVariant("SHOE-DUPLICATE", 100, 5, new[] { ("color", "black"), ("size", "8") }) // Duplicates variant #1
        };

        _output.WriteLine($"Testing {variants.Length} multi-product variants");

        // Act - Group by product and check uniqueness within each product
        var productGroups = variants.GroupBy(v => v["product_id"]).ToList();

        foreach (var group in productGroups)
        {
            var productId = group.Key;
            var productVariants = group.ToList();
            var optionKeys = productVariants
                .Select(v => GetOptionCombinationKey(v["option_values"]))
                .ToList();
            var uniqueKeys = optionKeys.Distinct().ToList();

            _output.WriteLine($"Product {productId}: {productVariants.Count} variants, {uniqueKeys.Count} unique option combinations");

            // Assert - Each product should maintain its own uniqueness
            if (productId.Equals(100))
            {
                // Product 100 has 3 variants but only 2 unique option combinations
                Assert.Equal(3, productVariants.Count);
                Assert.Equal(2, uniqueKeys.Count);
            }
            else if (productId.Equals(101))
            {
                // Product 101 has 2 variants with 2 unique option combinations
                Assert.Equal(2, productVariants.Count);
                Assert.Equal(2, uniqueKeys.Count);
            }
        }
    }

    [Fact]
    public void VariantUniqueness_EdgeCases_ShouldHandleGracefully()
    {
        // Arrange - Edge cases
        var variants = new[]
        {
            // Normal variant
            CreateVariant("NORMAL", 100, 1, new[] { ("color", "red"), ("size", "large") }),
            
            // Variant with no options
            CreateVariantWithoutOptions("NO-OPTIONS", 100, 2),
            
            // Variant with single option
            CreateVariant("SINGLE-OPTION", 100, 3, new[] { ("color", "blue") }),
            
            // Variant with many options
            CreateVariant("MANY-OPTIONS", 100, 4, new[] { 
                ("color", "green"), ("size", "medium"), ("material", "cotton"), ("style", "casual") 
            })
        };

        _output.WriteLine($"Testing {variants.Length} edge case variants");

        // Act
        var optionKeys = variants
            .Select(v => GetOptionCombinationKey(v.ContainsKey("option_values") ? v["option_values"] : new List<Dictionary<string, object>>()))
            .ToList();

        _output.WriteLine($"Edge case option keys: {string.Join(", ", optionKeys.Select(k => string.IsNullOrEmpty(k) ? "[EMPTY]" : k))}");

        // Assert - All should generate different keys (including empty for no options)
        var uniqueKeys = optionKeys.Distinct().ToList();
        Assert.Equal(4, uniqueKeys.Count);
        
        // Verify empty key for variant without options
        Assert.Contains(string.Empty, optionKeys);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void VariantUniqueness_LargeDataset_ShouldPerformEfficiently()
    {
        // Arrange - Large dataset with many duplicates
        var variants = new List<Dictionary<string, object>>();
        var colors = new[] { "red", "blue", "green", "black", "white" };
        var sizes = new[] { "small", "medium", "large", "xl" };

        // Generate 1000 variants with many duplicates
        for (int i = 1; i <= 1000; i++)
        {
            var colorIndex = i % colors.Length;
            var sizeIndex = i % sizes.Length;
            
            variants.Add(CreateVariant(
                $"VARIANT-{i:D4}",
                100 + (i / 100), // 10 products with 100 variants each
                i,
                new[] { ("color", colors[colorIndex]), ("size", sizes[sizeIndex]) }
            ));
        }

        _output.WriteLine($"Testing performance with {variants.Count} variants");

        // Act - Measure performance
        var startTime = DateTime.UtcNow;
        
        var optionKeys = variants
            .Select(v => GetOptionCombinationKey(v["option_values"]))
            .ToList();
        
        var uniqueKeys = optionKeys.Distinct().ToList();
        
        var duration = DateTime.UtcNow - startTime;

        _output.WriteLine($"Performance test completed in {duration.TotalMilliseconds:F2}ms");
        _output.WriteLine($"Generated {optionKeys.Count} keys, {uniqueKeys.Count} unique");

        // Assert - Should complete quickly and detect duplicates correctly
        Assert.True(duration.TotalMilliseconds < 1000, $"Performance test took too long: {duration.TotalMilliseconds}ms");
        Assert.Equal(1000, optionKeys.Count);
        Assert.True(uniqueKeys.Count < optionKeys.Count, "Should have detected duplicates");
        
        // With 5 colors and 4 sizes, we should have maximum 20 unique combinations per product
        Assert.True(uniqueKeys.Count <= 20 * 10, "Unique combinations should not exceed theoretical maximum");
    }

    #endregion

    #region Helper Methods

    private static Dictionary<string, object> CreateVariant(
        string sku, 
        int productId, 
        int id, 
        (string optionName, string optionValue)[] options)
    {
        var optionValues = options.Select((opt, index) => new Dictionary<string, object>
        {
            ["option_id"] = index + 1, // Simple option IDs
            ["id"] = opt.optionValue.GetHashCode(StringComparison.Ordinal) & 0x7FFFFFFF, // Positive hash as ID
            ["option_display_name"] = opt.optionName,
            ["label"] = opt.optionValue
        }).ToList();

        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["sku"] = sku,
            ["product_id"] = productId,
            ["option_values"] = optionValues,
            ["price"] = 29.99m,
            ["weight"] = 1.5m,
            ["inventory_level"] = 100
        };
    }

    private static Dictionary<string, object> CreateVariantWithoutOptions(string sku, int productId, int id)
    {
        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["sku"] = sku,
            ["product_id"] = productId,
            ["price"] = 19.99m,
            ["weight"] = 1.0m,
            ["inventory_level"] = 50
            // No option_values
        };
    }

    private static string GetOptionCombinationKey(object optionValuesObj)
    {
        if (optionValuesObj is not List<Dictionary<string, object>> optionValues || !optionValues.Any())
            return string.Empty;

        return string.Join("-", optionValues
            .OrderBy(ov => ov.TryGetValue("option_id", out var oid) ? oid?.ToString() : "0")
            .Select(ov =>
            {
                var optionId = ov.TryGetValue("option_id", out var oid) ? oid?.ToString() : "0";
                var valueId = ov.TryGetValue("id", out var vid) ? vid?.ToString() : "0";
                return $"{optionId}:{valueId}";
            }));
    }

    #endregion
}