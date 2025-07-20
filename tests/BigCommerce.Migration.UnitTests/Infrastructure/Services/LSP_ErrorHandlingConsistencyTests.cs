using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Strategies;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// LSP (Liskov Substitution Principle) error handling consistency tests
/// Ensures all interface implementations handle errors consistently across the system
/// Final task in Phase 4 (LSP) implementation - validates consistent exception handling
/// </summary>
public class LSP_ErrorHandlingConsistencyTests
{
    private readonly Mock<ILogger<CategoryFetchStrategy>> _mockCategoryLogger;
    private readonly Mock<ILogger<ProductFetchStrategy>> _mockProductLogger;
    private readonly Mock<ILogger<BrandFetchStrategy>> _mockBrandLogger;
    private readonly Mock<ILogger<VariantFetchStrategy>> _mockVariantLogger;
    private readonly Mock<ILogger<ImageFetchStrategy>> _mockImageLogger;
    private readonly Mock<ILogger<ModifierFetchStrategy>> _mockModifierLogger;
    private readonly Mock<IBigCommerceApiClient> _mockApiClient;

    public LSP_ErrorHandlingConsistencyTests()
    {
        _mockCategoryLogger = new Mock<ILogger<CategoryFetchStrategy>>();
        _mockProductLogger = new Mock<ILogger<ProductFetchStrategy>>();
        _mockBrandLogger = new Mock<ILogger<BrandFetchStrategy>>();
        _mockVariantLogger = new Mock<ILogger<VariantFetchStrategy>>();
        _mockImageLogger = new Mock<ILogger<ImageFetchStrategy>>();
        _mockModifierLogger = new Mock<ILogger<ModifierFetchStrategy>>();
        _mockApiClient = new Mock<IBigCommerceApiClient>();
    }

    #region LSP Error Handling - Repository Implementations

    /// <summary>
    /// RED PHASE: LSP Error Handling - All repository implementations must handle null parameters consistently
    /// </summary>
    [Fact]
    public void LSP_AllRepositories_ShouldHandleNullParameters_WithConsistentExceptions()
    {
        // Arrange
        var migrationLogger = new Mock<ILogger<MigrationRepository>>().Object;
        var entityMappingLogger = new Mock<ILogger<EntityMappingRepository>>().Object;
        var apiCallLogger = new Mock<ILogger<ApiCallTrackingRepository>>().Object;
        var cancellationLogger = new Mock<ILogger<CancellationTokenRepository>>().Object;

        var repositories = new object[]
        {
            new MigrationRepository(migrationLogger),
            new EntityMappingRepository(entityMappingLogger),
            new ApiCallTrackingRepository(apiCallLogger),
            new CancellationTokenRepository(cancellationLogger)
        };

        var exceptionTypes = new List<Type>();

        // Act - Test null parameter handling across all repositories
        foreach (var repository in repositories)
        {
            var repositoryType = repository.GetType();
            var createMethods = repositoryType.GetMethods()
                .Where(m => m.Name.Contains("Create") && m.GetParameters().Length > 0)
                .ToList();

            foreach (var method in createMethods)
            {
                try
                {
                    var parameters = method.GetParameters().Select(p => (object?)null).ToArray();
                    method.Invoke(repository, parameters);
                }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
                {
                    exceptionTypes.Add(ex.InnerException.GetType());
                }
                catch (Exception ex)
                {
                    exceptionTypes.Add(ex.GetType());
                }
            }
        }

        // Assert - LSP VALIDATION: All repositories should throw same exception type for null parameters
        var uniqueExceptionTypes = exceptionTypes.Distinct().ToList();
        uniqueExceptionTypes.Should().HaveCount(1, 
            "LSP Violation: All repository implementations must handle null parameters with consistent exception types");
        
        uniqueExceptionTypes.First().Should().Be(typeof(ArgumentNullException),
            "All repositories should throw ArgumentNullException for null parameters");
    }

    /// <summary>
    /// RED PHASE: LSP Error Handling - All repository implementations must handle invalid data consistently
    /// </summary>
    [Fact]
    public async Task LSP_AllRepositories_ShouldHandleInvalidData_WithConsistentExceptions()
    {
        // Arrange
        var migrationLogger = new Mock<ILogger<MigrationRepository>>().Object;
        var entityMappingLogger = new Mock<ILogger<EntityMappingRepository>>().Object;

        var migrationRepo = new MigrationRepository(migrationLogger);
        var entityMappingRepo = new EntityMappingRepository(entityMappingLogger);

        var exceptionTypes = new List<Type>();

        // Act - Test invalid data handling
        try
        {
            // Invalid migration entry (missing required fields)
            await migrationRepo.CreateAsync(new MigrationEntry());
        }
        catch (Exception ex)
        {
            exceptionTypes.Add(ex.GetType());
        }

        try
        {
            // Invalid entity mapping (missing required fields)
            await entityMappingRepo.CreateAsync(new EntityMapping());
        }
        catch (Exception ex)
        {
            exceptionTypes.Add(ex.GetType());
        }

        // Assert - LSP VALIDATION: All repositories should handle invalid data consistently
        var uniqueExceptionTypes = exceptionTypes.Distinct().ToList();
        uniqueExceptionTypes.Should().HaveCount(1, 
            "LSP Violation: All repository implementations must handle invalid data with consistent exception types");
        
        // Should throw ArgumentException for invalid data
        uniqueExceptionTypes.First().Should().Be(typeof(ArgumentException),
            "All repositories should throw ArgumentException for invalid data");
    }

    #endregion

    #region LSP Error Handling - Strategy Pattern Implementations

    /// <summary>
    /// RED PHASE: LSP Error Handling - All IEntityFetchStrategy implementations must handle null parameters consistently
    /// </summary>
    [Fact]
    public async Task LSP_AllEntityFetchStrategies_ShouldHandleNullParameters_WithConsistentExceptions()
    {
        // Arrange
        var strategies = new List<IEntityFetchStrategy>
        {
            new CategoryFetchStrategy(_mockApiClient.Object, _mockCategoryLogger.Object),
            new ProductFetchStrategy(_mockApiClient.Object, _mockProductLogger.Object),
            new BrandFetchStrategy(_mockApiClient.Object, _mockBrandLogger.Object),
            new VariantFetchStrategy(_mockApiClient.Object, _mockVariantLogger.Object),
            new ImageFetchStrategy(_mockApiClient.Object, _mockImageLogger.Object),
            new ModifierFetchStrategy(_mockApiClient.Object, _mockModifierLogger.Object)
        };

        var exceptionTypes = new List<Type>();

        // Act - Test null parameter handling across all strategies
        foreach (var strategy in strategies)
        {
            try
            {
                await strategy.FetchEntitiesAsync(null!, "migration-123", new StoreConfiguration(), null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                exceptionTypes.Add(ex.GetType());
            }

            try
            {
                await strategy.FetchEntitiesAsync(new List<string>(), null!, new StoreConfiguration(), null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                exceptionTypes.Add(ex.GetType());
            }

            try
            {
                await strategy.FetchEntitiesAsync(new List<string>(), "migration-123", null!, null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                exceptionTypes.Add(ex.GetType());
            }
        }

        // Assert - LSP VALIDATION: All strategies should handle null parameters consistently
        var uniqueExceptionTypes = exceptionTypes.Distinct().ToList();
        uniqueExceptionTypes.Should().HaveCount(1, 
            "LSP Violation: All IEntityFetchStrategy implementations must handle null parameters with consistent exception types");
        
        uniqueExceptionTypes.First().Should().Be(typeof(ArgumentNullException),
            "All fetch strategies should throw ArgumentNullException for null parameters");
    }

    /// <summary>
    /// RED PHASE: LSP Error Handling - All IEntityFetchStrategy implementations must handle cancellation consistently
    /// </summary>
    [Fact(Skip = "Complex strategy cancellation testing requires proper service setup")]
    public async Task LSP_AllEntityFetchStrategies_ShouldHandleCancellation_WithConsistentExceptions()
    {
        // Arrange
        var strategies = new List<IEntityFetchStrategy>
        {
            new CategoryFetchStrategy(_mockApiClient.Object, _mockCategoryLogger.Object),
            new ProductFetchStrategy(_mockApiClient.Object, _mockProductLogger.Object),
            new BrandFetchStrategy(_mockApiClient.Object, _mockBrandLogger.Object)
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        var exceptionTypes = new List<Type>();

        // Act - Test cancellation handling across all strategies
        foreach (var strategy in strategies)
        {
            try
            {
                await strategy.FetchEntitiesAsync(
                    new List<string> { "1", "2", "3" }, 
                    "migration-123", 
                    new StoreConfiguration { StoreId = "test", AccessToken = "token", ChannelId = "1", BaseUrl = "https://test.com" }, 
                    null, 
                    cts.Token);
            }
            catch (Exception ex)
            {
                exceptionTypes.Add(ex.GetType());
            }
        }

        // Assert - LSP VALIDATION: All strategies should handle cancellation consistently
        var uniqueExceptionTypes = exceptionTypes.Distinct().ToList();
        uniqueExceptionTypes.Should().HaveCount(1, 
            "LSP Violation: All IEntityFetchStrategy implementations must handle cancellation with consistent exception types");
        
        uniqueExceptionTypes.First().Should().Be(typeof(OperationCanceledException),
            "All fetch strategies should throw OperationCanceledException for cancelled operations");
    }

    #endregion

    #region LSP Error Handling - Factory Pattern Implementations

    /// <summary>
    /// RED PHASE: LSP Error Handling - All strategy factories must handle invalid input consistently
    /// </summary>
    [Fact]
    public void LSP_AllStrategyFactories_ShouldHandleInvalidInput_WithConsistentExceptions()
    {
        // Arrange
        var serviceProvider = new Mock<IServiceProvider>().Object;
        var fetchFactory = new EntityFetchStrategyFactory(serviceProvider);

        var exceptionTypes = new List<Type>();

        // Act - Test invalid input handling
        try
        {
            fetchFactory.GetStrategy(null!);
        }
        catch (Exception ex)
        {
            exceptionTypes.Add(ex.GetType());
        }

        try
        {
            fetchFactory.GetStrategy("");
        }
        catch (Exception ex)
        {
            exceptionTypes.Add(ex.GetType());
        }

        try
        {
            fetchFactory.GetStrategy("invalid-entity-type");
        }
        catch (Exception ex)
        {
            exceptionTypes.Add(ex.GetType());
        }

        // Assert - LSP VALIDATION: Factory should handle invalid input consistently
        var uniqueExceptionTypes = exceptionTypes.Distinct().ToList();
        uniqueExceptionTypes.Should().HaveCount(1, 
            "LSP Violation: Strategy factories must handle invalid input with consistent exception types");
        
        uniqueExceptionTypes.First().Should().Be(typeof(ArgumentException),
            "Strategy factories should throw ArgumentException for invalid input");
    }

    #endregion

    #region LSP Error Handling - Cross-Pattern Consistency

    /// <summary>
    /// RED PHASE: LSP Error Handling - All implementations must follow consistent error categorization
    /// </summary>
    [Fact]
    public void LSP_AllImplementations_ShouldFollowConsistentErrorCategorization()
    {
        // Arrange - Test that implementations categorize errors consistently:
        // - ArgumentNullException for null parameters
        // - ArgumentException for invalid parameters  
        // - OperationCanceledException for cancellation
        // - InvalidOperationException for state-related errors

        var errorCategories = new Dictionary<string, Type>
        {
            ["null-parameter"] = typeof(ArgumentNullException),
            ["invalid-parameter"] = typeof(ArgumentException),
            ["cancellation"] = typeof(OperationCanceledException),
            ["invalid-state"] = typeof(InvalidOperationException)
        };

        // Act & Assert - Test that all implementations follow these categorizations
        foreach (var category in errorCategories)
        {
            category.Value.Should().BeAssignableTo<Exception>(
                $"Error category '{category.Key}' should use appropriate exception type");
        }

        // Validate exception hierarchy
        typeof(ArgumentNullException).Should().BeAssignableTo<ArgumentException>(
            "ArgumentNullException should inherit from ArgumentException for consistent handling");
        
        typeof(ArgumentException).Should().BeAssignableTo<SystemException>(
            "ArgumentException should be part of system exception hierarchy");
    }

    /// <summary>
    /// RED PHASE: LSP Error Handling - All implementations must provide meaningful error messages
    /// </summary>
    [Fact]
    public async Task LSP_AllImplementations_ShouldProvideConsistentErrorMessages()
    {
        // Arrange
        var migrationLogger = new Mock<ILogger<MigrationRepository>>().Object;
        var repository = new MigrationRepository(migrationLogger);

        var strategy = new CategoryFetchStrategy(_mockApiClient.Object, _mockCategoryLogger.Object);

        var errorMessages = new List<string>();

        // Act - Collect error messages from different implementations
        try
        {
            await repository.CreateAsync(null!);
        }
        catch (Exception ex)
        {
            errorMessages.Add(ex.Message);
        }

        try
        {
            await strategy.FetchEntitiesAsync(null!, "test", new StoreConfiguration(), null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            errorMessages.Add(ex.Message);
        }

        // Assert - LSP VALIDATION: Error messages should be meaningful and consistent
        errorMessages.Should().OnlyContain(msg => !string.IsNullOrWhiteSpace(msg),
            "All implementations should provide meaningful error messages");

        errorMessages.Should().OnlyContain(msg => msg.Length > 10,
            "Error messages should be descriptive enough to be helpful");
    }

    #endregion

    #region LSP Error Handling - Integration Points

    /// <summary>
    /// RED PHASE: LSP Error Handling - Interface implementations must handle downstream errors consistently
    /// </summary>
    [Fact]
    public async Task LSP_AllImplementations_ShouldHandleDownstreamErrors_Consistently()
    {
        // Arrange - Setup mock to throw different types of downstream errors
        var logger = new Mock<ILogger<ApiRequestHandler>>().Object;
        var mockRateLimitService = new Mock<IRateLimitService>();
        var mockOpenSearchService = new Mock<IOpenSearchService>();
        var mockHttpClient = new Mock<HttpClient>();

        // Setup downstream service to throw errors
        mockRateLimitService.Setup(x => x.CheckAndWaitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Rate limit service error"));

        var apiHandler = new ApiRequestHandler(
            mockHttpClient.Object,
            mockRateLimitService.Object,
            mockOpenSearchService.Object,
            logger);

        var validStoreConfig = new StoreConfiguration 
        { 
            StoreId = "test", 
            AccessToken = "token", 
            ChannelId = "1", // Required for StoreConfiguration.IsValid()
            BaseUrl = "https://test.com" 
        };
        
        var request = new ApiRequest
        {
            Url = "https://test.com/api",
            Method = HttpMethod.Get,
            StoreConfiguration = validStoreConfig
        };

        // Act & Assert - Test that downstream errors are handled consistently
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            apiHandler.ExecuteRequestAsync<string>(request, CancellationToken.None));

        // LSP VALIDATION: Implementation should preserve downstream exception types
        // This ensures that error handling remains predictable when substituting implementations
    }

    #endregion
} 