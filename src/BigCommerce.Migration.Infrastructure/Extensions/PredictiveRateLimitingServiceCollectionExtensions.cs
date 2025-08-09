using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;

namespace BigCommerce.Migration.Infrastructure.Extensions;

/// <summary>
/// Service collection extensions for predictive rate limiting components
/// Provides centralized registration of all predictive rate limiting services with proper dependencies
/// </summary>
public static class PredictiveRateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Adds all predictive rate limiting services to the service collection
    /// Registers components in correct dependency order with appropriate lifetimes
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPredictiveRateLimiting(this IServiceCollection services)
    {
        // Core foundational services
        services.TryAddSingleton<IRateLimitingTableStorageFactory, RateLimitingTableStorageFactory>();
        
        // Distributed coordination services
        services.TryAddSingleton<IDistributedQuotaTracker, DistributedQuotaTracker>();
        services.TryAddSingleton<IInstanceCoordinator, InstanceCoordinator>();
        services.TryAddSingleton<IInstanceCoordinationManager, InstanceCoordinationManager>();
        services.TryAddSingleton<ITokenConsensusManager, TokenConsensusManager>();
        
        // Health monitoring services
        services.TryAddSingleton<ICoordinationHealthMonitor, CoordinationHealthMonitor>();
        
        // High-level predictive services
        services.TryAddSingleton<IPredictiveRateLimitingService, PredictiveRateLimitingService>();
        services.TryAddSingleton<IQuotaTrackingService, QuotaTrackingService>();
        
        // Real-time monitoring
        services.TryAddSingleton<IPredictiveRateLimitingMonitoringService, PredictiveRateLimitingMonitoringService>();
        
        // Background services
        services.TryAddSingleton<IHeartbeatBackgroundService, HeartbeatBackgroundService>();
        services.AddHostedService<HeartbeatBackgroundService>(provider => 
            (HeartbeatBackgroundService)provider.GetRequiredService<IHeartbeatBackgroundService>());

        return services;
    }

    /// <summary>
    /// Adds enhanced dynamic rate limiting service that integrates predictive capabilities
    /// Decorates the existing IDynamicRateLimiter with predictive intelligence
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddEnhancedDynamicRateLimiting(this IServiceCollection services)
    {
        // First ensure all predictive services are registered
        services.AddPredictiveRateLimiting();

        // Decorate the existing IDynamicRateLimiter with enhanced capabilities
        // Enhanced integration - temporarily disabled for initial build
        // TODO: Add Scrutor package for Decorate pattern or use manual decoration
        // services.Decorate<IDynamicRateLimiter, EnhancedDynamicRateLimitService>();

        return services;
    }

    /// <summary>
    /// Adds only the quota tracking integration without full predictive capabilities
    /// Lightweight integration for existing systems that only want quota intelligence
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddQuotaTrackingOnly(this IServiceCollection services)
    {
        // Core infrastructure for quota tracking
        services.TryAddSingleton<IRateLimitingTableStorageFactory, RateLimitingTableStorageFactory>();
        services.TryAddSingleton<IDistributedQuotaTracker, DistributedQuotaTracker>();
        services.TryAddSingleton<IQuotaTrackingService, QuotaTrackingService>();

        return services;
    }

    /// <summary>
    /// Adds real-time monitoring services only
    /// For systems that want SignalR visibility without changing rate limiting behavior
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPredictiveRateLimitingMonitoring(this IServiceCollection services)
    {
        // Add all predictive services (required for monitoring)
        services.AddPredictiveRateLimiting();
        
        // Monitoring service is already included in AddPredictiveRateLimiting
        // This method exists for semantic clarity
        
        return services;
    }

    /// <summary>
    /// Validates that all required services are properly registered
    /// Throws descriptive exceptions if dependencies are missing
    /// </summary>
    /// <param name="serviceProvider">Service provider to validate</param>
    /// <exception cref="InvalidOperationException">Thrown when required services are not registered</exception>
    public static void ValidatePredictiveRateLimitingServices(this IServiceProvider serviceProvider)
    {
        var requiredServices = new[]
        {
            typeof(IRateLimitingTableStorageFactory),
            typeof(IDistributedQuotaTracker),
            typeof(IInstanceCoordinationManager),
            typeof(ITokenConsensusManager),
            typeof(ICoordinationHealthMonitor),
            typeof(IPredictiveRateLimitingService),
            typeof(IQuotaTrackingService),
            typeof(IPredictiveRateLimitingMonitoringService)
        };

        var missingServices = new List<string>();

        foreach (var serviceType in requiredServices)
        {
            try
            {
                var service = serviceProvider.GetService(serviceType);
                if (service == null)
                {
                    missingServices.Add(serviceType.Name);
                }
            }
            catch (Exception)
            {
                missingServices.Add(serviceType.Name);
            }
        }

        if (missingServices.Count > 0)
        {
            throw new InvalidOperationException(
                $"Predictive rate limiting validation failed. Missing services: {string.Join(", ", missingServices)}. " +
                "Ensure AddPredictiveRateLimiting() is called during service registration.");
        }
    }

    /// <summary>
    /// Gets service registration summary for diagnostic purposes
    /// Provides detailed information about which predictive rate limiting services are registered
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Registration summary</returns>
    public static PredictiveRateLimitingRegistrationSummary GetRegistrationSummary(this IServiceCollection services)
    {
        var summary = new PredictiveRateLimitingRegistrationSummary();

        var serviceTypes = new Dictionary<Type, string>
        {
            { typeof(IRateLimitingTableStorageFactory), "Table Storage Factory" },
            { typeof(IDistributedQuotaTracker), "Quota Tracker" },
            { typeof(IInstanceCoordinator), "Instance Coordinator" },
            { typeof(IInstanceCoordinationManager), "Coordination Manager" },
            { typeof(ITokenConsensusManager), "Token Consensus Manager" },
            { typeof(ICoordinationHealthMonitor), "Health Monitor" },
            { typeof(IPredictiveRateLimitingService), "Predictive Service" },
            { typeof(IQuotaTrackingService), "Quota Tracking Service" },
            { typeof(IPredictiveRateLimitingMonitoringService), "Monitoring Service" },
            { typeof(IHeartbeatBackgroundService), "Heartbeat Background Service" }
        };

        foreach (var (serviceType, friendlyName) in serviceTypes)
        {
            var isRegistered = services.Any(s => s.ServiceType == serviceType);
            summary.RegisteredServices[friendlyName] = isRegistered;
            
            if (isRegistered)
                summary.RegisteredCount++;
            else
                summary.MissingServices.Add(friendlyName);
        }

        summary.TotalServices = serviceTypes.Count;
        summary.IsComplete = summary.RegisteredCount == summary.TotalServices;

        return summary;
    }
}

/// <summary>
/// Summary of predictive rate limiting service registrations
/// </summary>
public class PredictiveRateLimitingRegistrationSummary
{
    /// <summary>
    /// Dictionary of service names and their registration status
    /// </summary>
    public Dictionary<string, bool> RegisteredServices { get; set; } = new();

    /// <summary>
    /// List of services that are not registered
    /// </summary>
    public List<string> MissingServices { get; set; } = new();

    /// <summary>
    /// Number of services that are successfully registered
    /// </summary>
    public int RegisteredCount { get; set; }

    /// <summary>
    /// Total number of required services
    /// </summary>
    public int TotalServices { get; set; }

    /// <summary>
    /// Whether all required services are registered
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Returns a string representation of the registration summary
    /// </summary>
    /// <returns>String with registration status, counts, and details</returns>
    public override string ToString()
    {
        var status = IsComplete ? "COMPLETE" : "INCOMPLETE";
        var details = string.Join(", ", RegisteredServices.Where(kvp => kvp.Value).Select(kvp => kvp.Key));
        var missing = MissingServices.Count > 0 ? $" | Missing: {string.Join(", ", MissingServices)}" : "";
        
        return $"Predictive Rate Limiting Registration: {status} ({RegisteredCount}/{TotalServices}) | " +
               $"Registered: {details}{missing}";
    }
}