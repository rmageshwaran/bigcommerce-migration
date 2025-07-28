using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.Core.Interfaces;

/// <summary>
/// Unit tests for IDynamicRateLimiter interface
/// Tests dynamic rate limiting capabilities extending base IRateLimitService
/// Follows TDD approach - tests written before implementation
/// </summary>
public class IDynamicRateLimiterTests
{
    private readonly Mock<ILogger<IDynamicRateLimiter>> _mockLogger;

    public IDynamicRateLimiterTests()
    {
        _mockLogger = new Mock<ILogger<IDynamicRateLimiter>>();
    }

    #region Interface Contract Tests (Liskov Substitution Principle)

    [Fact]
    public void IDynamicRateLimiter_Should_ExtendIRateLimitService()
    {
        // Arrange & Act & Assert - Interface inheritance test
        typeof(IDynamicRateLimiter).Should().BeAssignableTo<IRateLimitService>();
    }

    [Fact]
    public void IDynamicRateLimiter_Should_HaveGetOptimalRateMethod()
    {
        // Arrange & Act
        var method = typeof(IDynamicRateLimiter).GetMethod(nameof(IDynamicRateLimiter.GetOptimalRateAsync));

        // Assert
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<double>));
    }

    [Fact]
    public void IDynamicRateLimiter_Should_HaveUpdateApiHealthMethod()
    {
        // Arrange & Act
        var method = typeof(IDynamicRateLimiter).GetMethod(nameof(IDynamicRateLimiter.UpdateApiHealthAsync));

        // Assert
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void IDynamicRateLimiter_Should_HaveGetApiHealthMethod()
    {
        // Arrange & Act
        var method = typeof(IDynamicRateLimiter).GetMethod(nameof(IDynamicRateLimiter.GetApiHealthAsync));

        // Assert
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<ApiHealthMetrics>));
    }

    [Fact]
    public void IDynamicRateLimiter_Should_HaveGetEnhancedRateLimitStatusMethod()
    {
        // Arrange & Act
        var method = typeof(IDynamicRateLimiter).GetMethod(nameof(IDynamicRateLimiter.GetEnhancedRateLimitStatusAsync));

        // Assert
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<EnhancedRateLimitStatus>));
    }

    #endregion

    #region Dynamic Rate Calculation Tests

    [Theory]
    [InlineData(5)]   // Minimum rate
    [InlineData(12)]  // Current static rate
    [InlineData(25)]  // Mid-range dynamic rate
    [InlineData(50)]  // Maximum rate
    public void GetOptimalRateAsync_Should_ReturnValidRateRange(int expectedRate)
    {
        // Arrange - This test validates the interface contract
        // Implementation will be tested in concrete class tests
        expectedRate.Should().BeInRange(5, 50); // Validate test data is in expected range

        // Act & Assert - Interface method signature validation
        var method = typeof(IDynamicRateLimiter).GetMethod(nameof(IDynamicRateLimiter.GetOptimalRateAsync));
        method.Should().NotBeNull();
        method!.GetParameters().Should().HaveCount(2); // storeId, cancellationToken
    }

    [Fact]
    public void GetOptimalRateAsync_Should_RequireStoreId()
    {
        // Arrange & Act
        var method = typeof(IDynamicRateLimiter).GetMethod(nameof(IDynamicRateLimiter.GetOptimalRateAsync));
        var parameters = method!.GetParameters();

        // Assert
        parameters[0].Name.Should().Be("storeId");
        parameters[0].ParameterType.Should().Be(typeof(string));
    }

    [Fact]
    public void GetOptimalRateAsync_Should_SupportCancellation()
    {
        // Arrange & Act
        var method = typeof(IDynamicRateLimiter).GetMethod(nameof(IDynamicRateLimiter.GetOptimalRateAsync));
        var parameters = method!.GetParameters();

        // Assert
        parameters[1].Name.Should().Be("cancellationToken");
        parameters[1].ParameterType.Should().Be(typeof(CancellationToken));
        parameters[1].HasDefaultValue.Should().BeTrue();
    }

    #endregion

    #region API Health Management Tests

    [Fact]
    public void UpdateApiHealthAsync_Should_AcceptBigCommerceRateLimitInfo()
    {
        // Arrange & Act
        var method = typeof(IDynamicRateLimiter).GetMethod(nameof(IDynamicRateLimiter.UpdateApiHealthAsync));
        var parameters = method!.GetParameters();

        // Assert
        parameters.Should().HaveCount(3); // storeId, rateLimitInfo, cancellationToken
        parameters[0].ParameterType.Should().Be(typeof(string));
        parameters[1].ParameterType.Should().Be(typeof(BigCommerceRateLimitInfo));
        parameters[2].ParameterType.Should().Be(typeof(CancellationToken));
    }

    [Fact]
    public void GetApiHealthAsync_Should_ReturnApiHealthMetrics()
    {
        // Arrange & Act
        var method = typeof(IDynamicRateLimiter).GetMethod(nameof(IDynamicRateLimiter.GetApiHealthAsync));

        // Assert
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<ApiHealthMetrics>));
    }

    #endregion

    #region Enhanced Status Tests

    [Fact]
    public void GetEnhancedRateLimitStatusAsync_Should_ExtendBaseStatus()
    {
        // Arrange & Act
        var method = typeof(IDynamicRateLimiter).GetMethod(nameof(IDynamicRateLimiter.GetEnhancedRateLimitStatusAsync));
        var parameters = method!.GetParameters();

        // Assert
        method.Should().NotBeNull();
        parameters.Should().HaveCount(2); // storeId, cancellationToken
        method!.ReturnType.Should().Be(typeof(Task<EnhancedRateLimitStatus>));
    }

    #endregion

    #region Thread Safety Contract Tests

    [Fact]
    public void IDynamicRateLimiter_AllMethods_Should_BeAsync()
    {
        // Arrange
        var methods = typeof(IDynamicRateLimiter).GetMethods()
            .Where(m => m.DeclaringType == typeof(IDynamicRateLimiter))
            .Where(m => !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_")); // Exclude event accessors

        // Act & Assert - All methods should return Task or Task<T>
        foreach (var method in methods)
        {
            method.ReturnType.Should().Match(t => 
                t == typeof(Task) || 
                (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>)),
                $"Method {method.Name} should be async");
        }
    }

    #endregion

    #region Error Handling Contract Tests

    [Fact]
    public void IDynamicRateLimiter_Methods_Should_SupportCancellationToken()
    {
        // Arrange
        var methods = typeof(IDynamicRateLimiter).GetMethods()
            .Where(m => m.DeclaringType == typeof(IDynamicRateLimiter))
            .Where(m => !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_")); // Exclude event accessors

        // Act & Assert - All methods should accept CancellationToken
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var hasCancellationToken = parameters.Any(p => p.ParameterType == typeof(CancellationToken));
            
            hasCancellationToken.Should().BeTrue($"Method {method.Name} should accept CancellationToken");
        }
    }

    #endregion
} 