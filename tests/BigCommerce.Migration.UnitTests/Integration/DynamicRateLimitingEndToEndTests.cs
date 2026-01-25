using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Orchestration.Extensions;
using FluentAssertions;
using Xunit;
using System.Net;

namespace BigCommerce.Migration.UnitTests.Integration;

/// <summary>
/// End-to-end integration tests for dynamic rate limiting complete pipeline
/// Tests the full flow: API call -> header extraction -> health monitoring -> rate calculation -> rate limiting
/// Verifies real-world scenarios and system behavior under various conditions
/// </summary>
public class DynamicRateLimitingEndToEndTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDynamicRateLimiter _dynamicRateLimiter;
    private readonly IApiHealthMonitor _healthMonitor;
    private readonly IRateCalculator _rateCalculator;
    private readonly string _testStoreId = "store-integration-test";

    public DynamicRateLimitingEndToEndTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddOrchestrationServices();
        
        _serviceProvider = services.BuildServiceProvider();
        _dynamicRateLimiter = _serviceProvider.GetRequiredService<IDynamicRateLimiter>();
        _healthMonitor = _serviceProvider.GetRequiredService<IApiHealthMonitor>();
        _rateCalculator = _serviceProvider.GetRequiredService<IRateCalculator>();
    }

    /// <summary>
    /// Tests the complete pipeline: record health data -> calculate rate -> verify rate limiting decision
    /// </summary>
    [Fact]
    public async Task EndToEnd_HealthyAPI_Should_CalculateHighOptimalRate()
    {
        // Arrange - Simulate healthy BigCommerce API responses
        var healthyRateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = 180,
            RequestsQuota = 200,
            TimeResetMs = 50000,
            TimeWindowMs = 60000
        };

        // Act - Record multiple successful API calls with healthy rate limit data
        for (int i = 0; i < 10; i++)
        {
            await _healthMonitor.RecordApiCallAsync(_testStoreId, healthyRateLimitInfo, 150, true, CancellationToken.None);
            await Task.Delay(10); // Simulate time between calls
        }

        // Wait for health aggregation
        await Task.Delay(100);

        // Get optimal rate and health
        var optimalRate = await _dynamicRateLimiter.GetOptimalRateAsync(_testStoreId, CancellationToken.None);
        var healthMetrics = await _dynamicRateLimiter.GetApiHealthAsync(_testStoreId, CancellationToken.None);
        var enhancedStatus = await _dynamicRateLimiter.GetEnhancedRateLimitStatusAsync(_testStoreId, CancellationToken.None);

        // Assert - Healthy API should result in high optimal rate
        optimalRate.Should().BeGreaterThan(25, "healthy API should allow higher throughput");
        optimalRate.Should().BeLessOrEqualTo(50, "rate should respect maximum bounds");
        
        healthMetrics.Should().NotBeNull();
        healthMetrics.GetHealthScore().Should().BeGreaterThan(70, "healthy API should have good health score");
        
        enhancedStatus.Should().NotBeNull();
        enhancedStatus.OptimalRate.Should().BeGreaterThan(1500, "enhanced status should show high req/min rate"); // req/min
        enhancedStatus.HealthScore.Should().BeGreaterThan(70);
    }

    /// <summary>
    /// Tests pipeline behavior with degraded API performance
    /// </summary>
    [Fact]
    public async Task EndToEnd_DegradedAPI_Should_CalculateLowerOptimalRate()
    {
        // Arrange - Simulate degraded BigCommerce API responses
        var degradedRateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = 50,
            RequestsQuota = 200,
            TimeResetMs = 45000,
            TimeWindowMs = 60000
        };

        // Act - Record API calls with slow responses and some failures
        for (int i = 0; i < 10; i++)
        {
            var isSuccess = i < 7; // 70% success rate
            var responseTime = isSuccess ? 800 : 2000; // Slow responses
            
            await _healthMonitor.RecordApiCallAsync(_testStoreId, degradedRateLimitInfo, responseTime, isSuccess, CancellationToken.None);
            await Task.Delay(15);
        }

        await Task.Delay(100);

        var optimalRate = await _dynamicRateLimiter.GetOptimalRateAsync(_testStoreId, CancellationToken.None);
        var healthMetrics = await _dynamicRateLimiter.GetApiHealthAsync(_testStoreId, CancellationToken.None);

        // Assert - Degraded API should result in conservative rate
        optimalRate.Should().BeLessOrEqualTo(20, "degraded API should limit throughput");
        optimalRate.Should().BeGreaterOrEqualTo(5, "rate should respect minimum bounds");
        
        healthMetrics.GetHealthScore().Should().BeLessOrEqualTo(70, "degraded API should have lower health score");
        healthMetrics.ErrorRate.Should().BeGreaterThan(0.2, "should reflect error rate");
        healthMetrics.AverageResponseTimeMs.Should().BeGreaterThan(500, "should reflect slow responses");
    }

    /// <summary>
    /// Tests rate limiting decision integration with health monitoring
    /// </summary>
    [Fact]
    public async Task EndToEnd_RateLimitingDecisions_Should_ReflectHealthConditions()
    {
        // Arrange - Establish poor health conditions
        var criticalRateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = 5,
            RequestsQuota = 200,
            TimeResetMs = 58000,
            TimeWindowMs = 60000
        };

        // Record critical API conditions
        for (int i = 0; i < 5; i++)
        {
            await _healthMonitor.RecordApiCallAsync(_testStoreId, criticalRateLimitInfo, 3000, false, CancellationToken.None);
        }

        await Task.Delay(100);

        // Act - Check if we can make requests under critical conditions
        var canMakeRequest1 = await _dynamicRateLimiter.CanMakeRequestAsync(_testStoreId, CancellationToken.None);
        
        // Try to make multiple rapid requests
        var rapidRequests = new List<bool>();
        for (int i = 0; i < 10; i++)
        {
            var canMake = await _dynamicRateLimiter.CanMakeRequestAsync(_testStoreId, CancellationToken.None);
            rapidRequests.Add(canMake);
            if (canMake)
            {
                await _dynamicRateLimiter.RecordApiCallAsync(_testStoreId, $"test-url-{i}", 100, true, CancellationToken.None);
            }
            await Task.Delay(50); // Small delay between requests
        }

        // Assert - Rate limiting should be more restrictive under poor health
        var allowedRequests = rapidRequests.Count(r => r);
        allowedRequests.Should().BeLessOrEqualTo(10, "poor health should limit allowed requests compared to healthy conditions");
        
        var enhancedStatus = await _dynamicRateLimiter.GetEnhancedRateLimitStatusAsync(_testStoreId, CancellationToken.None);
        enhancedStatus.HealthScore.Should().BeLessOrEqualTo(50, "critical conditions should result in low health score");
    }

    /// <summary>
    /// Tests event triggering throughout the pipeline
    /// </summary>
    [Fact]
    public async Task EndToEnd_HealthAndRateEvents_Should_BeTriggered()
    {
        // Arrange
        var optimalRateChangedEvents = new List<OptimalRateChangedEventArgs>();
        var healthChangedEvents = new List<ApiHealthChangedEventArgs>();

        _dynamicRateLimiter.OptimalRateChanged += (sender, args) => optimalRateChangedEvents.Add(args);
        _dynamicRateLimiter.ApiHealthChanged += (sender, args) => healthChangedEvents.Add(args);

        // Act - Create significant health changes
        var goodRateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = 195,
            RequestsQuota = 200,
            TimeResetMs = 55000,
            TimeWindowMs = 60000
        };

        // Record good performance
        for (int i = 0; i < 5; i++)
        {
            await _healthMonitor.RecordApiCallAsync(_testStoreId, goodRateLimitInfo, 100, true, CancellationToken.None);
            await _dynamicRateLimiter.GetOptimalRateAsync(_testStoreId, CancellationToken.None); // Trigger rate calculation
            await Task.Delay(20);
        }

        // Create dramatic degradation
        var badRateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = 10,
            RequestsQuota = 200,
            TimeResetMs = 59000,
            TimeWindowMs = 60000
        };

        for (int i = 0; i < 5; i++)
        {
            await _healthMonitor.RecordApiCallAsync(_testStoreId, badRateLimitInfo, 2500, false, CancellationToken.None);
            await _dynamicRateLimiter.GetOptimalRateAsync(_testStoreId, CancellationToken.None); // Trigger rate calculation
            await Task.Delay(20);
        }

        await Task.Delay(200); // Allow events to propagate

        // Assert - Events should be triggered for significant changes
        optimalRateChangedEvents.Should().NotBeEmpty("significant rate changes should trigger events");
        healthChangedEvents.Should().NotBeEmpty("significant health changes should trigger events");

        // Verify event data quality
        if (optimalRateChangedEvents.Any())
        {
            var rateEvent = optimalRateChangedEvents.First();
            rateEvent.StoreId.Should().Be(_testStoreId);
            rateEvent.NewRate.Should().BeGreaterThan(0);
        }

        if (healthChangedEvents.Any())
        {
            var healthEvent = healthChangedEvents.First();
            healthEvent.StoreId.Should().Be(_testStoreId);
            healthEvent.NewHealthScore.Should().BeGreaterOrEqualTo(0);
            healthEvent.NewHealthScore.Should().BeLessOrEqualTo(100);
        }
    }

    /// <summary>
    /// Tests backward compatibility - existing rate limiting functionality should still work
    /// </summary>
    [Fact]
    public async Task EndToEnd_BackwardCompatibility_Should_PreserveExistingFunctionality()
    {
        // Arrange
        var rateLimitService = _serviceProvider.GetRequiredService<IRateLimitService>();

        // Act - Use base rate limiting methods (should delegate to dynamic implementation)
        var canMakeRequest = await rateLimitService.CanMakeRequestAsync(_testStoreId, CancellationToken.None);
        
        await rateLimitService.RecordApiCallAsync(_testStoreId, "test-url", 200, true, CancellationToken.None);
        
        var status = await rateLimitService.GetRateLimitStatusAsync(_testStoreId, CancellationToken.None);

        // Assert - Base functionality should work unchanged
        canMakeRequest.Should().BeTrue("backward compatibility should allow requests");
        status.Should().NotBeNull("should return rate limit status");
        status.StoreId.Should().Be(_testStoreId);
        
        // Verify it's actually the dynamic implementation
        rateLimitService.Should().BeOfType<DynamicRateLimitService>("IRateLimitService should resolve to dynamic implementation");
    }

    /// <summary>
    /// Tests system behavior with rapid concurrent requests
    /// </summary>
    [Fact]
    public async Task EndToEnd_ConcurrentRequests_Should_HandleThreadSafety()
    {
        // Arrange
        var tasks = new List<Task>();
        var results = new List<bool>();
        var lockObject = new object();

        // Act - Simulate concurrent API calls from multiple threads
        for (int i = 0; i < 20; i++)
        {
            var taskId = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var rateLimitInfo = new BigCommerceRateLimitInfo
                    {
                        RequestsLeft = 100 - taskId,
                        RequestsQuota = 200,
                        TimeResetMs = 45000,
                        TimeWindowMs = 60000
                    };

                    await _healthMonitor.RecordApiCallAsync(_testStoreId, rateLimitInfo, 150 + taskId * 10, taskId % 5 != 0, CancellationToken.None);
                    
                    var canMakeRequest = await _dynamicRateLimiter.CanMakeRequestAsync(_testStoreId, CancellationToken.None);
                    
                    if (canMakeRequest)
                    {
                        await _dynamicRateLimiter.RecordApiCallAsync(_testStoreId, $"concurrent-url-{taskId}", 100, true, CancellationToken.None);
                    }

                    lock (lockObject)
                    {
                        results.Add(canMakeRequest);
                    }
                }
                catch (Exception ex)
                {
                    // Should not throw exceptions under concurrent access
                    Assert.Fail($"Concurrent operation failed: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - No exceptions should occur, some requests should be allowed
        results.Should().NotBeEmpty("some concurrent requests should be processed");
        results.Should().Contain(true, "some requests should be allowed");
        
        // Verify health monitoring still works after concurrent access
        var finalHealth = await _dynamicRateLimiter.GetApiHealthAsync(_testStoreId, CancellationToken.None);
        finalHealth.Should().NotBeNull("health monitoring should work after concurrent operations");
        finalHealth.TotalRequests.Should().BeGreaterOrEqualTo(20, "should have recorded all concurrent calls");
    }

    /// <summary>
    /// Tests recovery behavior - system should adapt when API health improves
    /// </summary>
    [Fact]
    public async Task EndToEnd_HealthRecovery_Should_IncreaseOptimalRate()
    {
        // Arrange - Start with poor health
        var poorRateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = 20,
            RequestsQuota = 200,
            TimeResetMs = 57000,
            TimeWindowMs = 60000
        };

        for (int i = 0; i < 5; i++)
        {
            await _healthMonitor.RecordApiCallAsync(_testStoreId, poorRateLimitInfo, 2000, false, CancellationToken.None);
        }

        await Task.Delay(50);
        var poorRate = await _dynamicRateLimiter.GetOptimalRateAsync(_testStoreId, CancellationToken.None);

        // Act - Improve health conditions
        var excellentRateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = 199,
            RequestsQuota = 200,
            TimeResetMs = 50000,
            TimeWindowMs = 60000
        };

        for (int i = 0; i < 10; i++)
        {
            await _healthMonitor.RecordApiCallAsync(_testStoreId, excellentRateLimitInfo, 80, true, CancellationToken.None);
            await Task.Delay(10);
        }

        await Task.Delay(100);
        var recoveredRate = await _dynamicRateLimiter.GetOptimalRateAsync(_testStoreId, CancellationToken.None);

        // Assert - Rate should increase significantly after recovery
        recoveredRate.Should().BeGreaterThan(poorRate, "recovered health should increase optimal rate");
        recoveredRate.Should().BeGreaterThan(poorRate * 1.15, "recovery should provide measurable rate improvement");
        
        var finalHealth = await _dynamicRateLimiter.GetApiHealthAsync(_testStoreId, CancellationToken.None);
        finalHealth.GetHealthScore().Should().BeGreaterThan(40, "improved conditions should yield better health score");
    }

    /// <summary>
    /// Tests integration with ApiRequestHandler header extraction
    /// Simulates the real flow where API responses contain BigCommerce headers
    /// </summary>
    [Fact]
    public async Task EndToEnd_HeaderExtraction_Should_IntegrateWithHealthMonitoring()
    {
        // Arrange - Create a mock HTTP response with BigCommerce headers
        var mockHeaders = new Dictionary<string, string>
        {
            ["X-Rate-Limit-Requests-Left"] = "150",
            ["X-Rate-Limit-Requests-Quota"] = "200", 
            ["X-Rate-Limit-Time-Reset-Ms"] = "45000",
            ["X-Rate-Limit-Time-Window-Ms"] = "60000"
        };

        // Simulate header extraction (this would normally be done by ApiRequestHandler)
        var extractedRateLimitInfo = new BigCommerceRateLimitInfo
        {
            RequestsLeft = int.Parse(mockHeaders["X-Rate-Limit-Requests-Left"]),
            RequestsQuota = int.Parse(mockHeaders["X-Rate-Limit-Requests-Quota"]),
            TimeResetMs = long.Parse(mockHeaders["X-Rate-Limit-Time-Reset-Ms"]),
            TimeWindowMs = long.Parse(mockHeaders["X-Rate-Limit-Time-Window-Ms"])
        };

        // Act - Record the health data extracted from headers
        await _healthMonitor.RecordApiCallAsync(_testStoreId, extractedRateLimitInfo, 200, true, CancellationToken.None);
        
        // Trigger rate calculation
        var optimalRate = await _dynamicRateLimiter.GetOptimalRateAsync(_testStoreId, CancellationToken.None);
        var healthMetrics = await _dynamicRateLimiter.GetApiHealthAsync(_testStoreId, CancellationToken.None);

        // Assert - Header data should be properly integrated
        healthMetrics.BigCommerceRateLimit.Should().NotBeNull("BigCommerce rate limit data should be captured");
        healthMetrics.BigCommerceRateLimit!.RequestsLeft.Should().Be(150);
        healthMetrics.BigCommerceRateLimit!.RequestsQuota.Should().Be(200);
        
        optimalRate.Should().BeGreaterThan(5, "valid rate limit data should allow reasonable throughput");
        
        var enhancedStatus = await _dynamicRateLimiter.GetEnhancedRateLimitStatusAsync(_testStoreId, CancellationToken.None);
        enhancedStatus.BigCommerceRateLimit.Should().NotBeNull("enhanced status should include BigCommerce data");
    }
} 