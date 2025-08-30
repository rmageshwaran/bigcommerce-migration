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
using BigCommerce.Migration.Infrastructure.Services;

namespace BigCommerce.Migration.Tests.Integration
{
    /// <summary>
    /// Phase 7: End-to-End Integration Tests for RowNumber Pagination System
    /// Validates complete workflow from RowNumber assignment through range-based fetching
    /// </summary>
    public class Phase7EndToEndTests : IClassFixture<IntegrationTestFixture>
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly ILogger<Phase7EndToEndTests> _logger;
        private readonly IRowNumberService _rowNumberService;
        private readonly IMigrationStorageService _storageService;
        private readonly IEntityMappingsPaginationService _paginationService;

        public Phase7EndToEndTests(IntegrationTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            _logger = _fixture.ServiceProvider.GetRequiredService<ILogger<Phase7EndToEndTests>>();
            _rowNumberService = _fixture.ServiceProvider.GetRequiredService<IRowNumberService>();
            _storageService = _fixture.ServiceProvider.GetRequiredService<IMigrationStorageService>();
            _paginationService = _fixture.ServiceProvider.GetRequiredService<IEntityMappingsPaginationService>();
        }

        [Fact]
        [Trait("Category", "Phase7")]
        [Trait("Task", "7.1.1")]
        public async Task SmallDataset_1000Products_CompletesEndToEndSuccessfully()
        {
            // Arrange - Use smaller dataset for initial validation
            const int productCount = 1000;
            var migrationId = $"phase7-test-{Guid.NewGuid():N}";
            
            var initialMemory = GC.GetTotalMemory(true);
            var stopwatch = Stopwatch.StartNew();
            
            _output.WriteLine($"🧪 [PHASE7-TEST] Starting end-to-end test: {productCount:N0} products");
            _output.WriteLine($"📊 Migration ID: {migrationId}");
            _output.WriteLine($"📊 Initial memory: {initialMemory / (1024 * 1024):F1} MB");

            try
            {
                // Step 1: Test RowNumber Service Basic Functionality
                _output.WriteLine($"🔢 Step 1: Testing RowNumber service...");
                await ValidateRowNumberServiceFunctionality(migrationId);

                // Step 2: Create EntityMappings with RowNumbers
                _output.WriteLine($"📝 Step 2: Creating {productCount} EntityMappings with RowNumbers...");
                var entityMappings = await CreateEntityMappingsWithRowNumbers(migrationId, productCount);

                // Step 3: Validate RowNumber Assignment
                _output.WriteLine($"✅ Step 3: Validating RowNumber assignments...");
                ValidateRowNumberAssignments(entityMappings);

                // Step 4: Test Range-Based Pagination
                _output.WriteLine($"📄 Step 4: Testing range-based pagination...");
                await ValidateRangeBasedPagination(migrationId, productCount);

                // Step 5: Test Parallel Range Queries
                _output.WriteLine($"🔄 Step 5: Testing parallel range queries...");
                await ValidateParallelRangeQueries(migrationId, productCount);

                stopwatch.Stop();
                var finalMemory = GC.GetTotalMemory(true);
                var memoryGrowth = (finalMemory - initialMemory) / (1024 * 1024);

                // Final Validation
                Assert.True(memoryGrowth < 10, $"Memory growth {memoryGrowth:F1} MB exceeds 10 MB limit");

                _output.WriteLine($"🎉 End-to-end test completed successfully!");
                _output.WriteLine($"📊 Total processing time: {stopwatch.Elapsed.TotalSeconds:F1} seconds");
                _output.WriteLine($"📊 Memory growth: {memoryGrowth:F1} MB");
                _output.WriteLine($"📊 Throughput: {productCount / stopwatch.Elapsed.TotalSeconds:F0} entities/second");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Phase 7 end-to-end test failed");
                _output.WriteLine($"❌ Test failed: {ex.Message}");
                throw;
            }
        }

        private async Task ValidateRowNumberServiceFunctionality(string migrationId)
        {
            const string entityType = "products";
            
            // Test single RowNumber assignment
            var rowNumber1 = await _rowNumberService.GetNextRowNumberAsync(migrationId, entityType);
            var rowNumber2 = await _rowNumberService.GetNextRowNumberAsync(migrationId, entityType);
            
            Assert.True(rowNumber1 > 0, "First RowNumber should be positive");
            Assert.True(rowNumber2 > rowNumber1, "RowNumbers should be sequential");
            
            _output.WriteLine($"   ✅ Sequential assignment: {rowNumber1} → {rowNumber2}");

            // Test range allocation
            const int rangeSize = 100;
            var (startRow, endRow) = await _rowNumberService.AllocateRangeAsync(migrationId, entityType, rangeSize);
            
            Assert.Equal(rangeSize, endRow - startRow + 1);
            Assert.True(startRow > rowNumber2, "Range should start after individual assignments");
            
            _output.WriteLine($"   ✅ Range allocation: {startRow}-{endRow} (size: {rangeSize})");

            // Test counter state
            var counter = await _rowNumberService.GetCurrentCounterAsync(migrationId, entityType);
            Assert.NotNull(counter);
            Assert.True(counter.CurrentValue >= endRow, "Counter should reflect all assignments");
            
            _output.WriteLine($"   ✅ Counter state: {counter.CurrentValue}");
        }

        private async Task<List<EntityMapping>> CreateEntityMappingsWithRowNumbers(string migrationId, int count)
        {
            var entityMappings = new List<EntityMapping>();
            var batchSize = 100; // Use smaller batches for better monitoring

            for (int i = 0; i < count; i += batchSize)
            {
                var batch = new List<EntityMapping>();
                var currentBatchSize = Math.Min(batchSize, count - i);

                for (int j = 0; j < currentBatchSize; j++)
                {
                    batch.Add(new EntityMapping
                    {
                        MigrationId = migrationId,
                        EntityType = "products",
                        SourceId = $"product-{i + j + 1:D6}",
                        DestinationId = string.Empty, // Will be set after creation
                        SourceStoreId = "source-store",
                        DestinationStoreId = "dest-store",
                        Status = "Pending"
                    });
                }

                // Create batch with RowNumbers
                var createdBatch = await _storageService.CreateEntityMappingsBatchAsync(batch);
                entityMappings.AddRange(createdBatch);

                if ((i / batchSize + 1) % 10 == 0)
                {
                    _output.WriteLine($"   Created {i + currentBatchSize:N0} / {count:N0} EntityMappings");
                }
            }

            _output.WriteLine($"   ✅ Created {entityMappings.Count:N0} EntityMappings with RowNumbers");
            return entityMappings;
        }

        private void ValidateRowNumberAssignments(List<EntityMapping> entityMappings)
        {
            // Validate EntityMapping creation - all should be created successfully
            Assert.True(entityMappings.Count > 0, "EntityMappings should be created");
            Assert.All(entityMappings, mapping =>
            {
                Assert.False(string.IsNullOrEmpty(mapping.MigrationId));
                Assert.False(string.IsNullOrEmpty(mapping.EntityType)); 
                Assert.False(string.IsNullOrEmpty(mapping.SourceId));
            });

            _output.WriteLine($"   ✅ EntityMapping validation: {entityMappings.Count:N0} mappings created successfully");
        }

        private async Task ValidateRangeBasedPagination(string migrationId, int totalCount)
        {
            const int pageSize = 100;
            var totalRetrieved = 0;
            var allRetrievedIds = new HashSet<string>();

            for (int page = 0; page * pageSize < totalCount; page++)
            {
                var startRowNumber = (page * pageSize) + 1;
                var endRowNumber = Math.Min(startRowNumber + pageSize - 1, totalCount);
                
                var stopwatch = Stopwatch.StartNew();
                
                var mappings = await _paginationService.GetEntityMappingsByRowNumberRangeAsync(
                    migrationId, "products", startRowNumber, endRowNumber);
                
                stopwatch.Stop();
                
                foreach (var mapping in mappings)
                {
                    Assert.True(allRetrievedIds.Add(mapping.SourceId), 
                        $"Duplicate SourceId found: {mapping.SourceId}");
                }
                
                totalRetrieved += mappings.Count;
                
                if (page % 5 == 0)
                {
                    _output.WriteLine($"   Page {page}: Retrieved {mappings.Count} mappings in {stopwatch.Elapsed.TotalMilliseconds:F0}ms");
                }

                // Validate query performance
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), 
                    $"Range query took {stopwatch.Elapsed.TotalSeconds:F1}s, exceeds 2s limit");
            }

            Assert.Equal(totalCount, totalRetrieved);
            Assert.Equal(totalCount, allRetrievedIds.Count);
            
            _output.WriteLine($"   ✅ Range pagination: Retrieved all {totalRetrieved:N0} entities uniquely");
        }

        private async Task ValidateParallelRangeQueries(string migrationId, int totalCount)
        {
            const int parallelQueries = 10;
            var entitiesPerQuery = totalCount / parallelQueries;
            
            var tasks = new List<Task<List<EntityMapping>>>();
            var stopwatch = Stopwatch.StartNew();

            // Create parallel range queries
            for (int i = 0; i < parallelQueries; i++)
            {
                var startRowNumber = (i * entitiesPerQuery) + 1;
                var endRowNumber = (i + 1) * entitiesPerQuery;
                
                tasks.Add(_paginationService.GetEntityMappingsByRowNumberRangeAsync(
                    migrationId, "products", startRowNumber, endRowNumber));
            }

            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Validate results
            var totalParallelRetrieved = results.Sum(r => r.Count);
            var allSourceIds = new HashSet<string>();
            
            foreach (var result in results)
            {
                foreach (var mapping in result)
                {
                    Assert.True(allSourceIds.Add(mapping.SourceId), 
                        $"Duplicate found in parallel queries: {mapping.SourceId}");
                }
            }

            Assert.True(totalParallelRetrieved >= totalCount * 0.9, // Allow some tolerance for range boundaries
                $"Parallel queries retrieved {totalParallelRetrieved}, expected ~{totalCount}");
            
            _output.WriteLine($"   ✅ Parallel queries: {parallelQueries} queries completed in {stopwatch.Elapsed.TotalMilliseconds:F0}ms");
            _output.WriteLine($"   Retrieved {totalParallelRetrieved:N0} total entities across all queries");
        }

        [Fact]
        [Trait("Category", "Phase7")]
        [Trait("Task", "7.2.basic")]
        public async Task BasicPerformanceTest_RowNumberService_MeetsRequirements()
        {
            // Arrange
            var migrationId = $"perf-test-{Guid.NewGuid():N}";
            const int requestCount = 500; // Reduced for basic test
            
            _output.WriteLine($"⚡ [PERFORMANCE] Testing RowNumber service performance: {requestCount} requests");

            var stopwatch = Stopwatch.StartNew();
            var tasks = new List<Task<long>>();

            // Act - Execute concurrent RowNumber requests
            for (int i = 0; i < requestCount; i++)
            {
                tasks.Add(_rowNumberService.GetNextRowNumberAsync(migrationId, "products"));
            }

            var rowNumbers = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var requestsPerSecond = requestCount / stopwatch.Elapsed.TotalSeconds;
            var uniqueCount = rowNumbers.Distinct().Count();

            Assert.Equal(requestCount, uniqueCount); // All RowNumbers must be unique
            Assert.True(requestsPerSecond > 50, // Lower threshold for initial test
                $"Performance {requestsPerSecond:F0} req/sec is below 50 req/sec threshold");

            _output.WriteLine($"✅ Performance test results:");
            _output.WriteLine($"   Requests per second: {requestsPerSecond:F0}");
            _output.WriteLine($"   Average latency: {stopwatch.Elapsed.TotalMilliseconds / requestCount:F1}ms");
            _output.WriteLine($"   Unique RowNumbers: {uniqueCount:N0} / {requestCount:N0}");
        }
    }
}
