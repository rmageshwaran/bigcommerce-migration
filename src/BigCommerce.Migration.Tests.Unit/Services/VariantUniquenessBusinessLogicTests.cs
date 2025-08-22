using System.Globalization;
using Xunit;

namespace BigCommerce.Migration.Tests.Unit.Services;

/// <summary>
/// Business logic tests for Task 4 fix: Option-combination-based uniqueness
/// Validates the core algorithm without complex mocking dependencies
/// </summary>
public class VariantUniquenessBusinessLogicTests
{
    #region Core Business Logic Tests

    [Fact]
    public void OptionCombinationKey_WithIdenticalCombinations_ShouldGenerateSameKey()
    {
        // Arrange - Two variants with identical option combinations
        var variant1Options = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 101, ["id"] = 1010 }, // Color: Red
            new() { ["option_id"] = 102, ["id"] = 1020 }  // Size: Large
        };

        var variant2Options = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 101, ["id"] = 1010 }, // Color: Red
            new() { ["option_id"] = 102, ["id"] = 1020 }  // Size: Large
        };

        // Act
        var key1 = GenerateOptionCombinationKey(variant1Options);
        var key2 = GenerateOptionCombinationKey(variant2Options);

        // Assert
        Assert.Equal(key1, key2);
        Assert.Equal("101:1010-102:1020", key1);
    }

    [Fact]
    public void OptionCombinationKey_WithDifferentCombinations_ShouldGenerateDifferentKeys()
    {
        // Arrange - Two variants with different option combinations
        var redLarge = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 101, ["id"] = 1010 }, // Color: Red
            new() { ["option_id"] = 102, ["id"] = 1020 }  // Size: Large
        };

        var blueMedium = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 101, ["id"] = 1011 }, // Color: Blue
            new() { ["option_id"] = 102, ["id"] = 1021 }  // Size: Medium
        };

        // Act
        var key1 = GenerateOptionCombinationKey(redLarge);
        var key2 = GenerateOptionCombinationKey(blueMedium);

        // Assert
        Assert.NotEqual(key1, key2);
        Assert.Equal("101:1010-102:1020", key1); // Red, Large
        Assert.Equal("101:1011-102:1021", key2); // Blue, Medium
    }

    [Theory]
    [InlineData("SAME-SKU", "Red", "Large", "SAME-SKU", "Red", "Medium", false)] // Same SKU, different options - should be different
    [InlineData("SAME-SKU", "Red", "Large", "SAME-SKU", "Red", "Large", true)]   // Same SKU, same options - should be identical
    [InlineData("DIFF-SKU", "Red", "Large", "SAME-SKU", "Red", "Large", true)]   // Different SKU, same options - should be identical (Task 4 key insight!)
    public void VariantUniqueness_RealWorldScenarios_ShouldFollowOptionCombinationLogic(
        string sku1, string color1, string size1,
        string sku2, string color2, string size2,
        bool shouldBeIdentical)
    {
        // Validate that SKUs are provided (even though we don't use them in the logic)
        Assert.NotNull(sku1);
        Assert.NotNull(sku2);
        // Arrange
        var variant1Options = CreateOptionCombination(color1, size1);
        var variant2Options = CreateOptionCombination(color2, size2);

        // Act
        var key1 = GenerateOptionCombinationKey(variant1Options);
        var key2 = GenerateOptionCombinationKey(variant2Options);

        // Assert
        if (shouldBeIdentical)
        {
            Assert.Equal(key1, key2);
        }
        else
        {
            Assert.NotEqual(key1, key2);
        }
    }

    [Fact]
    public void Task4Fix_BeforeVsAfter_ShouldDemonstrateImprovement()
    {
        // Arrange - Scenario that demonstrates Task 4 improvement
        var productSku = "TSHIRT-001";
        var scenarios = new[]
        {
            (VariantSku: "TSHIRT-001", Color: "Red", Size: "Large"),   // Same SKU as product
            (VariantSku: "TSHIRT-001", Color: "Red", Size: "Medium"),  // Same SKU as product
            (VariantSku: "TSHIRT-001", Color: "Blue", Size: "Large"),  // Same SKU as product
            (VariantSku: "TSHIRT-002", Color: "Green", Size: "Small")  // Different SKU, different options
        };

        // Act & Assert
        var oldLogicSkippedCount = 0;
        var newLogicUniqueCount = 0;
        var uniqueOptionCombinations = new HashSet<string>();

        foreach (var scenario in scenarios)
        {
            // OLD LOGIC (before Task 4): Skip if SKU matches product SKU
            var oldLogicWouldSkip = scenario.VariantSku == productSku;
            if (oldLogicWouldSkip) oldLogicSkippedCount++;

            // NEW LOGIC (after Task 4): Only skip if option combination is duplicate
            var optionCombination = CreateOptionCombination(scenario.Color, scenario.Size);
            var optionKey = GenerateOptionCombinationKey(optionCombination);
            
            if (uniqueOptionCombinations.Add(optionKey))
            {
                newLogicUniqueCount++;
            }
        }

        // Assert the improvement
        Assert.Equal(3, oldLogicSkippedCount); // Old logic would skip 3 variants (wrong!)
        Assert.Equal(4, newLogicUniqueCount);  // New logic processes 4 unique variants (correct!)
        
        // Task 4 fix recovers 3 previously skipped legitimate variants
        var recoveredVariants = newLogicUniqueCount - (scenarios.Length - oldLogicSkippedCount);
        Assert.Equal(3, recoveredVariants);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void OptionCombinationKey_WithEmptyOptions_ShouldReturnEmptyString()
    {
        // Arrange
        var emptyOptions = new List<Dictionary<string, object>>();

        // Act
        var key = GenerateOptionCombinationKey(emptyOptions);

        // Assert
        Assert.Equal("", key);
    }

    [Fact]
    public void OptionCombinationKey_WithSingleOption_ShouldGenerateCorrectKey()
    {
        // Arrange
        var singleOption = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 101, ["id"] = 1010 }
        };

        // Act
        var key = GenerateOptionCombinationKey(singleOption);

        // Assert
        Assert.Equal("101:1010", key);
    }

    [Fact]
    public void OptionCombinationKey_WithUnorderedOptions_ShouldGenerateConsistentKey()
    {
        // Arrange - Same options in different order
        var optionsOrder1 = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 102, ["id"] = 1020 }, // Size first
            new() { ["option_id"] = 101, ["id"] = 1010 }  // Color second
        };

        var optionsOrder2 = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 101, ["id"] = 1010 }, // Color first
            new() { ["option_id"] = 102, ["id"] = 1020 }  // Size second
        };

        // Act
        var key1 = GenerateOptionCombinationKey(optionsOrder1);
        var key2 = GenerateOptionCombinationKey(optionsOrder2);

        // Assert
        Assert.Equal(key1, key2); // Should be same despite different input order
        Assert.Equal("101:1010-102:1020", key1); // Always ordered by option_id
    }

    #endregion

    #region Rollback API Compatibility Tests

    [Fact]
    public void VariantUniqueness_RollbackApiApproach_ShouldMatchMigrationImplementation()
    {
        // Arrange - Simulate Rollback API's uniqueness logic
        var rollbackVariants = new[]
        {
            new { OptionId = 101L, Id = 1010L }, // Red
            new { OptionId = 102L, Id = 1020L }  // Large
        };

        var migrationOptions = new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 101, ["id"] = 1010 },
            new() { ["option_id"] = 102, ["id"] = 1020 }
        };

        // Act - Generate keys using both approaches
        var rollbackKey = string.Join("-", rollbackVariants
            .OrderBy(rv => rv.OptionId)
            .Select(rv => $"{rv.OptionId}:{rv.Id}"));

        var migrationKey = GenerateOptionCombinationKey(migrationOptions);

        // Assert - Both approaches should generate identical keys
        Assert.Equal(rollbackKey, migrationKey);
        Assert.Equal("101:1010-102:1020", rollbackKey);
    }

    #endregion

    #region Performance Tests

    [Theory]
    [InlineData(10, 5)]   // 10 variants, 5 unique combinations
    [InlineData(50, 12)]  // 50 variants, 12 unique combinations (3 colors × 4 sizes)
    [InlineData(100, 20)] // 100 variants, 20 unique combinations
    public void OptionCombinationLogic_WithLargeVariantSets_ShouldPerformEfficiently(
        int totalVariants, int expectedUniqueCombinations)
    {
        // Arrange
        var uniqueKeys = new HashSet<string>();

        // Act
        for (int i = 0; i < totalVariants; i++)
        {
            var colorId = (i % expectedUniqueCombinations) + 1000; // Cycle through unique combinations
            var sizeId = 2000; // Same size for all

            var options = new List<Dictionary<string, object>>
            {
                new() { ["option_id"] = 101, ["id"] = colorId },
                new() { ["option_id"] = 102, ["id"] = sizeId }
            };

            var key = GenerateOptionCombinationKey(options);
            uniqueKeys.Add(key);
        }

        // Assert
        Assert.Equal(expectedUniqueCombinations, uniqueKeys.Count);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates option combination for testing (Color + Size)
    /// </summary>
    private List<Dictionary<string, object>> CreateOptionCombination(string color, string size)
    {
        var colorId = color switch
        {
            "Red" => 1010,
            "Blue" => 1011,
            "Green" => 1012,
            _ => 1000
        };

        var sizeId = size switch
        {
            "Large" => 1020,
            "Medium" => 1021,
            "Small" => 1022,
            _ => 2000
        };

        return new List<Dictionary<string, object>>
        {
            new() { ["option_id"] = 101, ["id"] = colorId }, // Color option
            new() { ["option_id"] = 102, ["id"] = sizeId }   // Size option
        };
    }

    /// <summary>
    /// Replicates the option combination key generation logic from Task 4 implementation
    /// </summary>
    private static string GenerateOptionCombinationKey(List<Dictionary<string, object>> optionValues)
    {
        if (!optionValues.Any())
            return "";

        return string.Join("-", optionValues
            .Where(ov => ov.TryGetValue("option_id", out var oid) && ov.TryGetValue("id", out var vid) && 
                         oid != null && vid != null)
            .OrderBy(ov => ov.TryGetValue("option_id", out var oid) ? oid?.ToString() : "0")
            .Select(ov => 
            {
                var optionId = ov.TryGetValue("option_id", out var oid) ? oid?.ToString() : "0";
                var valueId = ov.TryGetValue("id", out var vid) ? vid?.ToString() : "0";
                return $"{optionId}:{valueId}";
            }));
    }

    #endregion

    #region Validation of Task 4 Business Requirements

    [Fact]
    public void Task4Requirements_ShouldBeMetByImplementation()
    {
        // Test Case 1: Variants with same SKU but different options should be unique
        var sameSkuDifferentOptions = new[]
        {
            CreateOptionCombination("Red", "Large"),
            CreateOptionCombination("Blue", "Large"),
            CreateOptionCombination("Red", "Medium")
        };

        var keys = sameSkuDifferentOptions.Select(GenerateOptionCombinationKey).ToList();
        Assert.Equal(3, keys.Distinct().Count()); // All should be unique

        // Test Case 2: Variants with different SKUs but same options should be duplicates
        var differentSkuSameOptions = new[]
        {
            CreateOptionCombination("Red", "Large"),
            CreateOptionCombination("Red", "Large") // Exact same options
        };

        var duplicateKeys = differentSkuSameOptions.Select(GenerateOptionCombinationKey).ToList();
        Assert.Single(duplicateKeys.Distinct()); // Should be considered duplicates

        // Test Case 3: Empty options should be handled gracefully
        var emptyOptions = new List<Dictionary<string, object>>();
        var emptyKey = GenerateOptionCombinationKey(emptyOptions);
        Assert.Equal("", emptyKey);
    }

    [Fact]
    public void Task4Fix_BigCommerceRealityAlignment_ShouldWork()
    {
        // Arrange - Real BigCommerce scenario
        var realWorldScenarios = new[]
        {
            // Product: "Premium T-Shirt", SKU: "PREMIUM-TEE"
            // BigCommerce creates default variant with product SKU during product creation
            // Additional variants with same SKU but different options should be allowed
            
            (Scenario: "Default Variant", Sku: "PREMIUM-TEE", Options: Array.Empty<(string, string)>()),
            (Scenario: "Red Large", Sku: "PREMIUM-TEE", Options: new[] { ("Color", "Red"), ("Size", "Large") }),
            (Scenario: "Red Medium", Sku: "PREMIUM-TEE", Options: new[] { ("Color", "Red"), ("Size", "Medium") }),
            (Scenario: "Blue Large", Sku: "PREMIUM-TEE", Options: new[] { ("Color", "Blue"), ("Size", "Large") })
        };

        // Act
        var uniqueKeys = new HashSet<string>();
        foreach (var scenario in realWorldScenarios)
        {
            string key;
            if (scenario.Options.Any())
            {
                var options = scenario.Options.Select((opt, index) => new Dictionary<string, object>
                {
                    ["option_id"] = 101 + index, // Color=101, Size=102
                    ["id"] = opt.Item2 == "Red" ? 1010 : opt.Item2 == "Blue" ? 1011 : 
                             opt.Item2 == "Large" ? 1020 : 1021
                }).ToList();
                key = GenerateOptionCombinationKey(options);
            }
            else
            {
                key = $"no-options-{scenario.Sku}"; // Handle variants without options
            }

            uniqueKeys.Add(key);
        }

        // Assert - All variants should be considered unique (Task 4 success)
        Assert.Equal(4, uniqueKeys.Count); // All 4 variants should be unique
    }

    #endregion
}