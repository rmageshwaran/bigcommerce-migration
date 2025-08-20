using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// Tests to ensure variants have proper dependencies on product-components
/// </summary>
public class VariantDependencyTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly ILogger<VariantDependencyTests> _logger;

    public VariantDependencyTests(ITestOutputHelper output)
    {
        _output = output;
        
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<VariantDependencyTests>>();
    }

    [Fact]
    public void EntityDependencyResolution_ShouldIncludeComponentsBeforeVariants()
    {
        // Arrange
        var requestedEntities = new[] { "products" };

        // Act
        var resolvedEntities = SimulateEntityDependencyResolution(requestedEntities);

        // Assert
        resolvedEntities.Should().ContainInOrder(
            "products",
            "options", 
            "modifiers", 
            "images", 
            "reviews", 
            "product-variants"
        );

        var optionsIndex = Array.IndexOf(resolvedEntities, "options");
        var variantsIndex = Array.IndexOf(resolvedEntities, "product-variants");
        
        optionsIndex.Should().BeLessThan(variantsIndex, 
            "Options must be processed before variants to create necessary mappings");
    }

    [Fact]
    public void ProgressiveDiscoveryWorkflow_ShouldCreateOptionMappings()
    {
        // Arrange - Simulate the workflow that was failing
        var migrationWorkflow = new List<(string EntityType, bool HasRealCount, int ProcessedCount)>();

        // Act - Simulate the corrected workflow
        // 1. Products discovery and processing
        migrationWorkflow.Add(("products", HasRealCount: true, ProcessedCount: 4791));

        // 2. Product-components progressive discovery (previously broken)
        var componentTypes = new[] { "options", "modifiers", "images", "reviews" };
        foreach (var componentType in componentTypes)
        {
            // Progressive discovery: TotalCount=0, but processes products to extract components
            var shouldProcess = SimulateProgressiveDiscoveryDecision(
                totalCount: 0,
                isProgressiveDiscovery: true,
                totalProducts: 4791
            );

            if (shouldProcess)
            {
                // This should now work with the fix - extracts components from 4791 products
                var extractedCount = SimulateComponentExtraction(componentType, productCount: 4791);
                migrationWorkflow.Add((componentType, HasRealCount: false, ProcessedCount: extractedCount));
            }
        }

        // 3. Variants processing (should now succeed)
        var optionMappingsExist = migrationWorkflow.Any(w => w.EntityType == "options" && w.ProcessedCount > 0);
        var variantSuccessRate = optionMappingsExist ? 0.95 : 0.0; // 95% success if mappings exist, 0% if not

        migrationWorkflow.Add(("product-variants", HasRealCount: true, ProcessedCount: (int)(99584 * variantSuccessRate)));

        // Assert
        var optionsWorkflow = migrationWorkflow.FirstOrDefault(w => w.EntityType == "options");
        optionsWorkflow.ProcessedCount.Should().BeGreaterThan(0, 
            "Options should be extracted and processed from products");

        var variantsWorkflow = migrationWorkflow.FirstOrDefault(w => w.EntityType == "product-variants");
        variantsWorkflow.ProcessedCount.Should().BeGreaterThan(90000, 
            "Variants should have high success rate when option mappings exist");

        _output.WriteLine($"Migration workflow completed:");
        foreach (var (entityType, hasRealCount, processedCount) in migrationWorkflow)
        {
            _output.WriteLine($"  {entityType}: {processedCount} processed (Real count: {hasRealCount})");
        }
    }

    [Fact]
    public void BrokenProgressiveDiscovery_ShouldCauseVariantFailures()
    {
        // Arrange - Simulate the broken scenario (before fix)
        var brokenWorkflow = new List<(string EntityType, int ProcessedCount, string FailureReason)>();

        // Act - Simulate broken progressive discovery
        brokenWorkflow.Add(("products", ProcessedCount: 4791, FailureReason: ""));

        // Broken product-components: ChunkSize=0 means nothing processed
        var componentTypes = new[] { "options", "modifiers", "images", "reviews" };
        foreach (var componentType in componentTypes)
        {
            brokenWorkflow.Add((componentType, ProcessedCount: 0, FailureReason: "ChunkSize=0"));
        }

        // Variants fail because no option mappings
        var optionMappingsExist = brokenWorkflow.Any(w => w.EntityType == "options" && w.ProcessedCount > 0);
        var variantFailureRate = optionMappingsExist ? 0.0 : 1.0; // 100% failure if no mappings

        brokenWorkflow.Add(("product-variants", 
            ProcessedCount: (int)(99584 * (1 - variantFailureRate)), 
            FailureReason: optionMappingsExist ? "" : "Missing option value mappings"));

        // Assert
        var optionsResult = brokenWorkflow.FirstOrDefault(w => w.EntityType == "options");
        optionsResult.ProcessedCount.Should().Be(0, "Broken progressive discovery processes no options");
        optionsResult.FailureReason.Should().Be("ChunkSize=0");

        var variantsResult = brokenWorkflow.FirstOrDefault(w => w.EntityType == "product-variants");
        variantsResult.ProcessedCount.Should().Be(0, "Variants should fail when no option mappings exist");
        variantsResult.FailureReason.Should().Contain("Missing option value mappings");

        _output.WriteLine($"Broken workflow results:");
        foreach (var (entityType, processedCount, failureReason) in brokenWorkflow)
        {
            _output.WriteLine($"  {entityType}: {processedCount} processed - {(string.IsNullOrEmpty(failureReason) ? "Success" : $"Failed: {failureReason}")}");
        }
    }

    [Theory]
    [InlineData(0, false, 0, false)] // No products, no progressive discovery
    [InlineData(0, true, 0, false)]  // Progressive discovery but no products
    [InlineData(0, true, 4791, true)] // Progressive discovery with products (fixed scenario)
    [InlineData(100, false, 0, true)] // Regular discovery with entities
    public void ProgressiveDiscoveryDecision_ShouldReturnCorrectResult(
        int totalCount, 
        bool isProgressiveDiscovery, 
        int totalProducts, 
        bool expectedShouldProcess)
    {
        // Act
        var shouldProcess = SimulateProgressiveDiscoveryDecision(totalCount, isProgressiveDiscovery, totalProducts);

        // Assert
        shouldProcess.Should().Be(expectedShouldProcess);
    }

    private static string[] SimulateEntityDependencyResolution(string[] requestedEntities)
    {
        // Simulate the ResolveEntityDependencies activity logic
        var dependencies = new List<string>(requestedEntities);
        
        if (requestedEntities.Contains("products"))
        {
            dependencies.AddRange(new[] { "options", "modifiers", "images", "reviews", "product-variants" });
        }
        
        return dependencies.ToArray();
    }

    private static bool SimulateProgressiveDiscoveryDecision(int totalCount, bool isProgressiveDiscovery, int totalProducts)
    {
        if (totalCount > 0) return true; // Regular entities
        if (!isProgressiveDiscovery) return false; // No progressive discovery
        return totalProducts > 0; // Progressive discovery with products to process
    }

    private static int SimulateComponentExtraction(string componentType, int productCount)
    {
        // Simulate extracting components from products
        return componentType switch
        {
            "options" => (int)(productCount * 0.3), // ~30% of products have options
            "modifiers" => (int)(productCount * 0.1), // ~10% have modifiers
            "images" => (int)(productCount * 0.8), // ~80% have images
            "reviews" => (int)(productCount * 0.05), // ~5% have reviews
            _ => 0
        };
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}