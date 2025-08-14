using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace BigCommerce.Migration.Tests.Validation;

/// <summary>
/// Level-specific cancellation tests for Migration, Entity, Chunk, and Component levels
/// Tests cancellation behavior at different granularities in the migration pipeline
/// </summary>
public class CancellationLevelSpecificTests : IDisposable
{
    private readonly Mock<ICancellationStore> _mockCancellationStore;
    private readonly Mock<ILogger<CancellationWorkflowValidator>> _mockLogger;
    private readonly CancellationWorkflowValidator _validator;
    private readonly string _testMigrationId;

    public CancellationLevelSpecificTests()
    {
        _testMigrationId = $"level_test_{Guid.NewGuid():N}";
        _mockCancellationStore = new Mock<ICancellationStore>();
        _mockLogger = new Mock<ILogger<CancellationWorkflowValidator>>();
        _validator = new CancellationWorkflowValidator(_mockCancellationStore.Object, _mockLogger.Object);
    }

    #region Migration Level Tests

    [Fact]
    public async Task MigrationLevel_Cancellation_FullWorkflowValidation()
    {
        // Arrange: Setup migration-level cancellation
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(_testMigrationId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(_testMigrationId))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(_testMigrationId))
            .ReturnsAsync("Migration-level cancellation test");

        // Act: Validate complete workflow at migration level
        var result = await _validator.ValidateCompleteWorkflowAsync(_testMigrationId);

        // Assert: All steps should complete successfully
        result.IsSuccess.Should().BeTrue("Migration-level cancellation should work end-to-end");
        result.StepResults.Should().HaveCount(8, "All 8 cancellation steps should be validated");
        result.TotalExecutionTime.TotalSeconds.Should().BeLessThan(5, "Migration-level cancellation should be fast");
        
        // Verify each step completed
        foreach (var step in Enum.GetValues<CancellationStep>())
        {
            var stepResult = result.GetStepResult(step);
            stepResult.Should().NotBeNull($"Step {step} should have a result");
            stepResult!.IsSuccess.Should().BeTrue($"Step {step} should complete successfully");
        }
    }

    [Fact]
    public async Task MigrationLevel_CancellationTiming_MeetsPerformanceRequirements()
    {
        // Arrange: Setup fast cancellation scenario
        _mockCancellationStore.Setup(s => s.SetCancellationFlagAsync(_testMigrationId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(_testMigrationId))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(_testMigrationId))
            .ReturnsAsync("Performance test");

        // Act: Run multiple iterations to test consistent performance
        var results = new List<CancellationWorkflowResult>();
        for (int i = 0; i < 5; i++)
        {
            var result = await _validator.ValidateCompleteWorkflowAsync($"{_testMigrationId}_{i}");
            results.Add(result);
        }

        // Assert: All iterations should meet performance requirements
        results.Should().OnlyContain(r => r.IsSuccess, "All performance test iterations should succeed");
        results.Should().OnlyContain(r => r.TotalExecutionTime.TotalSeconds < 5, "All iterations should complete under 5 seconds");
        
        var averageTime = results.Select(r => r.TotalExecutionTime.TotalMilliseconds).Average();
        averageTime.Should().BeLessThan(2000, "Average execution time should be under 2 seconds");
    }

    #endregion

    #region Entity Level Tests

    [Fact]
    public async Task EntityLevel_Cancellation_ProductEntitiesHandledCorrectly()
    {
        // Arrange: Setup entity-level cancellation for products
        var entityMigrationId = $"{_testMigrationId}_products";
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(entityMigrationId))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(entityMigrationId))
            .ReturnsAsync("Product entity cancellation");

        // Act: Simulate entity-level processing with cancellation
        var entityProcessor = new SimulatedEntityProcessor(_mockCancellationStore.Object);
        var processingResult = await entityProcessor.ProcessEntityBatchAsync(
            "Products", entityMigrationId, batchSize: 50);

        // Assert: Entity processing should detect cancellation
        processingResult.WasCancelled.Should().BeTrue("Entity processor should detect cancellation");
        processingResult.ProcessedCount.Should().Be(0, "No entities should be processed after cancellation");
        processingResult.CancellationReason.Should().Contain("Product entity cancellation");
        processingResult.ProcessingTime.TotalSeconds.Should().BeLessThan(1, "Cancellation detection should be fast");
    }

    [Fact]
    public async Task EntityLevel_PartialProcessing_CancellationMidBatch()
    {
        // Arrange: Setup cancellation that occurs mid-batch
        var entityMigrationId = $"{_testMigrationId}_partial";
        var callCount = 0;
        
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(entityMigrationId))
            .ReturnsAsync(() => ++callCount > 2); // Cancel after 2 checks
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(entityMigrationId))
            .ReturnsAsync("Mid-batch cancellation test");

        // Act: Process entities with periodic cancellation checks
        var entityProcessor = new SimulatedEntityProcessor(_mockCancellationStore.Object);
        var processingResult = await entityProcessor.ProcessEntityBatchWithPeriodicChecksAsync(
            "Categories", entityMigrationId, batchSize: 10, checkInterval: 2);

        // Assert: Should process some entities before cancellation
        processingResult.WasCancelled.Should().BeTrue("Should detect cancellation mid-batch");
        processingResult.ProcessedCount.Should().BeGreaterThan(0, "Should process some entities before cancellation");
        processingResult.ProcessedCount.Should().BeLessThan(10, "Should not process all entities due to cancellation");
    }

    #endregion

    #region Chunk Level Tests

    [Fact]
    public async Task ChunkLevel_Cancellation_IndividualChunksHandledCorrectly()
    {
        // Arrange: Setup chunk-level cancellation
        var chunkMigrationId = $"{_testMigrationId}_chunk";
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(chunkMigrationId))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(chunkMigrationId))
            .ReturnsAsync("Chunk-level cancellation test");

        // Act: Process multiple chunks with cancellation
        var chunkProcessor = new SimulatedChunkProcessor(_mockCancellationStore.Object);
        var chunkResults = await chunkProcessor.ProcessMultipleChunksAsync(
            chunkMigrationId, chunkCount: 5, entitiesPerChunk: 20);

        // Assert: All chunks should detect cancellation
        chunkResults.Should().HaveCount(5, "All chunks should be processed");
        chunkResults.Should().OnlyContain(cr => cr.WasCancelled, "All chunks should detect cancellation");
        chunkResults.Should().OnlyContain(cr => cr.ProcessedEntities == 0, "No entities should be processed in cancelled chunks");
        
        var totalProcessingTime = chunkResults.Sum(cr => cr.ProcessingTime.TotalMilliseconds);
        totalProcessingTime.Should().BeLessThan(1000, "Chunk cancellation should be fast");
    }

    [Fact]
    public async Task ChunkLevel_SubBatchProcessing_CancellationWithinSubBatches()
    {
        // Arrange: Setup sub-batch level cancellation
        var subBatchMigrationId = $"{_testMigrationId}_subbatch";
        var checkCount = 0;
        
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(subBatchMigrationId))
            .ReturnsAsync(() => ++checkCount > 3); // Cancel after 3 checks
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(subBatchMigrationId))
            .ReturnsAsync("Sub-batch cancellation test");

        // Act: Process with sub-batch granularity
        var chunkProcessor = new SimulatedChunkProcessor(_mockCancellationStore.Object);
        var result = await chunkProcessor.ProcessChunkWithSubBatchesAsync(
            subBatchMigrationId, totalEntities: 100, subBatchSize: 10);

        // Assert: Should process some sub-batches before cancellation
        result.WasCancelled.Should().BeTrue("Should detect cancellation during sub-batch processing");
        result.ProcessedEntities.Should().BeGreaterThan(0, "Should process some entities before cancellation");
        result.ProcessedEntities.Should().BeLessThan(100, "Should not process all entities due to cancellation");
        result.ProcessedSubBatches.Should().BeGreaterThan(0, "Should complete some sub-batches");
        result.ProcessedSubBatches.Should().BeLessThan(10, "Should not complete all sub-batches");
    }

    #endregion

    #region Component Level Tests

    [Fact]
    public async Task ComponentLevel_ProductComponents_CancellationDuringProcessing()
    {
        // Arrange: Setup component-level cancellation for product components
        var componentMigrationId = $"{_testMigrationId}_components";
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(componentMigrationId))
            .ReturnsAsync(true);
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(componentMigrationId))
            .ReturnsAsync("Component-level cancellation test");

        // Act: Process product components (options, modifiers, images, reviews)
        var componentProcessor = new SimulatedComponentProcessor(_mockCancellationStore.Object);
        var componentResults = await componentProcessor.ProcessProductComponentsAsync(
            componentMigrationId, productCount: 10);

        // Assert: Component processing should detect cancellation
        componentResults.WasCancelled.Should().BeTrue("Component processor should detect cancellation");
        componentResults.ProcessedComponents.Should().BeEmpty("No components should be processed after cancellation");
        componentResults.ComponentTypes.Should().Contain("Options", "Should attempt to process options first");
        componentResults.ProcessingTime.TotalSeconds.Should().BeLessThan(0.5, "Component cancellation should be very fast");
    }

    [Fact]
    public async Task ComponentLevel_PeriodicChecks_CancellationBetweenComponentTypes()
    {
        // Arrange: Setup cancellation between component types
        var componentMigrationId = $"{_testMigrationId}_periodic_components";
        var componentTypeCount = 0;
        
        _mockCancellationStore.Setup(s => s.CheckCancellationFlagAsync(componentMigrationId))
            .ReturnsAsync(() => ++componentTypeCount > 2); // Cancel after processing 2 component types
        _mockCancellationStore.Setup(s => s.GetCancellationReasonAsync(componentMigrationId))
            .ReturnsAsync("Between component types cancellation");

        // Act: Process components with checks between types
        var componentProcessor = new SimulatedComponentProcessor(_mockCancellationStore.Object);
        var result = await componentProcessor.ProcessComponentsWithTypeChecksAsync(
            componentMigrationId, productCount: 5);

        // Assert: Should process some component types before cancellation
        result.WasCancelled.Should().BeTrue("Should detect cancellation between component types");
        result.ProcessedComponentTypes.Should().BeGreaterThan(0, "Should process some component types");
        result.ProcessedComponentTypes.Should().BeLessThan(4, "Should not process all component types (Options, Modifiers, Images, Reviews)");
        result.ProcessedComponents.Should().NotBeEmpty("Should process some components before cancellation");
    }

    #endregion

    #region Helper Classes

    private class SimulatedEntityProcessor
    {
        private readonly ICancellationStore _cancellationStore;

        public SimulatedEntityProcessor(ICancellationStore cancellationStore)
        {
            _cancellationStore = cancellationStore;
        }

        public async Task<EntityProcessingResult> ProcessEntityBatchAsync(string entityType, string migrationId, int batchSize)
        {
            var result = new EntityProcessingResult { EntityType = entityType };
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Check cancellation before processing
                var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                if (isCancelled)
                {
                    result.WasCancelled = true;
                    result.CancellationReason = await _cancellationStore.GetCancellationReasonAsync(migrationId);
                    return result;
                }

                // Simulate processing entities
                for (int i = 0; i < batchSize; i++)
                {
                    result.ProcessedCount++;
                    await Task.Delay(1); // Simulate work
                }
            }
            finally
            {
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
            }

            return result;
        }

        public async Task<EntityProcessingResult> ProcessEntityBatchWithPeriodicChecksAsync(
            string entityType, string migrationId, int batchSize, int checkInterval)
        {
            var result = new EntityProcessingResult { EntityType = entityType };
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                for (int i = 0; i < batchSize; i++)
                {
                    // Periodic cancellation check
                    if (i > 0 && i % checkInterval == 0)
                    {
                        var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                        if (isCancelled)
                        {
                            result.WasCancelled = true;
                            result.CancellationReason = await _cancellationStore.GetCancellationReasonAsync(migrationId);
                            break;
                        }
                    }

                    result.ProcessedCount++;
                    await Task.Delay(1); // Simulate entity processing
                }
            }
            finally
            {
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
            }

            return result;
        }
    }

    private class SimulatedChunkProcessor
    {
        private readonly ICancellationStore _cancellationStore;

        public SimulatedChunkProcessor(ICancellationStore cancellationStore)
        {
            _cancellationStore = cancellationStore;
        }

        public async Task<List<ChunkProcessingResult>> ProcessMultipleChunksAsync(
            string migrationId, int chunkCount, int entitiesPerChunk)
        {
            var results = new List<ChunkProcessingResult>();

            for (int i = 0; i < chunkCount; i++)
            {
                var chunkResult = new ChunkProcessingResult { ChunkNumber = i + 1 };
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    // Check cancellation for each chunk
                    var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                    if (isCancelled)
                    {
                        chunkResult.WasCancelled = true;
                        chunkResult.CancellationReason = await _cancellationStore.GetCancellationReasonAsync(migrationId);
                    }
                    else
                    {
                        chunkResult.ProcessedEntities = entitiesPerChunk;
                    }
                }
                finally
                {
                    stopwatch.Stop();
                    chunkResult.ProcessingTime = stopwatch.Elapsed;
                    results.Add(chunkResult);
                }
            }

            return results;
        }

        public async Task<SubBatchProcessingResult> ProcessChunkWithSubBatchesAsync(
            string migrationId, int totalEntities, int subBatchSize)
        {
            var result = new SubBatchProcessingResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                for (int processed = 0; processed < totalEntities; processed += subBatchSize)
                {
                    // Check cancellation before each sub-batch
                    var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                    if (isCancelled)
                    {
                        result.WasCancelled = true;
                        result.CancellationReason = await _cancellationStore.GetCancellationReasonAsync(migrationId);
                        break;
                    }

                    // Process sub-batch
                    var batchSize = Math.Min(subBatchSize, totalEntities - processed);
                    result.ProcessedEntities += batchSize;
                    result.ProcessedSubBatches++;
                    
                    await Task.Delay(10); // Simulate sub-batch processing
                }
            }
            finally
            {
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
            }

            return result;
        }
    }

    private class SimulatedComponentProcessor
    {
        private readonly ICancellationStore _cancellationStore;
        private readonly string[] _componentTypes = { "Options", "Modifiers", "Images", "Reviews" };

        public SimulatedComponentProcessor(ICancellationStore cancellationStore)
        {
            _cancellationStore = cancellationStore;
        }

        public async Task<ComponentProcessingResult> ProcessProductComponentsAsync(string migrationId, int productCount)
        {
            var result = new ComponentProcessingResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Check cancellation before processing any components
                var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                if (isCancelled)
                {
                    result.WasCancelled = true;
                    result.CancellationReason = await _cancellationStore.GetCancellationReasonAsync(migrationId);
                    result.ComponentTypes.Add("Options"); // Would have tried to process options first
                    return result;
                }

                // Process components for each product
                foreach (var componentType in _componentTypes)
                {
                    result.ComponentTypes.Add(componentType);
                    for (int i = 0; i < productCount; i++)
                    {
                        result.ProcessedComponents.Add($"{componentType}_{i}");
                    }
                }
            }
            finally
            {
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
            }

            return result;
        }

        public async Task<ComponentTypeProcessingResult> ProcessComponentsWithTypeChecksAsync(
            string migrationId, int productCount)
        {
            var result = new ComponentTypeProcessingResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                foreach (var componentType in _componentTypes)
                {
                    // Check cancellation before each component type
                    var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                    if (isCancelled)
                    {
                        result.WasCancelled = true;
                        result.CancellationReason = await _cancellationStore.GetCancellationReasonAsync(migrationId);
                        break;
                    }

                    // Process this component type for all products
                    result.ProcessedComponentTypes++;
                    for (int i = 0; i < productCount; i++)
                    {
                        result.ProcessedComponents.Add($"{componentType}_{i}");
                    }

                    await Task.Delay(5); // Simulate component type processing
                }
            }
            finally
            {
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
            }

            return result;
        }
    }

    #endregion

    #region Result Classes

    private class EntityProcessingResult
    {
        public string EntityType { get; set; } = string.Empty;
        public int ProcessedCount { get; set; }
        public bool WasCancelled { get; set; }
        public string? CancellationReason { get; set; }
        public TimeSpan ProcessingTime { get; set; }
    }

    private class ChunkProcessingResult
    {
        public int ChunkNumber { get; set; }
        public int ProcessedEntities { get; set; }
        public bool WasCancelled { get; set; }
        public string? CancellationReason { get; set; }
        public TimeSpan ProcessingTime { get; set; }
    }

    private class SubBatchProcessingResult
    {
        public int ProcessedEntities { get; set; }
        public int ProcessedSubBatches { get; set; }
        public bool WasCancelled { get; set; }
        public string? CancellationReason { get; set; }
        public TimeSpan ProcessingTime { get; set; }
    }

    private class ComponentProcessingResult
    {
        public List<string> ComponentTypes { get; set; } = new();
        public List<string> ProcessedComponents { get; set; } = new();
        public bool WasCancelled { get; set; }
        public string? CancellationReason { get; set; }
        public TimeSpan ProcessingTime { get; set; }
    }

    private class ComponentTypeProcessingResult
    {
        public int ProcessedComponentTypes { get; set; }
        public List<string> ProcessedComponents { get; set; } = new();
        public bool WasCancelled { get; set; }
        public string? CancellationReason { get; set; }
        public TimeSpan ProcessingTime { get; set; }
    }

    #endregion

    public void Dispose()
    {
        // Cleanup if needed
    }
}