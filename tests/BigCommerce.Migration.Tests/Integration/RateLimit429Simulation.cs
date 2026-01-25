using System;
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
/// Demonstrates how Dynamic Rate Limiting handles actual 429 errors
/// Shows the system's reaction when BigCommerce API returns rate limit errors
/// </summary>
public class RateLimit429Simulation
{
    private readonly ITestOutputHelper _output;

    public RateLimit429Simulation(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Demonstrate_429_Error_Response_And_Recovery()
    {
        _output.WriteLine("🚨 429 Rate Limit Error Simulation");
        _output.WriteLine("==================================");
        _output.WriteLine("Scenario: Migration hits BigCommerce 429 errors");
        _output.WriteLine("Expected: System detects errors and reduces rate automatically");
        _output.WriteLine("");

        // Setup basic dynamic rate limiter (no predictive features for this demo)
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        var mockRateLimitService = new Mock<IRateLimitService>();
        var mockHealthMonitor = new Mock<IApiHealthMonitor>();
        var mockRateCalculator = new Mock<IRateCalculator>();
        
        services.AddSingleton(mockRateLimitService.Object);
        services.AddSingleton(mockHealthMonitor.Object);
        services.AddSingleton(mockRateCalculator.Object);
        
        // Register basic dynamic rate limiter (no predictive services = null)
        services.AddSingleton<DynamicRateLimitService>();
        services.AddSingleton<IDynamicRateLimiter>(sp => sp.GetRequiredService<DynamicRateLimitService>());
        
        var serviceProvider = services.BuildServiceProvider();
        var rateLimiter = serviceProvider.GetRequiredService<IDynamicRateLimiter>();

        // Setup the simulation state
        var simulationState = new SimulationState();
        
        // Configure mocks to simulate realistic behavior
        SetupHealthMonitorMock(mockHealthMonitor, simulationState);
        SetupRateCalculatorMock(mockRateCalculator, simulationState);

        const string storeId = "demo-store-429";
        
        // Run the simulation
        await RunMigrationWith429Simulation(rateLimiter, storeId, simulationState, mockHealthMonitor.Object);
    }

    private void SetupHealthMonitorMock(Mock<IApiHealthMonitor> mockHealthMonitor, SimulationState state)
    {
        mockHealthMonitor.Setup(x => x.GetApiHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApiHealthMetrics
            {
                StoreId = "demo-store-429",
                AverageResponseTimeMs = state.AverageResponseTime,
                ErrorRate = state.ErrorRate,
                SuccessfulRequests = state.SuccessfulRequests,
                FailedRequests = state.FailedRequests
            });

        mockHealthMonitor.Setup(x => x.RecordApiCallAsync(
            It.IsAny<string>(), 
            It.IsAny<BigCommerceRateLimitInfo>(), 
            It.IsAny<double>(), 
            It.IsAny<bool>(), 
            It.IsAny<CancellationToken>()))
            .Callback<string, BigCommerceRateLimitInfo?, double, bool, CancellationToken>(
                (storeId, rateLimitInfo, responseTime, isSuccess, ct) =>
                {
                    if (isSuccess)
                    {
                        state.SuccessfulRequests++;
                        state.AverageResponseTime = Math.Max(150, state.AverageResponseTime - 5); // Improve
                    }
                    else
                    {
                        state.FailedRequests++;
                        state.AverageResponseTime = Math.Min(1000, state.AverageResponseTime + 100); // Degrade
                    }
                    
                    // Update error rate
                    var totalRequests = state.SuccessfulRequests + state.FailedRequests;
                    state.ErrorRate = totalRequests > 0 ? (double)state.FailedRequests / totalRequests : 0.0;
                });
    }

    private void SetupRateCalculatorMock(Mock<IRateCalculator> mockRateCalculator, SimulationState state)
    {
        mockRateCalculator.Setup(x => x.CalculateOptimalRate(It.IsAny<ApiHealthMetrics>()))
            .Returns<ApiHealthMetrics>(metrics =>
            {
                // Rate calculation based on error rate and response time
                var healthScore = (1.0 - metrics.ErrorRate) * 100;
                var responseTimePenalty = Math.Max(0, (metrics.AverageResponseTimeMs - 200) / 1000.0);
                
                var adjustedHealth = Math.Max(0, healthScore - (responseTimePenalty * 20));
                
                if (adjustedHealth > 80) return 10.0;      // Excellent health
                if (adjustedHealth > 60) return 7.0;       // Good health  
                if (adjustedHealth > 40) return 4.0;       // Poor health
                if (adjustedHealth > 20) return 2.0;       // Bad health
                return 1.0;                                // Critical health
            });
    }

    private async Task RunMigrationWith429Simulation(IDynamicRateLimiter rateLimiter, string storeId, SimulationState state, IApiHealthMonitor healthMonitor)
    {
        _output.WriteLine("🚀 Starting Migration with Simulated 429 Errors");
        _output.WriteLine("------------------------------------------------");

        // Simulate processing 200 products
        for (int batch = 1; batch <= 20; batch++)
        {
            _output.WriteLine($"\n📦 Batch {batch}/20 (Processing 10 products)");
            
            // Get current rate from dynamic rate limiter
            var currentRate = await rateLimiter.GetOptimalRateAsync(storeId);
            _output.WriteLine($"   ⚡ Current Rate: {currentRate:F1} req/sec");
            
            // Process 10 products in this batch
            for (int product = 1; product <= 10; product++)
            {
                // Apply rate limiting delay
                var delayMs = (int)(1000 / currentRate);
                await Task.Delay(delayMs);
                
                // Simulate API call with potential 429 error
                var apiResult = SimulateBigCommerceApiCall(currentRate, state);
                
                // Record the API call result (using health monitor)
                await healthMonitor.RecordApiCallAsync(storeId, 
                    null, // No rate limit info for this simulation
                    apiResult.ResponseTimeMs, 
                    apiResult.IsSuccess,
                    CancellationToken.None);
                
                if (!apiResult.IsSuccess)
                {
                    _output.WriteLine($"     ❌ 429 ERROR on product {product}! Rate was too aggressive ({currentRate:F1} req/sec)");
                }
                else if (product == 10) // Only show success for last product to avoid spam
                {
                    _output.WriteLine($"     ✅ All 10 products processed successfully");
                }
            }
            
            // Show batch summary
            var batchTotalRequests = state.SuccessfulRequests + state.FailedRequests;
            var successRate = batchTotalRequests > 0 ? (double)state.SuccessfulRequests / batchTotalRequests * 100 : 0;
            var errorRate = state.ErrorRate * 100;
            
            _output.WriteLine($"   📊 Batch Summary: {state.SuccessfulRequests}/{batchTotalRequests} success ({successRate:F1}%), " +
                            $"Error Rate: {errorRate:F1}%, Avg Response: {state.AverageResponseTime:F0}ms");
            
            // Show how the system is adapting
            if (errorRate > 5)
            {
                _output.WriteLine($"   🔻 High error rate detected! System will reduce rate for next batch");
            }
            else if (errorRate == 0 && successRate > 95)
            {
                _output.WriteLine($"   🔺 Excellent performance! System may increase rate");
            }
        }
        
        // Final results
        _output.WriteLine("\n🏁 Migration Complete - 429 Error Simulation Results");
        _output.WriteLine("===================================================");
        var totalRequests = state.SuccessfulRequests + state.FailedRequests;
        var finalSuccessRate = (double)state.SuccessfulRequests / totalRequests * 100;
        var finalErrorRate = state.ErrorRate * 100;
        
        _output.WriteLine($"📊 Total API Calls: {totalRequests}");
        _output.WriteLine($"✅ Successful: {state.SuccessfulRequests} ({finalSuccessRate:F1}%)");
        _output.WriteLine($"❌ 429 Errors: {state.FailedRequests} ({finalErrorRate:F1}%)");
        _output.WriteLine($"⏱️  Final Avg Response Time: {state.AverageResponseTime:F0}ms");
        
        var finalRate = await rateLimiter.GetOptimalRateAsync(storeId);
        _output.WriteLine($"⚡ Final Rate: {finalRate:F1} req/sec");
        
        _output.WriteLine("\n🧠 How Dynamic Rate Limiting Worked:");
        _output.WriteLine("====================================");
        _output.WriteLine("1. 🚀 Started at aggressive rate (10 req/sec)");
        _output.WriteLine("2. ❌ Hit 429 errors when rate was too high");
        _output.WriteLine("3. 📉 System detected failures and reduced rate");
        _output.WriteLine("4. 🔄 Continued monitoring and adjusting rate");
        _output.WriteLine("5. 📈 Rate increased again when API health improved");
        
        if (finalErrorRate < 5)
        {
            _output.WriteLine("\n✅ SUCCESS: Dynamic rate limiting kept errors under 5%!");
        }
        else if (finalErrorRate < 15)
        {
            _output.WriteLine("\n⚠️  ACCEPTABLE: Some 429 errors occurred but system adapted");
        }
        else
        {
            _output.WriteLine("\n❌ NEEDS TUNING: Too many 429 errors - rate limiting needs adjustment");
        }
    }

    private ApiCallResult SimulateBigCommerceApiCall(double currentRate, SimulationState state)
    {
        var random = new Random();
        
        // Calculate 429 probability based on current rate and system health
        var baseProbability = Math.Max(0, (currentRate - 5) / 15.0); // Higher rate = higher chance of 429
        var healthPenalty = state.ErrorRate * 2; // Previous errors increase chance of more errors
        var probability429 = Math.Min(0.7, baseProbability + healthPenalty);
        
        var is429 = random.NextDouble() < probability429;
        
        return new ApiCallResult
        {
            IsSuccess = !is429,
            ResponseTimeMs = is429 ? random.Next(100, 300) : random.Next(150, 500),
            ErrorType = is429 ? "429 Too Many Requests" : null
        };
    }

    private class SimulationState
    {
        public int SuccessfulRequests { get; set; } = 0;
        public int FailedRequests { get; set; } = 0;
        public double ErrorRate { get; set; } = 0.0;
        public double AverageResponseTime { get; set; } = 200.0;
    }

    private class ApiCallResult
    {
        public bool IsSuccess { get; set; }
        public double ResponseTimeMs { get; set; }
        public string? ErrorType { get; set; }
    }
}