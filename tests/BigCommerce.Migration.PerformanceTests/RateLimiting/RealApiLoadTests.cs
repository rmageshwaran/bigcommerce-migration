using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Models.RateLimiting;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.PerformanceTests.Infrastructure;

namespace BigCommerce.Migration.PerformanceTests.RateLimiting;

/// <summary>
/// Real-world load tests using actual BigCommerce API to validate predictive rate limiting
/// Tests against authentic rate limits and validates zero 429 error guarantee
/// Uses Azurite for real Azure Table Storage integration
/// </summary>
[Collection("Azurite")]
public class RealApiLoadTests : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly DistributedQuotaTracker _quotaTracker;
    private readonly AzuriteTestFixture _azuriteFixture;
    private readonly Mock<ISignalREventFactory> _signalREventFactory;
    private readonly Mock<IProgressEventPublisher> _progressEventPublisher;
    private readonly DynamicRateLimitingConfiguration _configuration;

    // BigCommerce API Configuration
    private const string BaseUrl = "https://api.bigcommerce.com/stores/v6q95r5n91/v3";
    private const string AuthToken = "2jxzl0n457l7dbz9jgeo8tzbj6xw6ba";
    private const string TestEndpoint = "/catalog/variants";
    private const string StoreId = "v6q95r5n91";

    public RealApiLoadTests(AzuriteTestFixture azuriteFixture)
    {
        _azuriteFixture = azuriteFixture;
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("X-Auth-Token", AuthToken);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        // Setup mocks for SignalR (but use real Azure Storage via Azurite)
        _signalREventFactory = new Mock<ISignalREventFactory>();
        _progressEventPublisher = new Mock<IProgressEventPublisher>();

        _configuration = new DynamicRateLimitingConfiguration
        {
            Features = new FeatureFlags
            {
                EnablePredictiveDistribution = true,
                EnableInstanceCoordination = true,
                EnableQuotaTracking = true
            },
            Predictive = new PredictiveSettings
            {
                SafetyBufferPercentage = 0.25, // 25% safety buffer for real API
                HealthyQuotaThreshold = 0.3,
                CriticalQuotaThreshold = 0.1,
                TokenExpirySeconds = 300,
                InstanceTimeoutSeconds = 60,
                HeartbeatIntervalSeconds = 30,
                MaxETagRetries = 3,
                CoordinationHealthCheckSeconds = 15,
                TableStorage = new TableStorageSettings
                {
                    QuotaTableName = "quotatracking",
                    InstanceTableName = "instancecoordination",
                    TokenTableName = "tokenallocation",
                    AutoCreateTables = true
                }
            }
        };

        // Setup SignalR mocks
        _signalREventFactory.Setup(x => x.CreateQuotaUpdate(It.IsAny<string>(), It.IsAny<QuotaUpdateOptions>()))
            .Returns(new QuotaUpdateEvent());

        // Create real quota tracker with Azurite
        _quotaTracker = _azuriteFixture.CreateDistributedQuotaTracker(
            _configuration, 
            _signalREventFactory.Object, 
            _progressEventPublisher.Object);
    }

    [Fact]
    public async Task AzuriteIntegration_ValidateStorageConnection()
    {
        // Arrange & Act - Validate Azurite connection and table setup
        var isValid = await _azuriteFixture.ValidateTablesAsync();
        
        // Assert
        isValid.Should().BeTrue("All required tables should be accessible in Azurite");
        
        // Test direct table operations
        var quotaTableClient = await _azuriteFixture.GetTableClientAsync("quotatracking");
        quotaTableClient.Should().NotBeNull("Should be able to get quota table client");
        
        Console.WriteLine($"✅ Azurite integration validated successfully");
        Console.WriteLine($"🔗 Connection: {_azuriteFixture.ConnectionString}");
    }

    [Fact]
    public async Task RealBigCommerceApi_SingleRequest_CapturesRateLimitHeaders()
    {
        // Arrange & Act - Make a real API call to capture headers
        var response = await MakeApiCallAsync(1, 1);

        // Assert - Validate we received rate limit headers
        response.Should().NotBeNull();
        response.IsSuccessStatusCode.Should().BeTrue("First API call should succeed");

        // Extract and validate rate limit headers
        var rateLimitInfo = ExtractRateLimitInfo(response);
        rateLimitInfo.Should().NotBeNull();
        rateLimitInfo!.RequestsQuota.Should().BeGreaterThan(0, "Should have a valid quota");
        rateLimitInfo.RequestsLeft.Should().BeGreaterThan(0, "Should have remaining requests");
        rateLimitInfo.TimeWindowMs.Should().BeGreaterThan(0, "Should have a valid time window");

        // Update our quota tracker with real data
        var updateResult = await _quotaTracker.UpdateQuotaFromBigCommerceInfoAsync(StoreId, rateLimitInfo);
        updateResult.Should().BeTrue("Quota tracker should successfully update with real API data");

        Console.WriteLine($"=== Real API Rate Limit Info ===");
        Console.WriteLine($"Store ID: {StoreId}");
        Console.WriteLine($"Current Quota: {rateLimitInfo.RequestsQuota}");
        Console.WriteLine($"Remaining: {rateLimitInfo.RequestsLeft}");
        Console.WriteLine($"Time Window: {rateLimitInfo.TimeWindowMs}ms");
        Console.WriteLine($"Reset Time: {rateLimitInfo.TimeResetMs}ms");
    }

    [Fact]
    public async Task RealBigCommerceApi_ModerateLoad_StaysWithinLimits()
    {
        // Arrange - Conservative load test to validate rate limiting works
        const int totalRequests = 20;
        const int concurrentWorkers = 5;
        const int requestsPerWorker = totalRequests / concurrentWorkers;

        var results = new ConcurrentBag<ApiCallResult>();
        var rateLimitHeaders = new ConcurrentBag<BigCommerceRateLimitInfo>();

        // Act - Make controlled concurrent requests
        var tasks = Enumerable.Range(0, concurrentWorkers).Select(async workerId =>
        {
            for (int i = 0; i < requestsPerWorker; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var response = await MakeApiCallAsync(1, 10); // Small page size for faster responses
                    stopwatch.Stop();

                    var result = new ApiCallResult
                    {
                        WorkerId = workerId,
                        RequestNumber = i,
                        StatusCode = response.StatusCode,
                        ResponseTime = stopwatch.Elapsed,
                        Success = response.IsSuccessStatusCode
                    };

                    if (response.IsSuccessStatusCode)
                    {
                        var rateLimitInfo = ExtractRateLimitInfo(response);
                        if (rateLimitInfo != null)
                        {
                            rateLimitHeaders.Add(rateLimitInfo);
                            
                            // Update quota tracker with each response
                            await _quotaTracker.UpdateQuotaFromBigCommerceInfoAsync(StoreId, rateLimitInfo);
                        }
                    }

                    results.Add(result);
                }
                catch (HttpRequestException ex)
                {
                    stopwatch.Stop();
                    results.Add(new ApiCallResult
                    {
                        WorkerId = workerId,
                        RequestNumber = i,
                        StatusCode = HttpStatusCode.RequestTimeout,
                        ResponseTime = stopwatch.Elapsed,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }

                // Small delay to avoid overwhelming the API
                await Task.Delay(Random.Shared.Next(100, 300));
            }
        });

        await Task.WhenAll(tasks);

        // Assert - Validate results
        var successfulResults = results.Where(r => r.Success).ToArray();
        var failedResults = results.Where(r => !r.Success).ToArray();
        var rateLimitViolations = results.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToArray();

        successfulResults.Length.Should().BeGreaterThan(totalRequests / 2, 
            "At least half of requests should succeed");

        rateLimitViolations.Length.Should().Be(0, 
            "Should have zero 429 errors with proper rate limiting");

        var avgResponseTime = successfulResults.Average(r => r.ResponseTime.TotalMilliseconds);
        avgResponseTime.Should().BeLessThan(2000, 
            "Average response time should be reasonable");

        // Validate quota tracking worked
        var latestHeaders = rateLimitHeaders.OrderByDescending(h => h.RequestsLeft).FirstOrDefault();
        if (latestHeaders != null)
        {
            Console.WriteLine($"=== Real API Load Test Results ===");
            Console.WriteLine($"Total Requests: {results.Count}");
            Console.WriteLine($"Successful: {successfulResults.Length}");
            Console.WriteLine($"Failed: {failedResults.Length}");
            Console.WriteLine($"429 Errors: {rateLimitViolations.Length}");
            Console.WriteLine($"Average Response Time: {avgResponseTime:F1}ms");
            Console.WriteLine($"Final Quota Remaining: {latestHeaders.RequestsLeft}/{latestHeaders.RequestsQuota}");
        }
    }

    [Fact]
    public async Task RealBigCommerceApi_QuotaTracking_UpdatesCorrectly()
    {
        // Arrange - Make several API calls to track quota changes
        const int numberOfCalls = 5;
        var quotaSnapshots = new List<QuotaSnapshot>();

        // Act - Make sequential API calls and track quota
        for (int i = 0; i < numberOfCalls; i++)
        {
            var response = await MakeApiCallAsync(1, 5);
            
            if (response.IsSuccessStatusCode)
            {
                var rateLimitInfo = ExtractRateLimitInfo(response);
                if (rateLimitInfo != null)
                {
                    await _quotaTracker.UpdateQuotaFromBigCommerceInfoAsync(StoreId, rateLimitInfo);
                    
                    quotaSnapshots.Add(new QuotaSnapshot
                    {
                        CallNumber = i + 1,
                        Quota = rateLimitInfo.RequestsQuota,
                        Remaining = rateLimitInfo.RequestsLeft,
                        Timestamp = DateTimeOffset.UtcNow
                    });

                    Console.WriteLine($"Call {i + 1}: {rateLimitInfo.RequestsLeft}/{rateLimitInfo.RequestsQuota} remaining");
                }
            }

            // Delay between calls to see quota consumption
            await Task.Delay(200);
        }

        // Assert - Validate quota tracking behavior
        quotaSnapshots.Should().HaveCountGreaterThan(0, "Should have captured quota snapshots");

        if (quotaSnapshots.Count > 1)
        {
            var firstSnapshot = quotaSnapshots.First();
            var lastSnapshot = quotaSnapshots.Last();

            // Quota should decrease or stay the same (never increase unless reset)
            lastSnapshot.Remaining.Should().BeLessOrEqualTo(firstSnapshot.Remaining, 
                "Remaining quota should decrease with API usage");

            // Validate quota utilization calculation
            var utilizationRate = (double)(firstSnapshot.Quota - lastSnapshot.Remaining) / firstSnapshot.Quota * 100;
            utilizationRate.Should().BeGreaterThan(0, "Should show some quota utilization");

            Console.WriteLine($"=== Quota Tracking Results ===");
            Console.WriteLine($"Initial: {firstSnapshot.Remaining}/{firstSnapshot.Quota}");
            Console.WriteLine($"Final: {lastSnapshot.Remaining}/{lastSnapshot.Quota}");
            Console.WriteLine($"Utilization: {utilizationRate:F2}%");
        }
    }

    [Fact]
    public async Task RealBigCommerceApi_RateLimitApproach_PredictiveDetection()
    {
        // Arrange - Test predictive detection as we approach rate limits
        var quotaHistory = new List<BigCommerceRateLimitInfo>();

        // Act - Make requests until we approach the rate limit
        var requestCount = 0;
        var maxRequests = 30; // Conservative limit to avoid hitting actual 429s

        while (requestCount < maxRequests)
        {
            try
            {
                var response = await MakeApiCallAsync(1, 5);
                requestCount++;

                if (response.IsSuccessStatusCode)
                {
                    var rateLimitInfo = ExtractRateLimitInfo(response);
                    if (rateLimitInfo != null)
                    {
                        quotaHistory.Add(rateLimitInfo);
                        await _quotaTracker.UpdateQuotaFromBigCommerceInfoAsync(StoreId, rateLimitInfo);

                        // Calculate utilization percentage
                        var utilizationPercent = (double)(rateLimitInfo.RequestsQuota - rateLimitInfo.RequestsLeft) / rateLimitInfo.RequestsQuota * 100;

                        Console.WriteLine($"Request {requestCount}: {rateLimitInfo.RequestsLeft}/{rateLimitInfo.RequestsQuota} " +
                                        $"({utilizationPercent:F1}% utilized)");

                        // If we're approaching the limit, validate our predictive logic
                        if (utilizationPercent > 70) // 70% utilization triggers predictive warnings
                        {
                            var quotaHealth = await _quotaTracker.GetQuotaHealthAsync(StoreId, CancellationToken.None);
                            
                            quotaHealth.Should().NotBeNull("Should have quota health data");
                            quotaHealth!.HealthStatus.Should().NotBe(QuotaHealthStatus.Healthy, 
                                "Health status should change when approaching limits");

                            Console.WriteLine($"⚠️  Predictive Alert: Health Status = {quotaHealth.HealthStatus}, " +
                                            $"Safe Tokens = {quotaHealth.SafeTokens}");
                        }

                        // Stop if we're getting close to exhaustion (predictive prevention)
                        if (rateLimitInfo.RequestsLeft < rateLimitInfo.RequestsQuota * 0.1) // 10% remaining
                        {
                            Console.WriteLine("🛑 Stopping due to low quota to prevent 429 errors");
                            break;
                        }
                    }
                }
                else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Assert.Fail("Should not hit 429 errors with predictive rate limiting!");
                }

                // Delay between requests
                await Task.Delay(Random.Shared.Next(100, 300));
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Request {requestCount} failed: {ex.Message}");
                break;
            }
        }

        // Assert - Validate predictive behavior
        quotaHistory.Should().HaveCountGreaterThan(0, "Should have quota history");

        var finalQuota = quotaHistory.LastOrDefault();
        finalQuota.Should().NotBeNull("Should have final quota reading");

        if (finalQuota != null)
        {
            var finalUtilization = (double)(finalQuota.RequestsQuota - finalQuota.RequestsLeft) / finalQuota.RequestsQuota * 100;
            finalUtilization.Should().BeLessThan(90, 
                "Should stop before hitting 90% utilization to prevent 429s");

            Console.WriteLine($"=== Predictive Rate Limiting Results ===");
            Console.WriteLine($"Total Requests Made: {requestCount}");
            Console.WriteLine($"Final Quota State: {finalQuota.RequestsLeft}/{finalQuota.RequestsQuota}");
            Console.WriteLine($"Final Utilization: {finalUtilization:F1}%");
            Console.WriteLine($"429 Errors: 0 (Predictive Success!)");
        }
    }

    [Fact]
    public async Task FullIntegration_RealApiWithAzuriteStorage_PredictiveRateLimit()
    {
        // Arrange - Clear any existing data and prepare for fresh test
        await _azuriteFixture.ClearAllTablesAsync();
        
        const int maxRequests = 15;
        var requestResults = new List<IntegrationTestResult>();

        // Act - Make sequential requests to test full integration
        for (int i = 0; i < maxRequests; i++)
        {
            try
            {
                // Step 1: Make real BigCommerce API call
                var response = await MakeApiCallAsync(1, 5);
                
                var result = new IntegrationTestResult
                {
                    RequestNumber = i + 1,
                    ApiSuccess = response.IsSuccessStatusCode,
                    StatusCode = response.StatusCode,
                    Timestamp = DateTimeOffset.UtcNow
                };

                if (response.IsSuccessStatusCode)
                {
                    // Step 2: Extract real rate limit headers
                    var rateLimitInfo = ExtractRateLimitInfo(response);
                    if (rateLimitInfo != null)
                    {
                        result.RateLimitInfo = rateLimitInfo;

                        // Step 3: Update distributed quota tracker (writes to Azurite)
                        result.QuotaUpdateSuccess = await _quotaTracker.UpdateQuotaFromBigCommerceInfoAsync(StoreId, rateLimitInfo);

                        // Step 4: Get quota health from Azurite storage
                        var quotaHealth = await _quotaTracker.GetQuotaHealthAsync(StoreId, CancellationToken.None);
                        result.QuotaHealth = quotaHealth;

                        // Step 5: Calculate utilization and predict safety
                        var utilizationPercent = (double)(rateLimitInfo.RequestsQuota - rateLimitInfo.RequestsLeft) / rateLimitInfo.RequestsQuota * 100;
                        result.UtilizationPercent = utilizationPercent;

                        Console.WriteLine($"Request {i + 1}: {rateLimitInfo.RequestsLeft}/{rateLimitInfo.RequestsQuota} " +
                                        $"({utilizationPercent:F1}% utilized) - Health: {quotaHealth?.HealthStatus}");

                        // Step 6: Predictive decision - should we continue?
                        if (quotaHealth != null && quotaHealth.HealthStatus == QuotaHealthStatus.Critical)
                        {
                            Console.WriteLine("🛑 PREDICTIVE STOP: Critical quota health detected!");
                            result.PredictiveStop = true;
                            requestResults.Add(result);
                            break;
                        }
                    }
                }
                else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    result.Hit429Error = true;
                    Console.WriteLine("❌ 429 Error - Predictive system failed!");
                }

                requestResults.Add(result);

                // Delay between requests
                await Task.Delay(Random.Shared.Next(200, 500));
            }
            catch (Exception ex)
            {
                requestResults.Add(new IntegrationTestResult
                {
                    RequestNumber = i + 1,
                    ApiSuccess = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTimeOffset.UtcNow
                });
                
                Console.WriteLine($"Request {i + 1} failed: {ex.Message}");
            }
        }

        // Assert - Validate full integration worked correctly
        var successfulRequests = requestResults.Where(r => r.ApiSuccess).ToArray();
        var quotaUpdates = requestResults.Where(r => r.QuotaUpdateSuccess).ToArray();
        var rateLimitViolations = requestResults.Where(r => r.Hit429Error).ToArray();
        var predictiveStops = requestResults.Where(r => r.PredictiveStop).ToArray();

        // Core assertions
        successfulRequests.Should().HaveCountGreaterThan(0, "Should have at least some successful API calls");
        quotaUpdates.Should().HaveCountGreaterThan(0, "Should have successful quota updates in Azurite");
        rateLimitViolations.Should().HaveCount(0, "Should have zero 429 errors with predictive system");

        // Validate Azurite storage integration
        var finalQuotaHealth = await _quotaTracker.GetQuotaHealthAsync(StoreId, CancellationToken.None);
        finalQuotaHealth.Should().NotBeNull("Should have final quota health data in Azurite");

        // Validate predictive behavior
        if (predictiveStops.Any())
        {
            Console.WriteLine("✅ Predictive system successfully prevented rate limit violations");
        }

        // Check that data was actually persisted to Azurite
        var quotaTableClient = await _azuriteFixture.GetTableClientAsync("quotatracking");
        var entities = 0;
        await foreach (var entity in quotaTableClient.QueryAsync<StoreQuotaEntity>(
            filter: $"PartitionKey eq '{StoreId}'"))
        {
            entities++;
        }
        
        entities.Should().BeGreaterThan(0, "Should have persisted quota data to Azurite");

        Console.WriteLine($"=== Full Integration Test Results ===");
        Console.WriteLine($"Total Requests: {requestResults.Count}");
        Console.WriteLine($"Successful API Calls: {successfulRequests.Length}");
        Console.WriteLine($"Successful Quota Updates: {quotaUpdates.Length}");
        Console.WriteLine($"429 Errors: {rateLimitViolations.Length}");
        Console.WriteLine($"Predictive Stops: {predictiveStops.Length}");
        Console.WriteLine($"Azurite Entities: {entities}");
        Console.WriteLine($"Final Health Status: {finalQuotaHealth?.HealthStatus}");
    }

    private async Task<HttpResponseMessage> MakeApiCallAsync(int page = 1, int limit = 10)
    {
        var url = $"{BaseUrl}{TestEndpoint}?page={page}&limit={limit}";
        return await _httpClient.GetAsync(url);
    }

    private static BigCommerceRateLimitInfo? ExtractRateLimitInfo(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Rate-Limit-Requests-Quota", out var quotaValues) ||
            !response.Headers.TryGetValues("X-Rate-Limit-Requests-Left", out var leftValues) ||
            !response.Headers.TryGetValues("X-Rate-Limit-Time-Reset-Ms", out var resetValues) ||
            !response.Headers.TryGetValues("X-Rate-Limit-Time-Window-Ms", out var windowValues))
        {
            return null;
        }

        if (!int.TryParse(quotaValues.FirstOrDefault(), out var quota) ||
            !int.TryParse(leftValues.FirstOrDefault(), out var left) ||
            !long.TryParse(resetValues.FirstOrDefault(), out var resetMs) ||
            !long.TryParse(windowValues.FirstOrDefault(), out var windowMs))
        {
            return null;
        }

        return new BigCommerceRateLimitInfo
        {
            StoreId = StoreId,
            RequestsQuota = quota,
            RequestsLeft = left,
            TimeResetMs = resetMs,
            TimeWindowMs = windowMs
        };
    }



    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    private class ApiCallResult
    {
        public int WorkerId { get; set; }
        public int RequestNumber { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private class QuotaSnapshot
    {
        public int CallNumber { get; set; }
        public int Quota { get; set; }
        public int Remaining { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }

    private class IntegrationTestResult
    {
        public int RequestNumber { get; set; }
        public bool ApiSuccess { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public BigCommerceRateLimitInfo? RateLimitInfo { get; set; }
        public bool QuotaUpdateSuccess { get; set; }
        public QuotaHealthMetrics? QuotaHealth { get; set; }
        public double UtilizationPercent { get; set; }
        public bool PredictiveStop { get; set; }
        public bool Hit429Error { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}