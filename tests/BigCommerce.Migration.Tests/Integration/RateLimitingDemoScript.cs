using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Infrastructure.Extensions;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// Demo script that demonstrates Enhanced Dynamic Rate Limiting in a realistic migration scenario
/// This test can be run in Docker to show real-world rate limiting behavior
/// </summary>
public class RateLimitingDemoScript
{
    private readonly ITestOutputHelper _output;

    public RateLimitingDemoScript(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DemonstrateEnhancedRateLimiting_RealWorldScenario()
    {
        _output.WriteLine("🚀 Enhanced Dynamic Rate Limiting Demonstration");
        _output.WriteLine("==============================================");
        _output.WriteLine("");

        // Setup services like they would be in the real application
        var services = new ServiceCollection();
        var configuration = BuildTestConfiguration();
        
        // Add rate limiting services (same as production)
        services.AddPredictiveRateLimiting();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Get the enhanced rate limiter
        var rateLimiter = serviceProvider.GetRequiredService<IEnhancedDynamicRateLimiter>();
        var logger = serviceProvider.GetRequiredService<ILogger<RateLimitingDemoScript>>();

        await RunMigrationSimulation(rateLimiter, logger);
    }

    private async Task RunMigrationSimulation(IEnhancedDynamicRateLimiter rateLimiter, ILogger logger)
    {
        const string storeId = "demo-store-123";
        const int totalProducts = 500;
        var processed = 0;
        var rateLimitHits = 0;
        var totalRequests = 0;
        
        var stopwatch = Stopwatch.StartNew();
        
        _output.WriteLine($"📦 Migrating {totalProducts} products from store: {storeId}");
        _output.WriteLine($"🎯 Goal: Zero 429 errors through intelligent rate limiting");
        _output.WriteLine("");

        while (processed < totalProducts)
        {
            try
            {
                // Step 1: Get optimal rate from enhanced rate limiter
                var optimalRate = await rateLimiter.GetOptimalRateAsync(storeId);
                var batchSize = Math.Min(25, totalProducts - processed);
                
                _output.WriteLine($"⚡ Optimal Rate: {optimalRate:F1}/sec, Processing batch of {batchSize} products...");

                // Step 2: Simulate API calls to BigCommerce
                var batchResults = await SimulateProductMigrationBatch(storeId, batchSize, optimalRate);
                
                // Step 3: Process results and update rate limiter
                foreach (var result in batchResults)
                {
                    totalRequests++;
                    
                    if (result.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        rateLimitHits++;
                        _output.WriteLine($"⚠️  429 Rate Limit Hit! (Total: {rateLimitHits})");
                        
                        // Record the failure - enhanced rate limiter will adapt
                        await rateLimiter.RecordApiCallAsync(storeId, result.ResponseTime, false);
                        
                        // Backoff strategy
                        var backoffMs = Math.Min(5000, rateLimitHits * 1000);
                        _output.WriteLine($"🛑 Backing off for {backoffMs}ms...");
                        await Task.Delay(backoffMs);
                    }
                    else if (result.StatusCode == HttpStatusCode.OK)
                    {
                        processed++;
                        await rateLimiter.RecordApiCallAsync(storeId, result.ResponseTime, true);
                        
                        if (processed % 50 == 0)
                        {
                            var currentRate = processed / stopwatch.Elapsed.TotalSeconds;
                            _output.WriteLine($"✅ Progress: {processed}/{totalProducts} ({currentRate:F1}/sec avg)");
                        }
                    }
                }

                // Step 4: Adaptive delay based on current conditions
                var delayMs = CalculateSmartDelay(optimalRate, rateLimitHits);
                if (delayMs > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delayMs));
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"❌ Error: {ex.Message}");
                await Task.Delay(1000); // Error recovery delay
            }
        }
        
        stopwatch.Stop();
        
        // Final Results
        _output.WriteLine("");
        _output.WriteLine("🏁 Migration Complete!");
        _output.WriteLine("===================");
        _output.WriteLine($"📊 Products Migrated: {processed}/{totalProducts}");
        _output.WriteLine($"⏱️  Total Time: {stopwatch.Elapsed:hh\\:mm\\:ss}");
        _output.WriteLine($"🚀 Average Rate: {processed / stopwatch.Elapsed.TotalSeconds:F2}/sec");
        _output.WriteLine($"📈 Total API Calls: {totalRequests}");
        _output.WriteLine($"⚠️  429 Rate Limits: {rateLimitHits}");
        _output.WriteLine($"🎯 Success Rate: {((double)(totalRequests - rateLimitHits) / totalRequests):P1}");
        
        var errorRate = (double)rateLimitHits / totalRequests;
        if (errorRate < 0.05)
        {
            _output.WriteLine("");
            _output.WriteLine("🎉 EXCELLENT! Enhanced rate limiting kept 429 errors under 5%");
        }
        else if (errorRate < 0.10)
        {
            _output.WriteLine("");
            _output.WriteLine("✅ GOOD! Enhanced rate limiting performance within acceptable range");
        }
        else
        {
            _output.WriteLine("");
            _output.WriteLine("⚠️  High error rate detected - may need configuration tuning");
        }
    }

    private async Task<List<ApiCallResult>> SimulateProductMigrationBatch(string storeId, int batchSize, double currentRate)
    {
        var results = new List<ApiCallResult>();
        var random = new Random();
        
        // Simulate the time it takes to process a batch
        var processingTimeMs = (int)(batchSize / currentRate * 1000);
        
        for (int i = 0; i < batchSize; i++)
        {
            var responseTime = TimeSpan.FromMilliseconds(random.Next(100, 800));
            
            // Simulate BigCommerce API behavior
            // Higher chance of 429 if rate is too aggressive
            var rateLimitProbability = Math.Max(0, (currentRate - 15.0) / 50.0); // Increases above 15/sec
            var hit429 = random.NextDouble() < rateLimitProbability;
            
            results.Add(new ApiCallResult
            {
                StatusCode = hit429 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK,
                ResponseTime = responseTime,
                QuotaInfo = GenerateMockQuotaInfo(storeId, hit429)
            });
            
            // Small delay between individual API calls in the batch
            await Task.Delay(Math.Max(10, processingTimeMs / batchSize));
        }
        
        return results;
    }

    private BigCommerceRateLimitInfo GenerateMockQuotaInfo(string storeId, bool justHit429)
    {
        var random = new Random();
        
        // Simulate quota depletion over time
        var baseQuota = 1000;
        var remainingQuota = justHit429 ? 0 : random.Next(50, 800);
        
        return new BigCommerceRateLimitInfo
        {
            StoreId = storeId,
            RequestsQuota = baseQuota,
            RequestsLeft = remainingQuota,
            TimeResetMs = random.Next(60000, 300000), // 1-5 minutes
            TimeWindowMs = 300000 // 5 minute window
        };
    }

    private static int CalculateSmartDelay(double currentRate, int recentRateLimitHits)
    {
        // Smart delay calculation based on current conditions
        var baseDelayMs = (int)(1000 / Math.Max(1, currentRate));
        
        // Increase delay if we've been hitting rate limits
        var rateLimitPenalty = recentRateLimitHits * 200;
        
        // Add some randomization to avoid thundering herd
        var jitter = new Random().Next(-50, 50);
        
        return Math.Max(0, Math.Min(3000, baseDelayMs + rateLimitPenalty + jitter));
    }

    private IConfiguration BuildTestConfiguration()
    {
        var configData = new Dictionary<string, string>
        {
            // Rate limiting configuration matching production
            {"DynamicRateLimiting:Features:EnablePredictiveDistribution", "true"},
            {"DynamicRateLimiting:Features:EnableInstanceCoordination", "true"},
            {"DynamicRateLimiting:Features:EnableQuotaTracking", "true"},
            {"DynamicRateLimiting:Features:EnableDetailedLogging", "true"},
            
            // Predictive settings (matching Docker configuration)
            {"DynamicRateLimiting:Predictive:SafetyBufferPercentage", "0.25"},
            {"DynamicRateLimiting:Predictive:HealthyQuotaThreshold", "0.3"},
            {"DynamicRateLimiting:Predictive:CriticalQuotaThreshold", "0.1"},
            {"DynamicRateLimiting:Predictive:TokenExpirySeconds", "20"},
            {"DynamicRateLimiting:Predictive:HeartbeatIntervalSeconds", "45"},
            {"DynamicRateLimiting:Predictive:CoordinationHealthCheckSeconds", "20"},
            
            // Rate calculation settings
            {"DynamicRateLimiting:RateCalculation:MinimumRate", "5.0"},
            {"DynamicRateLimiting:RateCalculation:MaximumRate", "25.0"},
            {"DynamicRateLimiting:RateCalculation:DefaultRate", "12.0"},
            {"DynamicRateLimiting:RateCalculation:AggressivenessFactor", "0.85"},
            
            // Storage settings (using development storage)
            {"AzureWebJobsStorage", "UseDevelopmentStorage=true"}
        };
        
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    public class ApiCallResult
    {
        public HttpStatusCode StatusCode { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public BigCommerceRateLimitInfo? QuotaInfo { get; set; }
    }
}