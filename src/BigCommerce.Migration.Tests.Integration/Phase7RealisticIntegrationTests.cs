using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Tests.Integration
{
    /// <summary>
    /// Phase 7: Realistic Integration Tests for RowNumber Pagination System
    /// Tests realistic migration scenarios with appropriate concurrency levels
    /// </summary>
    public class Phase7RealisticIntegrationTests : IClassFixture<IntegrationTestFixture>
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly ILogger<Phase7RealisticIntegrationTests> _logger;
        private readonly IRowNumberService _rowNumberService;
        private readonly IMigrationStorageService _storageService;
        private readonly IEntityMappingsPaginationService _paginationService;

        public Phase7RealisticIntegrationTests(IntegrationTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            _logger = _fixture.ServiceProvider.GetRequiredService<ILogger<Phase7RealisticIntegrationTests>>();
            _rowNumberService = _fixture.ServiceProvider.GetRequiredService<IRowNumberService>();
            _storageService = _fixture.ServiceProvider.GetRequiredService<IMigrationStorageService>();
            _paginationService = _fixture.ServiceProvider.GetRequiredService<IEntityMappingsPaginationService>();
        }

        [Fact]
        [Trait("Category", "Phase7Real")]
        [Trait("Task", "7.1.1")]
        public async Task EndToEnd_1000Products_CompletesSuccessfully()
        {
            // Arrange
            const int productCount = 1000;
            var migrationId = $"phase7-real-{Guid.NewGuid():N}";
            
            var initialMemory = GC.GetTotalMemory(true);
            var stopwatch = Stopwatch.StartNew();
            
            _output.WriteLine($"🧪 [REALISTIC-TEST] End-to-end integration test: {productCount:N0} products");
            _output.WriteLine($"📊 Migration ID: {migrationId}");
            _output.WriteLine($"📊 Initial memory: {initialMemory / (1024 * 1024):F1} MB");

            try
            {
                // Step 1: Test RowNumber Service Sequential Assignment
                _output.WriteLine($"🔢 Step 1: Testing sequential RowNumber assignment...");
                await ValidateSequentialAssignment(migrationId, 10);

                // Step 2: Test Range Allocation (Realistic Batch Sizes)
                _output.WriteLine($"📦 Step 2: Testing range allocation for batches...");
                await ValidateRangeAllocation(migrationId, 5, 100); // 5 batches of 100 each

                // Step 3: Create EntityMappings with Realistic Batching
                _output.WriteLine($"📝 Step 3: Creating {productCount} EntityMappings in realistic batches...");
                var entityMappings = await CreateEntityMappingsRealistic(migrationId, productCount, batchSize: 50);

                // Step 4: Test Range-Based Pagination Queries
                _output.WriteLine($"📄 Step 4: Testing range-based pagination queries...");
                await ValidateRangeBasedPaginationRealistic(migrationId, entityMappings.Count);

                // Step 5: Test Realistic Parallel Access (Like Real Migration)
                _output.WriteLine($"🔄 Step 5: Testing realistic parallel access (5 concurrent workers)...");
                await ValidateRealisticParallelAccess(migrationId, 5, 20); // 5 workers, 20 requests each

                stopwatch.Stop();
                var finalMemory = GC.GetTotalMemory(true);
                var memoryGrowth = (finalMemory - initialMemory) / (1024 * 1024);

                // Final Validation
                Assert.True(memoryGrowth < 50, $"Memory growth {memoryGrowth:F1} MB exceeds 50 MB limit");

                _output.WriteLine($"🎉 Realistic integration test completed successfully!");
                _output.WriteLine($"📊 Total processing time: {stopwatch.Elapsed.TotalSeconds:F1} seconds");
                _output.WriteLine($"📊 Memory growth: {memoryGrowth:F1} MB");
                _output.WriteLine($"📊 Effective throughput: {productCount / stopwatch.Elapsed.TotalSeconds:F0} entities/second");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Realistic integration test failed");
                _output.WriteLine($"❌ Test failed: {ex.Message}");
                throw;
            }
        }

        [Fact]
        [Trait("Category", "Phase7Real")]
        [Trait("Task", "7.2.1")]
        public async Task RealisticPerformance_ModerateLoad_MeetsRequirements()
        {
            // Arrange - Realistic concurrency (like actual migration pipeline)
            const int concurrentWorkers = 10; // Realistic parallel chunks
            const int requestsPerWorker = 25;  // Realistic batch sizes
            var migrationId = $"perf-real-{Guid.NewGuid():N}";

            _output.WriteLine($"⚡ [REALISTIC-PERF] Testing realistic performance: {concurrentWorkers} workers × {requestsPerWorker} requests");

            var allRowNumbers = new System.Collections.Concurrent.ConcurrentBag<long>();
            var successfulRequests = 0;
            var failedRequests = 0;
            var totalLatency = 0.0;

            var stopwatch = Stopwatch.StartNew();

            // Act - Simulate realistic migration pattern
            var tasks = new List<Task>();
            for (int worker = 0; worker < concurrentWorkers; worker++)
            {
                int workerId = worker;
                tasks.Add(Task.Run(async () =>
                {
                    for (int request = 0; request < requestsPerWorker; request++)
                    {
                        var requestStopwatch = Stopwatch.StartNew();
                        try
                        {
                            var rowNumber = await _rowNumberService.GetNextRowNumberAsync(migrationId, "products");
                            allRowNumbers.Add(rowNumber);
                            
                            Interlocked.Increment(ref successfulRequests);
                            lock (stopwatch)
                            {
                                totalLatency += requestStopwatch.Elapsed.TotalMilliseconds;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            Interlocked.Increment(ref failedRequests);
                            // In realistic scenarios, we'd retry or use range allocation instead
                        }
                        
                        // Realistic delay between requests (simulating processing time)
                        await Task.Delay(10); // 10ms between requests per worker
                    }
                }));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert - Realistic performance targets
            var totalRequests = concurrentWorkers * requestsPerWorker;
            var successRate = (double)successfulRequests / totalRequests * 100;
            var averageLatency = totalLatency / Math.Max(successfulRequests, 1);
            var requestsPerSecond = successfulRequests / stopwatch.Elapsed.TotalSeconds;

            // Validate realistic expectations
            Assert.True(successRate >= 80, $"Success rate {successRate:F1}% should be at least 80% for realistic load");
            Assert.True(averageLatency < 500, $"Average latency {averageLatency:F0}ms should be under 500ms for realistic load");
            Assert.True(requestsPerSecond >= 10, $"Should handle at least 10 requests/second in realistic scenarios");

            // Validate uniqueness of successful assignments
            var rowNumberList = allRowNumbers.ToList();
            var uniqueCount = rowNumberList.Distinct().Count();
            Assert.Equal(successfulRequests, uniqueCount); // All successful assignments must be unique

            _output.WriteLine($"✅ Realistic performance test results:");
            _output.WriteLine($"   Total requests: {totalRequests}");
            _output.WriteLine($"   Successful: {successfulRequests} ({successRate:F1}%)");
            _output.WriteLine($"   Failed: {failedRequests}");
            _output.WriteLine($"   Average latency: {averageLatency:F0}ms");
            _output.WriteLine($"   Requests per second: {requestsPerSecond:F1}");
            _output.WriteLine($"   Unique RowNumbers: {uniqueCount} (100% unique)");
        }

        private async Task ValidateSequentialAssignment(string migrationId, int count)
        {
            var rowNumbers = new List<long>();
            
            for (int i = 0; i < count; i++)
            {
                var rowNumber = await _rowNumberService.GetNextRowNumberAsync(migrationId, "products");
                rowNumbers.Add(rowNumber);
            }

            // Validate uniqueness and order
            Assert.Equal(count, rowNumbers.Distinct().Count());
            for (int i = 1; i < rowNumbers.Count; i++)
            {
                Assert.True(rowNumbers[i] > rowNumbers[i-1]);
            }

            _output.WriteLine($"   ✅ Sequential assignment: {rowNumbers.First()} to {rowNumbers.Last()}");
        }

        private async Task ValidateRangeAllocation(string migrationId, int rangeCount, int rangeSize)
        {
            var allocatedRanges = new List<(long Start, long End)>();
            
            for (int i = 0; i < rangeCount; i++)
            {
                var (start, end) = await _rowNumberService.AllocateRangeAsync(migrationId, "products", rangeSize);
                allocatedRanges.Add((start, end));
                
                Assert.Equal(rangeSize, end - start + 1);
            }

            // Validate no overlapping ranges
            for (int i = 1; i < allocatedRanges.Count; i++)
            {
                Assert.True(allocatedRanges[i].Start > allocatedRanges[i-1].End);
            }

            _output.WriteLine($"   ✅ Range allocation: {rangeCount} ranges × {rangeSize} entities each");
            _output.WriteLine($"   First range: {allocatedRanges.First().Start}-{allocatedRanges.First().End}");
            _output.WriteLine($"   Last range: {allocatedRanges.Last().Start}-{allocatedRanges.Last().End}");
        }

        private async Task<List<EntityMapping>> CreateEntityMappingsRealistic(string migrationId, int totalCount, int batchSize)
        {
            var allMappings = new List<EntityMapping>();
            var batchCount = (int)Math.Ceiling((double)totalCount / batchSize);

            _output.WriteLine($"   Creating {totalCount} mappings in {batchCount} batches of {batchSize}");

            for (int batch = 0; batch < batchCount; batch++)
            {
                var currentBatchSize = Math.Min(batchSize, totalCount - (batch * batchSize));
                var batchMappings = new List<EntityMapping>();

                for (int i = 0; i < currentBatchSize; i++)
                {
                    var entityIndex = (batch * batchSize) + i + 1;
                    batchMappings.Add(new EntityMapping
                    {
                        MigrationId = migrationId,
                        EntityType = "products",
                        SourceId = $"product-{entityIndex:D6}",
                        DestinationId = string.Empty,
                        SourceStoreId = "source-store",
                        DestinationStoreId = "dest-store",
                        Status = "Pending",
                        CreatedAt = DateTime.UtcNow, // ✅ FIX: Ensure UTC DateTime
                        UpdatedAt = DateTime.UtcNow  // ✅ FIX: Ensure UTC DateTime
                    });
                }

                // Create batch with RowNumbers
                var createdBatch = await _storageService.CreateEntityMappingsBatchAsync(batchMappings);
                allMappings.AddRange(createdBatch);

                if (batch % 5 == 0 || batch == batchCount - 1)
                {
                    _output.WriteLine($"   Batch {batch + 1}/{batchCount}: Created {allMappings.Count}/{totalCount} mappings");
                }

                // Small delay between batches (realistic migration timing)
                await Task.Delay(50);
            }

            return allMappings;
        }

        private async Task ValidateRangeBasedPaginationRealistic(string migrationId, int totalCount)
        {
            const int pageSize = 50; // Realistic page size
            var retrievedEntities = 0;
            var allSourceIds = new HashSet<string>();

            var pageCount = (int)Math.Ceiling((double)totalCount / pageSize);
            _output.WriteLine($"   Testing {pageCount} pages of {pageSize} entities each");

            for (int page = 0; page < pageCount; page++)
            {
                var startRowNumber = (page * pageSize) + 1;
                var endRowNumber = Math.Min(startRowNumber + pageSize - 1, totalCount);

                var pageStopwatch = Stopwatch.StartNew();
                var mappings = await _paginationService.GetEntityMappingsByRowNumberRangeAsync(
                    migrationId, "products", startRowNumber, endRowNumber);
                pageStopwatch.Stop();

                foreach (var mapping in mappings)
                {
                    Assert.True(allSourceIds.Add(mapping.SourceId), 
                        $"Duplicate SourceId: {mapping.SourceId}");
                }

                retrievedEntities += mappings.Count;

                if (page % 10 == 0 || page == pageCount - 1)
                {
                    _output.WriteLine($"   Page {page + 1}: {mappings.Count} entities in {pageStopwatch.Elapsed.TotalMilliseconds:F0}ms");
                }

                // Validate reasonable query performance
                Assert.True(pageStopwatch.Elapsed < TimeSpan.FromSeconds(5), 
                    $"Page query took {pageStopwatch.Elapsed.TotalSeconds:F1}s - too slow");
            }

            Assert.True(retrievedEntities >= totalCount * 0.95, 
                $"Retrieved {retrievedEntities}, expected at least {totalCount * 0.95:F0}");

            _output.WriteLine($"   ✅ Range pagination: Retrieved {retrievedEntities:N0} entities across {pageCount} pages");
        }

        private async Task ValidateRealisticParallelAccess(string migrationId, int workerCount, int requestsPerWorker)
        {
            _output.WriteLine($"   Testing {workerCount} workers × {requestsPerWorker} requests (realistic concurrency)");

            var allRowNumbers = new System.Collections.Concurrent.ConcurrentBag<long>();
            var successCount = 0;
            var tasks = new List<Task>();

            var stopwatch = Stopwatch.StartNew();

            for (int worker = 0; worker < workerCount; worker++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int request = 0; request < requestsPerWorker; request++)
                    {
                        try
                        {
                            var rowNumber = await _rowNumberService.GetNextRowNumberAsync(migrationId, "products");
                            allRowNumbers.Add(rowNumber);
                            Interlocked.Increment(ref successCount);
                        }
                        catch (InvalidOperationException ex)
                        {
                            _output.WriteLine($"   ⚠️ Worker failed after max retries: {ex.Message}");
                        }

                        // Realistic delay between requests (processing time simulation)
                        await Task.Delay(Random.Shared.Next(10, 50)); // 10-50ms processing time
                    }
                }));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Validate results
            var rowNumberList = allRowNumbers.ToList();
            var uniqueCount = rowNumberList.Distinct().Count();
            var successRate = (double)successCount / (workerCount * requestsPerWorker) * 100;

            Assert.True(successRate >= 90, $"Success rate {successRate:F1}% should be at least 90% for realistic load");
            Assert.Equal(successCount, uniqueCount); // All successful assignments must be unique

            _output.WriteLine($"   ✅ Parallel access results:");
            _output.WriteLine($"   Success rate: {successRate:F1}% ({successCount}/{workerCount * requestsPerWorker})");
            _output.WriteLine($"   Unique RowNumbers: {uniqueCount} (100% unique)");
            _output.WriteLine($"   Total time: {stopwatch.Elapsed.TotalSeconds:F1}s");
        }

        [Fact]
        [Trait("Category", "Phase7Real")]
        [Trait("Task", "7.3.1")]
        public async Task ErrorHandling_ConcurrencyConflicts_RecoveryWorking()
        {
            // Arrange
            var migrationId = $"error-test-{Guid.NewGuid():N}";
            
            _output.WriteLine($"🛡️ [ERROR-HANDLING] Testing concurrency conflict recovery");

            // Act - Test moderate concurrent load to trigger some conflicts
            const int moderateConcurrency = 20; // Enough to cause some conflicts
            var tasks = new List<Task<(bool Success, long? RowNumber, TimeSpan Latency)>>();

            for (int i = 0; i < moderateConcurrency; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        var rowNumber = await _rowNumberService.GetNextRowNumberAsync(migrationId, "products");
                        return (true, (long?)rowNumber, stopwatch.Elapsed);
                    }
                    catch (InvalidOperationException)
                    {
                        return (false, null, stopwatch.Elapsed);
                    }
                }));
            }

            var results = await Task.WhenAll(tasks);

            // Assert - Error handling validation
            var successful = results.Where(r => r.Success).ToList();
            var failed = results.Where(r => !r.Success).ToList();

            Assert.True(successful.Count >= moderateConcurrency * 0.7, // At least 70% should succeed
                $"Only {successful.Count}/{moderateConcurrency} succeeded - retry logic may need tuning");

            // Validate uniqueness of successful assignments
            var assignedNumbers = successful.Select(r => r.RowNumber!.Value).ToList();
            var uniqueAssigned = assignedNumbers.Distinct().Count();
            Assert.Equal(successful.Count, uniqueAssigned);

            var avgLatency = successful.Average(r => r.Latency.TotalMilliseconds);
            
            _output.WriteLine($"✅ Error handling validation:");
            _output.WriteLine($"   Successful requests: {successful.Count}/{moderateConcurrency} ({(double)successful.Count/moderateConcurrency*100:F1}%)");
            _output.WriteLine($"   Failed after retries: {failed.Count}");
            _output.WriteLine($"   Average latency: {avgLatency:F0}ms");
            _output.WriteLine($"   Unique assignments: {uniqueAssigned} (100% unique)");
            _output.WriteLine($"   Recovery working: ✅ Conflicts handled with retry logic");
        }
    }
}
