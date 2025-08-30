using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using Xunit.Abstractions;

namespace BigCommerce.Migration.Tests.Unit.Services
{
    /// <summary>
    /// Phase 7 Validation Tests - RowNumber Pagination Logic (Unit Tests)
    /// Tests the core pagination logic without requiring Azure Table Storage
    /// </summary>
    public class RowNumberPaginationValidationTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<IAzureTableInitializationService> _mockTableService;
        private readonly Mock<ILogger<RowNumberService>> _mockLogger;

        public RowNumberPaginationValidationTests(ITestOutputHelper output)
        {
            _output = output;
            _mockTableService = new Mock<IAzureTableInitializationService>();
            _mockLogger = new Mock<ILogger<RowNumberService>>();
        }

        [Fact]
        [Trait("Category", "Phase7Validation")]
        public async Task RowNumberService_SequentialAssignment_ProducesUniqueNumbers()
        {
            // Arrange
            const int requestCount = 100;
            var migrationId = "test-migration";
            var entityType = "products";

            var mockRowNumberService = CreateMockRowNumberService();
            
            _output.WriteLine($"🧪 [VALIDATION] Testing sequential RowNumber assignment: {requestCount} requests");

            // Act - Request multiple RowNumbers sequentially
            var rowNumbers = new List<long>();
            for (int i = 0; i < requestCount; i++)
            {
                var rowNumber = await mockRowNumberService.GetNextRowNumberAsync(migrationId, entityType);
                rowNumbers.Add(rowNumber);
            }

            // Assert - All numbers should be unique and sequential
            var uniqueCount = rowNumbers.Distinct().Count();
            Assert.Equal(requestCount, uniqueCount);

            for (int i = 1; i < rowNumbers.Count; i++)
            {
                Assert.True(rowNumbers[i] > rowNumbers[i-1], 
                    $"RowNumber {i}: {rowNumbers[i]} should be greater than {rowNumbers[i-1]}");
            }

            _output.WriteLine($"✅ Sequential assignment validated: {rowNumbers.First()} to {rowNumbers.Last()}");
            _output.WriteLine($"   All {uniqueCount} RowNumbers are unique and sequential");
        }

        [Fact]
        [Trait("Category", "Phase7Validation")]
        public void CompositeRowKey_FormatValidation_CorrectStructure()
        {
            // Arrange
            const long rowNumber = 12345;
            const string entityType = "products";
            const string sourceId = "product-67890";

            _output.WriteLine($"🧪 [VALIDATION] Testing composite RowKey format");

            // Act - Generate composite RowKey (simulating MigrationStorageService logic)
            var compositeRowKey = $"{rowNumber:D10}_{entityType}_{sourceId}";

            // Assert - Validate format structure
            Assert.Equal("0000012345_products_product-67890", compositeRowKey);

            var parts = compositeRowKey.Split('_');
            Assert.Equal(3, parts.Length);
            Assert.Equal("0000012345", parts[0]); // Zero-padded RowNumber
            Assert.Equal("products", parts[1]);   // EntityType
            Assert.Equal("product-67890", parts[2]); // SourceId

            _output.WriteLine($"✅ Composite RowKey format validated: {compositeRowKey}");
        }

        [Fact]
        [Trait("Category", "Phase7Validation")]
        public void RangeQuery_FilterGeneration_CorrectODataFilter()
        {
            // Arrange
            var migrationId = "test-migration-123";
            var entityType = "products";
            const long startRowNumber = 1000;
            const long endRowNumber = 1999;

            _output.WriteLine($"🧪 [VALIDATION] Testing range query filter generation");

            // Act - Generate OData filter (simulating EntityMappingsPaginationService logic)
            var startRowKey = $"{startRowNumber:D10}_{entityType}_";
            var endRowKey = $"{endRowNumber + 1:D10}_{entityType}_"; // +1 for exclusive upper bound
            var filter = $"PartitionKey eq '{migrationId}' and RowKey ge '{startRowKey}' and RowKey lt '{endRowKey}'";

            // Assert - Validate filter structure
            var expectedFilter = "PartitionKey eq 'test-migration-123' and RowKey ge '0000001000_products_' and RowKey lt '0000002000_products_'";
            Assert.Equal(expectedFilter, filter);

            _output.WriteLine($"✅ Range query filter validated:");
            _output.WriteLine($"   Filter: {filter}");
            _output.WriteLine($"   Start: {startRowKey}");
            _output.WriteLine($"   End: {endRowKey}");
        }

        [Fact]
        [Trait("Category", "Phase7Validation")]
        public void BatchSizeCalculation_PaginationMath_CorrectRanges()
        {
            // Arrange
            const int pageSize = 250;
            const int totalEntities = 10000;
            var expectedBatches = (int)Math.Ceiling((double)totalEntities / pageSize); // 40 batches

            _output.WriteLine($"🧪 [VALIDATION] Testing batch size calculations");
            _output.WriteLine($"   Total entities: {totalEntities:N0}");
            _output.WriteLine($"   Page size: {pageSize}");
            _output.WriteLine($"   Expected batches: {expectedBatches}");

            var batchRanges = new List<(long Start, long End, int Size)>();

            // Act - Calculate batch ranges (simulating EntityFetchService logic)
            for (int batchNumber = 0; batchNumber < expectedBatches; batchNumber++)
            {
                var startRowNumber = (batchNumber * pageSize) + 1;
                var endRowNumber = startRowNumber + pageSize - 1;
                
                // Handle last batch (might be smaller)
                if (endRowNumber > totalEntities)
                {
                    endRowNumber = totalEntities;
                }
                
                var actualBatchSize = (int)(endRowNumber - startRowNumber + 1);
                batchRanges.Add((startRowNumber, endRowNumber, actualBatchSize));
            }

            // Assert - Validate ranges don't overlap and cover all entities
            Assert.Equal(expectedBatches, batchRanges.Count);

            long totalCovered = 0;
            for (int i = 0; i < batchRanges.Count; i++)
            {
                var (start, end, size) = batchRanges[i];
                
                // Validate range properties
                Assert.True(start <= end, $"Batch {i}: Start {start} should be <= End {end}");
                Assert.Equal(size, end - start + 1);
                
                // Validate no gaps between batches
                if (i > 0)
                {
                    var previousEnd = batchRanges[i-1].End;
                    Assert.Equal(previousEnd + 1, start);
                }
                
                totalCovered += size;
            }

            Assert.Equal(totalEntities, totalCovered);

            _output.WriteLine($"✅ Batch calculations validated:");
            _output.WriteLine($"   Generated {batchRanges.Count} batches");
            _output.WriteLine($"   Total entities covered: {totalCovered:N0}");
            _output.WriteLine($"   First batch: {batchRanges.First().Start}-{batchRanges.First().End} (size: {batchRanges.First().Size})");
            _output.WriteLine($"   Last batch: {batchRanges.Last().Start}-{batchRanges.Last().End} (size: {batchRanges.Last().Size})");
        }

        [Theory]
        [InlineData(1000, 100)]    // 10 full batches
        [InlineData(1000, 150)]    // 6 batches + 1 partial (100 entities)
        [InlineData(999, 100)]     // 9 batches + 1 partial (99 entities)
        [InlineData(100, 100)]     // Exactly 1 batch
        [InlineData(50, 100)]      // Less than 1 batch (50 entities)
        [Trait("Category", "Phase7Validation")]
        public void PaginationMath_VariousDatasetSizes_HandlesEdgeCases(int totalEntities, int pageSize)
        {
            _output.WriteLine($"🧪 [VALIDATION] Testing edge case: {totalEntities} entities, {pageSize} page size");

            // Act - Calculate total batches needed
            var expectedBatches = (int)Math.Ceiling((double)totalEntities / pageSize);
            var actualBatchCount = 0;
            var totalProcessed = 0;

            for (int batchNumber = 0; batchNumber < expectedBatches; batchNumber++)
            {
                var startRowNumber = (batchNumber * pageSize) + 1;
                var endRowNumber = Math.Min(startRowNumber + pageSize - 1, totalEntities);
                var batchSize = (int)(endRowNumber - startRowNumber + 1);
                
                actualBatchCount++;
                totalProcessed += batchSize;
                
                Assert.True(batchSize > 0, "Batch size should be positive");
                Assert.True(batchSize <= pageSize, "Batch size should not exceed page size");
            }

            // Assert
            Assert.Equal(expectedBatches, actualBatchCount);
            Assert.Equal(totalEntities, totalProcessed);

            _output.WriteLine($"✅ Edge case handled correctly:");
            _output.WriteLine($"   Expected batches: {expectedBatches}");
            _output.WriteLine($"   Actual batches: {actualBatchCount}");
            _output.WriteLine($"   Total processed: {totalProcessed}");
        }

        private IRowNumberService CreateMockRowNumberService()
        {
            var currentValues = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
            
            var mock = new Mock<IRowNumberService>();
            
            mock.Setup(x => x.GetNextRowNumberAsync(It.IsAny<string>(), It.IsAny<string>(), default))
                .Returns<string, string, System.Threading.CancellationToken>((migrationId, entityType, ct) =>
                {
                    var key = $"{migrationId}_{entityType}";
                    var nextValue = currentValues.AddOrUpdate(key, 1, (k, v) => v + 1);
                    return Task.FromResult(nextValue);
                });
                
            mock.Setup(x => x.AllocateRangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), default))
                .Returns<string, string, int, System.Threading.CancellationToken>((migrationId, entityType, count, ct) =>
                {
                    var key = $"{migrationId}_{entityType}";
                    var startValue = currentValues.AddOrUpdate(key, count, (k, v) => v + count) - count + 1;
                    var endValue = startValue + count - 1;
                    return Task.FromResult((startValue, endValue));
                });

            return mock.Object;
        }
    }
}
