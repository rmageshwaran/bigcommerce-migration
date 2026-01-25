using BigCommerce.Migration.Core.Models;
using FluentAssertions;
using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// Unit tests for DynamicRateLimitingConfiguration and related models
/// Verifies validation logic, default values, and configuration binding
/// </summary>
public class DynamicRateLimitingConfigurationTests
{
    /// <summary>
    /// Test that default configuration values are valid and sensible
    /// </summary>
    [Fact]
    public void DefaultConfiguration_Should_BeValid()
    {
        // Arrange
        var config = new DynamicRateLimitingConfiguration();

        // Act
        var errors = config.Validate();

        // Assert
        errors.Should().BeEmpty("default configuration should be valid");
        
        // Verify feature flags defaults
        config.Features.EnableDynamicRateLimiting.Should().BeTrue();
        config.Features.EnableApiHealthMonitoring.Should().BeTrue();
        config.Features.EnableRealTimeEvents.Should().BeTrue();
        config.Features.EnableAdvancedRateCalculation.Should().BeTrue();
        config.Features.EnableBigCommerceRateLimitIntegration.Should().BeTrue();
        config.Features.EnableDetailedLogging.Should().BeFalse();
        
        // Verify rate calculation defaults
        config.RateCalculation.MinimumRate.Should().Be(5.0);
        config.RateCalculation.MaximumRate.Should().Be(50.0);
        config.RateCalculation.DefaultRate.Should().Be(12.0);
        config.RateCalculation.AreWeightsValid().Should().BeTrue("default weights should sum to 1.0");
        
        // Verify health monitoring defaults
        config.HealthMonitoring.MonitoringWindowMinutes.Should().Be(5);
        config.HealthMonitoring.MaxCallHistorySize.Should().Be(1000);
        config.HealthMonitoring.EnableAutomaticCleanup.Should().BeTrue();
        
        // Verify event defaults
        config.Events.SignificantRateChangeThreshold.Should().Be(0.2);
        config.Events.SignificantHealthChangeThreshold.Should().Be(15.0);
        config.Events.IncludeDetailedHealthInEvents.Should().BeTrue();
        
        // Verify safety defaults
        config.Safety.FallbackRateRequestsPerSecond.Should().Be(8.0);
        config.Safety.EnableAutomaticRecovery.Should().BeTrue();
        config.Safety.EnableCircuitBreaker.Should().BeTrue();
        config.Safety.EnableGracefulDegradation.Should().BeTrue();
    }

    /// <summary>
    /// Test validation of invalid rate calculation settings
    /// </summary>
    [Fact]
    public void RateCalculationValidation_Should_CatchInvalidValues()
    {
        // Arrange
        var config = new DynamicRateLimitingConfiguration();
        
        // Act & Assert - Test minimum rate <= 0
        config.RateCalculation.MinimumRate = 0;
        var errors = config.Validate();
        errors.Should().Contain("MinimumRate must be greater than 0");

        // Act & Assert - Test maximum rate <= minimum rate
        config.RateCalculation.MinimumRate = 10;
        config.RateCalculation.MaximumRate = 5;
        errors = config.Validate();
        errors.Should().Contain("MaximumRate must be greater than MinimumRate");

        // Act & Assert - Test unreasonably high rates
        config.RateCalculation.MinimumRate = 60;
        config.RateCalculation.MaximumRate = 250;
        errors = config.Validate();
        errors.Should().Contain("Rate limits seem unreasonably high - consider API provider limits");
    }

    /// <summary>
    /// Test validation of health monitoring settings
    /// </summary>
    [Fact]
    public void HealthMonitoringValidation_Should_CatchInvalidValues()
    {
        // Arrange
        var config = new DynamicRateLimitingConfiguration();
        
        // Act & Assert - Test monitoring window <= 0
        config.HealthMonitoring.MonitoringWindowMinutes = 0;
        var errors = config.Validate();
        errors.Should().Contain("MonitoringWindowMinutes must be greater than 0");

        // Act & Assert - Test monitoring window too large
        config.HealthMonitoring.MonitoringWindowMinutes = 120;
        errors = config.Validate();
        errors.Should().Contain("MonitoringWindowMinutes should not exceed 60 for responsive rate adjustments");
    }

    /// <summary>
    /// Test validation of event settings
    /// </summary>
    [Fact]
    public void EventSettingsValidation_Should_CatchInvalidValues()
    {
        // Arrange
        var config = new DynamicRateLimitingConfiguration();
        
        // Act & Assert - Test rate change threshold out of range
        config.Events.SignificantRateChangeThreshold = -0.1;
        var errors = config.Validate();
        errors.Should().Contain("SignificantRateChangeThreshold must be between 0 and 1");

        config.Events.SignificantRateChangeThreshold = 1.5;
        errors = config.Validate();
        errors.Should().Contain("SignificantRateChangeThreshold must be between 0 and 1");

        // Act & Assert - Test health change threshold out of range
        config.Events.SignificantRateChangeThreshold = 0.2; // Reset to valid
        config.Events.SignificantHealthChangeThreshold = -5;
        errors = config.Validate();
        errors.Should().Contain("SignificantHealthChangeThreshold must be between 0 and 100");

        config.Events.SignificantHealthChangeThreshold = 150;
        errors = config.Validate();
        errors.Should().Contain("SignificantHealthChangeThreshold must be between 0 and 100");
    }

    /// <summary>
    /// Test validation of safety settings
    /// </summary>
    [Fact]
    public void SafetySettingsValidation_Should_CatchInvalidValues()
    {
        // Arrange
        var config = new DynamicRateLimitingConfiguration();
        
        // Act & Assert - Test fallback rate <= 0
        config.Safety.FallbackRateRequestsPerSecond = 0;
        var errors = config.Validate();
        errors.Should().Contain("FallbackRateRequestsPerSecond must be greater than 0");
    }

    /// <summary>
    /// Test that rate calculation weights validation works correctly
    /// </summary>
    [Fact]
    public void RateCalculationWeights_Should_ValidateCorrectly()
    {
        // Arrange
        var settings = new RateCalculationSettings();
        
        // Act & Assert - Default weights should be valid
        settings.AreWeightsValid().Should().BeTrue("default weights should sum to 1.0");
        
        // Act & Assert - Invalid weights should be detected
        settings.ResponseTimeWeight = 0.5;
        settings.ErrorRateWeight = 0.3;
        settings.BigCommerceRateLimitWeight = 0.3;
        settings.HealthFactorWeight = 0.1; // Total = 1.2
        settings.AreWeightsValid().Should().BeFalse("weights that don't sum to 1.0 should be invalid");
        
        // Act & Assert - Valid custom weights should pass
        settings.ResponseTimeWeight = 0.3;
        settings.ErrorRateWeight = 0.2;
        settings.BigCommerceRateLimitWeight = 0.1;
        settings.HealthFactorWeight = 0.4; // Total = 1.0
        settings.AreWeightsValid().Should().BeTrue("weights that sum to 1.0 should be valid");
    }

    /// <summary>
    /// Test configuration binding from JSON structure
    /// </summary>
    [Fact]
    public void ConfigurationBinding_Should_WorkCorrectly()
    {
        // Arrange
        var jsonConfig = new Dictionary<string, string?>
        {
            ["DynamicRateLimiting:Features:EnableDynamicRateLimiting"] = "false",
            ["DynamicRateLimiting:Features:EnableDetailedLogging"] = "true",
            ["DynamicRateLimiting:RateCalculation:MinimumRate"] = "3.0",
            ["DynamicRateLimiting:RateCalculation:MaximumRate"] = "25.0",
            ["DynamicRateLimiting:RateCalculation:DefaultRate"] = "8.0",
            ["DynamicRateLimiting:HealthMonitoring:MonitoringWindowMinutes"] = "10",
            ["DynamicRateLimiting:HealthMonitoring:MaxCallHistorySize"] = "2000",
            ["DynamicRateLimiting:Events:SignificantRateChangeThreshold"] = "0.15",
            ["DynamicRateLimiting:Events:SignificantHealthChangeThreshold"] = "20.0",
            ["DynamicRateLimiting:Safety:FallbackRateRequestsPerSecond"] = "4.0",
            ["DynamicRateLimiting:Safety:MaxConsecutiveFailures"] = "5"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(jsonConfig)
            .Build();

        var services = new ServiceCollection();
        services.Configure<DynamicRateLimitingConfiguration>(
            configuration.GetSection("DynamicRateLimiting"));
        
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<DynamicRateLimitingConfiguration>>();

        // Act
        var config = options.Value;

        // Assert
        config.Features.EnableDynamicRateLimiting.Should().BeFalse();
        config.Features.EnableDetailedLogging.Should().BeTrue();
        config.RateCalculation.MinimumRate.Should().Be(3.0);
        config.RateCalculation.MaximumRate.Should().Be(25.0);
        config.RateCalculation.DefaultRate.Should().Be(8.0);
        config.HealthMonitoring.MonitoringWindowMinutes.Should().Be(10);
        config.HealthMonitoring.MaxCallHistorySize.Should().Be(2000);
        config.Events.SignificantRateChangeThreshold.Should().Be(0.15);
        config.Events.SignificantHealthChangeThreshold.Should().Be(20.0);
        config.Safety.FallbackRateRequestsPerSecond.Should().Be(4.0);
        config.Safety.MaxConsecutiveFailures.Should().Be(5);
    }

    /// <summary>
    /// Test edge cases and boundary values
    /// </summary>
    [Fact]
    public void EdgeCases_Should_BeHandledCorrectly()
    {
        // Arrange
        var config = new DynamicRateLimitingConfiguration();
        
        // Act & Assert - Minimum valid values
        config.RateCalculation.MinimumRate = 0.1;
        config.RateCalculation.MaximumRate = 0.2;
        config.Events.SignificantRateChangeThreshold = 0.0;
        config.Events.SignificantHealthChangeThreshold = 0.0;
        config.HealthMonitoring.MonitoringWindowMinutes = 1;
        config.Safety.FallbackRateRequestsPerSecond = 0.1;
        
        var errors = config.Validate();
        errors.Should().BeEmpty("minimum valid values should pass validation");
        
        // Act & Assert - Maximum valid values  
        config.RateCalculation.MinimumRate = 49;
        config.RateCalculation.MaximumRate = 50;
        config.Events.SignificantRateChangeThreshold = 1.0;
        config.Events.SignificantHealthChangeThreshold = 100.0;
        config.HealthMonitoring.MonitoringWindowMinutes = 60;
        config.Safety.FallbackRateRequestsPerSecond = 20.0;
        
        errors = config.Validate();
        errors.Should().BeEmpty("maximum valid values should pass validation");
    }

    /// <summary>
    /// Test production-like configuration scenarios
    /// </summary>
    [Fact]
    public void ProductionConfiguration_Should_BeRealistic()
    {
        // Arrange - Production-like configuration
        var config = new DynamicRateLimitingConfiguration
        {
            Features = new FeatureFlags
            {
                EnableDynamicRateLimiting = true,
                EnableDetailedLogging = false // Production should have minimal logging
            },
            RateCalculation = new RateCalculationSettings
            {
                MinimumRate = 5.0,
                MaximumRate = 40.0, // Conservative for production
                DefaultRate = 10.0
            },
            HealthMonitoring = new HealthMonitoringSettings
            {
                MonitoringWindowMinutes = 10, // Longer window for stability
                MaxCallHistorySize = 2000
            },
            Events = new EventSettings
            {
                SignificantRateChangeThreshold = 0.25, // Less sensitive in production
                EventThrottleIntervalSeconds = 60 // Longer throttle
            },
            Safety = new SafetySettings
            {
                FallbackRateRequestsPerSecond = 5.0, // Conservative fallback
                MaxConsecutiveFailures = 5, // More tolerance
                EnableAutomaticRecovery = true
            }
        };

        // Act
        var errors = config.Validate();

        // Assert
        errors.Should().BeEmpty("production configuration should be valid");
        config.RateCalculation.AreWeightsValid().Should().BeTrue("production weights should be valid");
        
        // Verify production-appropriate values
        config.Features.EnableDetailedLogging.Should().BeFalse("production should minimize logging overhead");
        config.RateCalculation.MaximumRate.Should().BeLessOrEqualTo(50, "production should be conservative");
        config.Events.EventThrottleIntervalSeconds.Should().BeGreaterOrEqualTo(30, "production should throttle events");
        config.Safety.EnableAutomaticRecovery.Should().BeTrue("production should self-heal");
    }
} 