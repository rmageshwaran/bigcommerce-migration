using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// Demonstrates how Dynamic Rate Limiting works when hitting actual 429 rate limit errors
/// Shows the basic rate adjustment behavior without predictive features
/// </summary>
public class DynamicRateLimitDemonstration
{
    private readonly ITestOutputHelper _output;

    public DynamicRateLimitDemonstration(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DemonstrateBasicDynamicRateLimit_When429Errors()
    {
        _output.WriteLine("🎯 Dynamic Rate Limiting Demonstration");
        _output.WriteLine("=====================================");
        _output.WriteLine("Scenario: Migration hits BigCommerce 429 rate limits");
        _output.WriteLine("Expected: System automatically reduces rate to recover");
        _output.WriteLine("");

        // Setup basic services (NO PREDICTIVE FEATURES)
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        // Mock dependencies
        var mockRateLimitService = new Mock<IRateLimitService>();
        var mockHealthMonitor = new Mock<IApiHealthMonitor>();
        var mockRateCalculator = new Mock<IRateCalculator>();
        
        services.AddSingleton(mockRateLimitService.Object);
        services.AddSingleton(mockHealthMonitor.Object);
        services.AddSingleton(mockRateCalculator.Object);
        
        // Register basic dynamic rate limiter (no predictive services)
        services.AddSingleton<DynamicRateLimitService>();
        services.AddSingleton<IDynamicRateLimiter>(sp => sp.GetRequiredService<DynamicRateLimitService>());
        
        var serviceProvider = services.BuildServiceProvider();
        var rateLimiter = serviceProvider.GetRequiredService<IDynamicRateLimiter>();
        var logger = serviceProvider.GetRequiredService<ILogger<DynamicRateLimitDemonstration>>();

        const string storeId = "demo-store-123";
        
        // Simulate migration workflow with rate limiting
        await SimulateMigrationWithRateLimiting(rateLimiter, mockHealthMonitor, mockRateCalculator, storeId, logger);
    }

    private async Task SimulateMigrationWithRateLimiting(
        IDynamicRateLimiter rateLimiter, 
        Mock<IApiHealthMonitor> mockHealthMonitor,
        Mock<IRateCalculator> mockRateCalculator,
        string storeId, 
        ILogger logger)
    {
        _output.WriteLine("🚀 Starting Migration Simulation");
        _output.WriteLine("--------------------------------");

        var totalRequests = 0;
        var successful = 0;
        var rateLimitHits = 0;
        var currentRate = 10.0; // Start optimistic
        
        // Setup mocks to simulate health degradation on 429s
        var healthScore = 85.0;
        mockHealthMonitor.Setup(x => x.GetApiHealthAsync(storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApiHealthMetrics
            {
                StoreId = storeId,
                AverageResponseTimeMs = healthScore > 70 ? 200 : 800,
                ErrorRate = (100 - healthScore) / 100.0,
                SuccessfulRequests = successful,
                FailedRequests = rateLimitHits
            });

        // Rate calculator reduces rate when health is poor
        mockRateCalculator.Setup(x => x.CalculateOptimalRate(It.IsAny<ApiHealthMetrics>()))
            .Returns<ApiHealthMetrics>(metrics => 
            {
                var score = 100 - (metrics.ErrorRate * 100);
                if (score > 80) return 10.0;      // Healthy: full speed
                if (score > 60) return 6.0;       // Degraded: reduce
                if (score > 40) return 3.0;       // Poor: much slower
                return 1.0;                       // Critical: minimal rate
            });

        // Simulate 100 API calls during migration
        for (int batch = 1; batch <= 10; batch++)
        {
            _output.WriteLine($"\n📦 Processing Batch {batch}/10");
            
            // Get current optimal rate
            var optimalRate = await rateLimiter.GetOptimalRateAsync(storeId);
            currentRate = optimalRate;
            _output.WriteLine($"   📊 Current Rate: {currentRate:F1} req/sec (Health: {healthScore:F0}%)");
            
            // Process 10 entities in this batch
            for (int i = 1; i <= 10; i++)
            {
                totalRequests++;
                
                // Simulate API call delay based on current rate
                var delayMs = (int)(1000 / currentRate);
                await Task.Delay(delayMs);
                
                // Simulate BigCommerce API response
                var apiResult = SimulateBigCommerceApiCall(currentRate, healthScore);
                
                if (apiResult.Hit429)
                {
                    rateLimitHits++;
                    healthScore = Math.Max(20, healthScore - 8); // Health degrades on 429
                    _output.WriteLine($"   ❌ 429 Rate Limit Hit! (Request {i}) - Health now: {healthScore:F0}%");
                    
                    // Record failure in health monitor
                    await mockHealthMonitor.Object.RecordApiCallAsync(
                        storeId, null, apiResult.ResponseTimeMs, false);
                }
                else
                {
                    successful++;
                    healthScore = Math.Min(95, healthScore + 1); // Gradual recovery
                    _output.WriteLine($"   ✅ Success (Request {i}) - {apiResult.ResponseTimeMs}ms");
                    
                    // Record success
                    await mockHealthMonitor.Object.RecordApiCallAsync(
                        storeId, null, apiResult.ResponseTimeMs, true);
                }
            }
            
            _output.WriteLine($"   📈 Batch {batch} Complete - Success: {successful}/{totalRequests} ({(double)successful/totalRequests*100:F1}%)");
        }

        // Final results
        _output.WriteLine("\n🏁 Migration Simulation Complete");
        _output.WriteLine("================================");
        _output.WriteLine($"📊 Total Requests: {totalRequests}");
        _output.WriteLine($"✅ Successful: {successful} ({(double)successful/totalRequests*100:F1}%)");
        _output.WriteLine($"❌ Rate Limit Hits: {rateLimitHits} ({(double)rateLimitHits/totalRequests*100:F1}%)");
        _output.WriteLine($"🎯 Final Health Score: {healthScore:F1}%");
        _output.WriteLine($"⚡ Final Rate: {currentRate:F1} req/sec");
        
        if (rateLimitHits == 0)
        {
            _output.WriteLine("\n🎉 PERFECT! Zero rate limit hits - system adapted perfectly!");
        }
        else if (rateLimitHits <= totalRequests * 0.1) // 10% or less
        {
            _output.WriteLine("\n✅ GOOD! Dynamic rate limiting kept errors under control");
        }
        else
        {
            _output.WriteLine("\n⚠️  High rate limit hits - system needs tuning");
        }
    }

    private ApiCallResult SimulateBigCommerceApiCall(double currentRate, double healthScore)
    {
        var random = new Random();
        
        // Higher rate = higher chance of 429
        // Lower health = higher chance of 429
        var rateLimitProbability = Math.Max(0, (currentRate - 5) / 20.0) + Math.Max(0, (80 - healthScore) / 100.0);
        rateLimitProbability = Math.Min(0.8, rateLimitProbability); // Cap at 80%
        
        var hit429 = random.NextDouble() < rateLimitProbability;
        
        return new ApiCallResult
        {
            Hit429 = hit429,
            ResponseTimeMs = hit429 ? random.Next(100, 300) : random.Next(150, 500),
            StatusCode = hit429 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK
        };
    }

    private class ApiCallResult
    {
        public bool Hit429 { get; set; }
        public double ResponseTimeMs { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }
}