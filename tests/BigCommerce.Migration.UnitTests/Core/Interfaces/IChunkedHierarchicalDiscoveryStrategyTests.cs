using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Xunit;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace BigCommerce.Migration.UnitTests.Core.Interfaces;

/// <summary>
/// TDD unit tests for IChunkedHierarchicalDiscoveryStrategy interface
/// Tests define the expected behavior - implementation will follow (TDD)
/// Validates Dependency Inversion Principle compliance and interface contracts
/// </summary>
public class IChunkedHierarchicalDiscoveryStrategyTests
{
    #region Interface Contract Tests (RED phase - should fail initially)

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_ShouldHave_RequiredMembers()
    {
        // Act - Verify interface exists and has required members
        var interfaceType = typeof(IChunkedHierarchicalDiscoveryStrategy);
        
        // Assert - Interface should exist and be properly defined
        interfaceType.Should().NotBeNull();
        interfaceType.IsInterface.Should().BeTrue();
        
        // Verify required properties (including inherited ones)
        var supportedApiVersionProperty = interfaceType.GetProperty("SupportedApiVersion") 
            ?? interfaceType.GetInterfaces().SelectMany(i => i.GetProperties()).FirstOrDefault(p => p.Name == "SupportedApiVersion");
        supportedApiVersionProperty.Should().NotBeNull("SupportedApiVersion property should exist (directly or inherited)");
        
        var maxMemoryUsageMBProperty = interfaceType.GetProperty("MaxMemoryUsageMB");
        maxMemoryUsageMBProperty.Should().NotBeNull("MaxMemoryUsageMB property should exist");
        
        // Verify required methods
        var analyzeHierarchyMethod = interfaceType.GetMethod("AnalyzeHierarchyAsync");
        analyzeHierarchyMethod.Should().NotBeNull("AnalyzeHierarchyAsync method should exist");
        
        var discoverLevelMethod = interfaceType.GetMethod("DiscoverLevelAsync");
        discoverLevelMethod.Should().NotBeNull("DiscoverLevelAsync method should exist");
        
        var estimateProcessingTimeMethod = interfaceType.GetMethod("EstimateProcessingTimeAsync");
        estimateProcessingTimeMethod.Should().NotBeNull("EstimateProcessingTimeAsync method should exist");
    }

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_AnalyzeHierarchyAsync_ShouldHave_CorrectSignature()
    {
        // Act - Get method info
        var interfaceType = typeof(IChunkedHierarchicalDiscoveryStrategy);
        var method = interfaceType.GetMethod("AnalyzeHierarchyAsync");
        
        // Assert - Verify method signature
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<HierarchyMetadata>), "Should return Task<HierarchyMetadata>");
        
        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(3, "Should have 3 parameters");
        
        parameters[0].ParameterType.Should().Be(typeof(EntityDiscoveryRequest), "First parameter should be EntityDiscoveryRequest");
        parameters[1].ParameterType.Should().Be(typeof(ChunkedHierarchyConfiguration), "Second parameter should be ChunkedHierarchyConfiguration");
        parameters[2].ParameterType.Should().Be(typeof(CancellationToken), "Third parameter should be CancellationToken");
        parameters[2].HasDefaultValue.Should().BeTrue("CancellationToken should have default value");
    }

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_DiscoverLevelAsync_ShouldHave_CorrectSignature()
    {
        // Act - Get method info
        var interfaceType = typeof(IChunkedHierarchicalDiscoveryStrategy);
        var method = interfaceType.GetMethod("DiscoverLevelAsync");
        
        // Assert - Verify method signature
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<LevelProcessingResult>), "Should return Task<LevelProcessingResult>");
        
        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(3, "Should have 3 parameters");
        
        parameters[0].ParameterType.Should().Be(typeof(LevelFetchRequest), "First parameter should be LevelFetchRequest");
        parameters[1].ParameterType.Should().Be(typeof(EntityDiscoveryRequest), "Second parameter should be EntityDiscoveryRequest");
        parameters[2].ParameterType.Should().Be(typeof(CancellationToken), "Third parameter should be CancellationToken");
        parameters[2].HasDefaultValue.Should().BeTrue("CancellationToken should have default value");
    }

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_EstimateProcessingTimeAsync_ShouldHave_CorrectSignature()
    {
        // Act - Get method info
        var interfaceType = typeof(IChunkedHierarchicalDiscoveryStrategy);
        var method = interfaceType.GetMethod("EstimateProcessingTimeAsync");
        
        // Assert - Verify method signature
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<double>), "Should return Task<double>");
        
        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(3, "Should have 3 parameters");
        
        parameters[0].ParameterType.Should().Be(typeof(EntityDiscoveryRequest), "First parameter should be EntityDiscoveryRequest");
        parameters[1].ParameterType.Should().Be(typeof(ChunkedHierarchyConfiguration), "Second parameter should be ChunkedHierarchyConfiguration");
        parameters[2].ParameterType.Should().Be(typeof(CancellationToken), "Third parameter should be CancellationToken");
        parameters[2].HasDefaultValue.Should().BeTrue("CancellationToken should have default value");
    }

    #endregion

    #region Dependency Inversion Principle Tests

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_ShouldFollow_DependencyInversionPrinciple()
    {
        // Act - Verify interface design follows DIP
        var interfaceType = typeof(IChunkedHierarchicalDiscoveryStrategy);
        
        // Assert - Should depend on abstractions, not concretions
        interfaceType.IsInterface.Should().BeTrue("Should be an interface (abstraction)");
        
        // Verify it uses model abstractions (Phase 1 interfaces)
        var analyzeMethod = interfaceType.GetMethod("AnalyzeHierarchyAsync");
        var returnType = analyzeMethod!.ReturnType.GetGenericArguments()[0]; // Task<T> -> T
        returnType.Should().Be(typeof(HierarchyMetadata), "Should return HierarchyMetadata abstraction");
        
        // Verify method parameters use abstractions
        var discoverMethod = interfaceType.GetMethod("DiscoverLevelAsync");
        var parameters = discoverMethod!.GetParameters();
        parameters[0].ParameterType.Should().Be(typeof(LevelFetchRequest), "Should use LevelFetchRequest model");
        
        // Interface should not reference concrete BigCommerce API classes directly
        var methods = interfaceType.GetMethods();
        foreach (var method in methods)
        {
            foreach (var param in method.GetParameters())
            {
                param.ParameterType.Name.Should().NotContain("BigCommerceApiClient");
            }
        }
    }

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_ShouldSupport_MemorySafeOperations()
    {
        // Act - Verify interface supports memory safety
        var interfaceType = typeof(IChunkedHierarchicalDiscoveryStrategy);
        
        // Assert - Should have memory usage tracking
        var maxMemoryProperty = interfaceType.GetProperty("MaxMemoryUsageMB");
        maxMemoryProperty.Should().NotBeNull("Should have MaxMemoryUsageMB property for memory monitoring");
        maxMemoryProperty!.PropertyType.Should().Be(typeof(double), "Memory usage should be tracked as double");
        
        // Methods should support cancellation for memory safety
        var methods = interfaceType.GetMethods().Where(m => m.Name.EndsWith("Async"));
        foreach (var method in methods)
        {
            var cancellationParam = method.GetParameters().LastOrDefault();
            cancellationParam.Should().NotBeNull("Async methods should have CancellationToken parameter");
            cancellationParam!.ParameterType.Should().Be(typeof(CancellationToken), "Should have CancellationToken parameter");
        }
    }

    #endregion

    #region Interface Segregation Verification

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_ShouldFollow_InterfaceSegregationPrinciple()
    {
        // Act - Verify interface is properly segregated
        var interfaceType = typeof(IChunkedHierarchicalDiscoveryStrategy);
        
        // Assert - Should have focused responsibilities (hierarchical discovery only)
        var methods = interfaceType.GetMethods().Where(m => !m.IsSpecialName);
        methods.Should().HaveCount(3, "Should have exactly 3 methods for focused responsibility");
        
        // Method names should indicate hierarchical operations
        var methodNames = methods.Select(m => m.Name).ToList();
        methodNames.Should().Contain("AnalyzeHierarchyAsync", "Should have hierarchy analysis method");
        methodNames.Should().Contain("DiscoverLevelAsync", "Should have level discovery method");
        methodNames.Should().Contain("EstimateProcessingTimeAsync", "Should have time estimation method");
        
        // Should not have generic discovery methods (those belong to IEntityDiscoveryStrategy)
        methodNames.Should().NotContain("DiscoverEntitiesAsync", "Should not duplicate generic discovery methods");
    }

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_ShouldNot_DuplicateBaseInterface()
    {
        // Act - Verify interface doesn't duplicate IEntityDiscoveryStrategy
        var chunkedInterface = typeof(IChunkedHierarchicalDiscoveryStrategy);
        var baseInterface = typeof(IEntityDiscoveryStrategy);
        
        var chunkedMethods = chunkedInterface.GetMethods().Select(m => m.Name);
        var baseMethods = baseInterface.GetMethods().Select(m => m.Name);
        
        // Assert - Should not duplicate method names from base interface (except shared properties)
        var duplicatedMethods = chunkedMethods.Intersect(baseMethods);
        var allowedSharedMembers = new[] { "get_SupportedApiVersion" }; // API version is needed by both
        var unexpectedDuplicates = duplicatedMethods.Except(allowedSharedMembers);
        unexpectedDuplicates.Should().BeEmpty("Chunked interface should not duplicate base interface methods except for shared properties");
        
        // Should be focused on chunked hierarchical operations only (excluding shared properties)
        var hierarchicalMethods = chunkedMethods.Except(allowedSharedMembers);
        hierarchicalMethods.Should().AllSatisfy(name => 
        {
            (name.Contains("Hierarchy") || name.Contains("Level") || name.Contains("Processing") || 
             name.Contains("Memory") || name.Contains("Estimate"))
                .Should().BeTrue($"Method {name} should be related to hierarchical/level processing or memory management");
        });
    }

    #endregion

    #region Memory Safety and Performance Tests

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_ShouldSupport_AzureFunctionsConstraints()
    {
        // Act - Verify interface supports Azure Functions constraints
        var interfaceType = typeof(IChunkedHierarchicalDiscoveryStrategy);
        
        // Assert - Should have memory monitoring for Azure Functions 1.5GB limit
        var maxMemoryProperty = interfaceType.GetProperty("MaxMemoryUsageMB");
        maxMemoryProperty.Should().NotBeNull("Should track memory usage for Azure Functions constraints");
        
        // All async methods should support cancellation for timeout handling
        var asyncMethods = interfaceType.GetMethods().Where(m => m.Name.EndsWith("Async"));
        asyncMethods.Should().AllSatisfy(method =>
        {
            var parameters = method.GetParameters();
            var cancellationParam = parameters.LastOrDefault();
            cancellationParam!.ParameterType.Should().Be(typeof(CancellationToken), 
                $"Method {method.Name} should support cancellation for timeout handling");
        });
    }

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_ShouldSupport_BigCommerceApiVersions()
    {
        // Act - Verify interface supports API version detection
        var interfaceType = typeof(IChunkedHierarchicalDiscoveryStrategy);
        
        // Assert - Should have API version property (including inherited ones)
        var apiVersionProperty = interfaceType.GetProperty("SupportedApiVersion") 
            ?? interfaceType.GetInterfaces().SelectMany(i => i.GetProperties()).FirstOrDefault(p => p.Name == "SupportedApiVersion");
        apiVersionProperty.Should().NotBeNull("Should declare supported API version (directly or inherited)");
        apiVersionProperty!.PropertyType.Should().Be(typeof(BigCommerceApiVersion), 
            "Should use BigCommerceApiVersion enum");
        
        // Should be compatible with existing discovery request pattern
        var analyzeMethod = interfaceType.GetMethod("AnalyzeHierarchyAsync");
        var firstParam = analyzeMethod!.GetParameters()[0];
        firstParam.ParameterType.Should().Be(typeof(EntityDiscoveryRequest), 
            "Should use same request model as existing strategies");
    }

    #endregion

    #region Continue-on-Error Policy Tests

    [Fact]
    public void IChunkedHierarchicalDiscoveryStrategy_ShouldSupport_ContinueOnErrorPolicy()
    {
        // Act - Verify interface supports continue-on-error policy
        var interfaceType = typeof(IChunkedHierarchicalDiscoveryStrategy);
        
        // Assert - DiscoverLevelAsync should return LevelProcessingResult which supports error tracking
        var discoverMethod = interfaceType.GetMethod("DiscoverLevelAsync");
        var returnType = discoverMethod!.ReturnType.GetGenericArguments()[0]; // Task<T> -> T
        returnType.Should().Be(typeof(LevelProcessingResult), 
            "Should return LevelProcessingResult which supports continue-on-error");
        
        // Methods should not throw exceptions for individual failures (continue-on-error)
        // This is verified by the return type supporting error collection rather than throwing
        var methods = interfaceType.GetMethods();
        methods.Should().AllSatisfy(method =>
        {
            method.ReturnType.Should().NotBe(typeof(void), 
                "Methods should return results instead of throwing exceptions");
        });
    }

    #endregion
}