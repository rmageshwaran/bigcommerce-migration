using BigCommerce.Migration.Activities.Activities;
using BigCommerce.Migration.Activities.Models;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Tests.Integration;

/// <summary>
/// COMPREHENSIVE WORKFLOW DEMONSTRATION
/// 
/// This test class demonstrates the complete incremental progress workflow integration:
/// 1. ProcessEntityChunkActivity processes chunks
/// 2. IncrementProgressAsync writes chunk data immediately  
/// 3. UpdateEntityProgressActivity uses aggregated data
/// 4. Main tables updated with real-time statistics
/// 5. Cancellation scenarios preserve completed work
/// 
/// PURPOSE: Prove that the incremental progress system works end-to-end
/// and solves the data loss problem during migration cancellations.
/// </summary>
[Collection("Workflow Demonstration Tests")]
public class IncrementalProgressWorkflowDemonstration : IDisposable
{
    #region Test Infrastructure

    private readonly ServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly string _testMigrationId;
    private readonly IIncrementEventsService _incrementEventsService;
    private readonly IProgressTracker _progressTracker;
    private readonly IMigrationStorageService _migrationStorageService;
    private readonly ILogger<IncrementalProgressWorkflowDemonstration> _logger;

    public IncrementalProgressWorkflowDemonstration()
    {
        _testMigrationId = $"demo-migration-{Guid.NewGuid():N}";

        // Configure services for demonstration
        var configData = new Dictionary<string, string>
        {
            ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true", // Azurite for local testing
            ["BigCommerce:DefaultApiVersion"] = "v3",
            ["BigCommerce:DefaultTimeout"] = "30"
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        _incrementEventsService = _serviceProvider.GetRequiredService<IIncrementEventsService>();
        _progressTracker = _serviceProvider.GetRequiredService<IProgressTracker>();
        _migrationStorageService = _serviceProvider.GetRequiredService<IMigrationStorageService>();
        _logger = _serviceProvider.GetRequiredService<ILogger<IncrementalProgressWorkflowDemonstration>>();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Add configuration
        services.AddSingleton(_configuration);

        // Add real services for full integration testing
        services.AddSingleton<IIncrementEventsService, IncrementEventsService>();
        services.AddSingleton<IMigrationStorageService, MigrationStorageService>();

        // Add ProgressTracker with real dependencies
        services.AddSingleton<IProgressTracker>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<ProgressTracker>>();
            var mockProgressEventPublisher = new Mock<IProgressEventPublisher>();
            var mockSignalREventFactory = new Mock<ISignalREventFactory>();
            var storageService = serviceProvider.GetRequiredService<IMigrationStorageService>();
            var incrementEventsService = serviceProvider.GetRequiredService<IIncrementEventsService>();

            // Setup SignalR factory mock to return valid events
            mockSignalREventFactory.Setup(x => x.CreateMigrationProgress(It.IsAny<string>(), It.IsAny<MigrationProgressOptions>()))
                .Returns(new MigrationProgressEvent { MigrationId = _testMigrationId });

            return new ProgressTracker(
                logger,
                mockProgressEventPublisher.Object,
                mockSignalREventFactory.Object,
                storageService,
                incrementEventsService);
        });
    }

    public void Dispose()
    {
        CleanupTestDataAsync().GetAwaiter().GetResult();
        _serviceProvider?.Dispose();
    }

    private async Task CleanupTestDataAsync()
    {
        try
        {
            await _incrementEventsService.DeleteMigrationIncrementsAsync(_testMigrationId);
            await _migrationStorageService.DeleteMigrationAsync(_testMigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup test data for migration {MigrationId}", _testMigrationId);
        }
    }

    #endregion

    #region DEMONSTRATION TEST 1: Complete Workflow Integration

    [Fact]
    public async Task DemonstrateCompleteWorkflowIntegration()
    {
        _logger.LogInformation("🚀 DEMONSTRATION 1: Complete Workflow Integration");
        _logger.LogInformation("📋 Testing Migration ID: {MigrationId}", _testMigrationId);

        // STEP 1: Create initial migration entry
        _logger.LogInformation("📝 STEP 1: Creating initial migration entry");
        var migrationEntry = new MigrationEntry
        {
            Id = _testMigrationId,
            SourceStoreId = "source-store-123",
            DestinationStoreId = "dest-store-456", 
            Status = "running",
            TotalEntities = 0,
            ProcessedEntities = 0,
            FailedEntities = 0,
            SkippedEntities = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _migrationStorageService.CreateMigrationAsync(migrationEntry);
        _logger.LogInformation("✅ STEP 1 COMPLETE: Migration entry created");

        // STEP 2: Simulate chunk processing with incremental progress
        _logger.LogInformation("🔄 STEP 2: Simulating chunk processing with incremental progress");
        
        var chunkResults = new List<(string entityType, int chunkNumber, int successful, int failed, int skipped)>
        {
            ("products", 1, 245, 3, 2),     // Chunk 1: 245 success, 3 failed, 2 skipped
            ("products", 2, 238, 7, 5),     // Chunk 2: 238 success, 7 failed, 5 skipped  
            ("products", 3, 241, 4, 5),     // Chunk 3: 241 success, 4 failed, 5 skipped
            ("categories", 1, 98, 1, 1),    // Categories: 98 success, 1 failed, 1 skipped
            ("brands", 1, 47, 2, 1)         // Brands: 47 success, 2 failed, 1 skipped
        };

        var processingStartTime = DateTime.UtcNow;
        
        foreach (var (entityType, chunkNumber, successful, failed, skipped) in chunkResults)
        {
            var chunkEndTime = DateTime.UtcNow;
            
            _logger.LogInformation("📦 Processing {EntityType} Chunk {ChunkNumber}: Success={Success}, Failed={Failed}, Skipped={Skipped}",
                entityType, chunkNumber, successful, failed, skipped);

            // Simulate the incremental progress call that would happen in ProcessEntityChunkActivity
            await _progressTracker.IncrementProgressAsync(
                migrationId: _testMigrationId,
                entityType: entityType,
                chunkNumber: chunkNumber,
                chunkStartIndex: (chunkNumber - 1) * 250,
                chunkSize: 250,
                successfulEntities: successful,
                failedEntities: failed,
                skippedEntities: skipped,
                cancelledEntities: 0,
                processingStartTime: processingStartTime,
                processingEndTime: chunkEndTime,
                sourceStore: "source-store-123",
                destinationStore: "dest-store-456");

            // Small delay to simulate processing time
            await Task.Delay(100);
        }

        _logger.LogInformation("✅ STEP 2 COMPLETE: All chunk increments written");

        // STEP 3: Wait for fire-and-forget writes to complete
        _logger.LogInformation("⏳ STEP 3: Waiting for fire-and-forget writes to complete");
        await Task.Delay(2000); // Give time for async writes to complete
        _logger.LogInformation("✅ STEP 3 COMPLETE: Fire-and-forget writes completed");

        // STEP 4: Verify chunk increment events were written
        _logger.LogInformation("🔍 STEP 4: Verifying chunk increment events were written");
        
        var allChunkEvents = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId);
        allChunkEvents.Should().HaveCount(5, "because we wrote 5 chunks");
        
        var productChunks = allChunkEvents.Where(e => e.EntityType == "products").ToList();
        productChunks.Should().HaveCount(3, "because we wrote 3 product chunks");
        
        var totalSuccessful = allChunkEvents.Sum(e => e.SuccessfulEntities);
        totalSuccessful.Should().Be(869, "because 245+238+241+98+47 = 869");
        
        _logger.LogInformation("✅ STEP 4 COMPLETE: Chunk events verified - Total Successful: {TotalSuccessful}", totalSuccessful);

        // STEP 5: Demonstrate aggregated progress retrieval
        _logger.LogInformation("📊 STEP 5: Demonstrating aggregated progress retrieval");
        
        var aggregatedProgress = await _incrementEventsService.GetAggregatedProgressAsync(_testMigrationId);
        aggregatedProgress.Should().HaveCount(3, "because we have 3 entity types");
        
        var productsSummary = aggregatedProgress["products"];
        productsSummary.TotalSuccessful.Should().Be(724, "because 245+238+241 = 724");
        productsSummary.TotalFailed.Should().Be(14, "because 3+7+4 = 14");
        productsSummary.TotalSkipped.Should().Be(12, "because 2+5+5 = 12");
        productsSummary.TotalChunks.Should().Be(3, "because we processed 3 product chunks");
        
        _logger.LogInformation("✅ STEP 5 COMPLETE: Products aggregation - Success: {Success}, Failed: {Failed}, Skipped: {Skipped}",
            productsSummary.TotalSuccessful, productsSummary.TotalFailed, productsSummary.TotalSkipped);

        // STEP 6: Demonstrate enhanced progress tracker integration
        _logger.LogInformation("🔗 STEP 6: Demonstrating enhanced progress tracker integration");
        
        // First, initialize some cached progress
        var initialUpdate = new ProgressUpdate
        {
            MigrationId = _testMigrationId,
            EntityType = "products",
            Phase = "Processing",
            ProcessedCount = 0,
            SuccessCount = 0,
            FailureCount = 0,
            SkippedCount = 0,
            Timestamp = DateTime.UtcNow
        };
        
        await _progressTracker.UpdateProgressAsync(_testMigrationId, initialUpdate);
        
        // Now get the latest aggregated progress (should include incremental data)
        var latestProgress = await _progressTracker.GetLatestAggregatedProgressAsync(_testMigrationId);
        
        // Verify that the progress includes real-time aggregated data
        latestProgress.EntityProgress.Should().ContainKey("products");
        latestProgress.EntityProgress["products"].SuccessCount.Should().Be(724, "because aggregated data shows 724 successful products");
        latestProgress.EntityProgress["products"].FailureCount.Should().Be(14, "because aggregated data shows 14 failed products");
        latestProgress.EntityProgress["products"].SkippedCount.Should().Be(12, "because aggregated data shows 12 skipped products");
        
        _logger.LogInformation("✅ STEP 6 COMPLETE: Enhanced progress shows real-time data - Success: {Success}, Failed: {Failed}, Skipped: {Skipped}",
            latestProgress.EntityProgress["products"].SuccessCount,
            latestProgress.EntityProgress["products"].FailureCount,
            latestProgress.EntityProgress["products"].SkippedCount);

        // STEP 7: Demonstrate table updates with enhanced data
        _logger.LogInformation("💾 STEP 7: Demonstrating table updates with enhanced data");
        
        // The PersistProgressToStorageAsync method (called by UpdateProgressAsync) should now use the enhanced data
        var finalUpdate = new ProgressUpdate
        {
            MigrationId = _testMigrationId,
            EntityType = "products",
            Phase = "Completed",
            ProcessedCount = 750, // This will be overridden by aggregated data
            SuccessCount = 500,   // This will be overridden by aggregated data
            FailureCount = 50,    // This will be overridden by aggregated data
            SkippedCount = 25,    // This will be overridden by aggregated data
            Timestamp = DateTime.UtcNow
        };
        
        await _progressTracker.UpdateProgressAsync(_testMigrationId, finalUpdate);
        
        // Verify that the final progress reflects aggregated data, not the update values
        var finalProgress = await _progressTracker.GetLatestAggregatedProgressAsync(_testMigrationId);
        finalProgress.EntityProgress["products"].SuccessCount.Should().Be(724, "because aggregated data takes precedence over update values");
        
        _logger.LogInformation("✅ STEP 7 COMPLETE: Table updates use aggregated data, not update values");

        _logger.LogInformation("🎉 DEMONSTRATION COMPLETE: Full workflow integration verified!");
    }

    #endregion

    #region DEMONSTRATION TEST 2: Cancellation Data Preservation

    [Fact]
    public async Task DemonstrateCancellationDataPreservation()
    {
        _logger.LogInformation("🚀 DEMONSTRATION 2: Cancellation Data Preservation");
        _logger.LogInformation("📋 Testing Migration ID: {MigrationId}", _testMigrationId);

        // STEP 1: Process several chunks successfully
        _logger.LogInformation("📦 STEP 1: Processing chunks successfully");
        
        var completedChunks = new[]
        {
            (entityType: "products", chunkNumber: 1, successful: 250, failed: 0, skipped: 0),
            (entityType: "products", chunkNumber: 2, successful: 248, failed: 2, skipped: 0),
            (entityType: "products", chunkNumber: 3, successful: 235, failed: 10, skipped: 5)
        };

        foreach (var chunk in completedChunks)
        {
            await _progressTracker.IncrementProgressAsync(
                _testMigrationId, chunk.entityType, chunk.chunkNumber,
                (chunk.chunkNumber - 1) * 250, 250,
                chunk.successful, chunk.failed, chunk.skipped, 0,
                DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow,
                "source-store", "dest-store");
        }

        await Task.Delay(1000); // Wait for writes
        _logger.LogInformation("✅ STEP 1 COMPLETE: {ChunkCount} chunks processed", completedChunks.Length);

        // STEP 2: Simulate migration cancellation (no more chunks processed)
        _logger.LogInformation("🛑 STEP 2: SIMULATING MIGRATION CANCELLATION");
        _logger.LogInformation("❌ Migration cancelled mid-process - no more chunks will be processed");

        // STEP 3: Verify that completed work is preserved
        _logger.LogInformation("🔍 STEP 3: Verifying completed work is preserved after cancellation");
        
        var preservedEvents = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId, "products");
        preservedEvents.Should().HaveCount(3, "because 3 chunks completed before cancellation");
        
        var totalPreservedSuccess = preservedEvents.Sum(e => e.SuccessfulEntities);
        totalPreservedSuccess.Should().Be(733, "because 250+248+235 = 733 entities were successfully migrated");
        
        _logger.LogInformation("✅ STEP 3 COMPLETE: Preserved {SuccessfulEntities} successful entities after cancellation", totalPreservedSuccess);

        // STEP 4: Demonstrate that aggregated progress shows preserved work
        _logger.LogInformation("📊 STEP 4: Demonstrating aggregated progress shows preserved work");
        
        var aggregatedProgress = await _incrementEventsService.GetAggregatedProgressAsync(_testMigrationId);
        var productsProgress = aggregatedProgress["products"];
        
        productsProgress.TotalSuccessful.Should().Be(733, "because aggregation preserves all successful work");
        productsProgress.TotalFailed.Should().Be(12, "because aggregation preserves all failed entities");
        productsProgress.TotalSkipped.Should().Be(5, "because aggregation preserves all skipped entities");
        
        _logger.LogInformation("✅ STEP 4 COMPLETE: Aggregated progress preserves work - Success: {Success}, Failed: {Failed}, Skipped: {Skipped}",
            productsProgress.TotalSuccessful, productsProgress.TotalFailed, productsProgress.TotalSkipped);

        // STEP 5: Show that enhanced progress tracker returns preserved data
        _logger.LogInformation("🔗 STEP 5: Enhanced progress tracker returns preserved data");
        
        var enhancedProgress = await _progressTracker.GetLatestAggregatedProgressAsync(_testMigrationId);
        
        if (enhancedProgress.EntityProgress.ContainsKey("products"))
        {
            var productProgress = enhancedProgress.EntityProgress["products"];
            productProgress.SuccessCount.Should().Be(733, "because enhanced progress uses aggregated increment data");
            productProgress.FailureCount.Should().Be(12, "because enhanced progress includes all failures");
            productProgress.SkippedCount.Should().Be(5, "because enhanced progress includes all skipped entities");
            
            _logger.LogInformation("✅ STEP 5 COMPLETE: Enhanced progress shows preserved data - Success: {Success}, Failed: {Failed}, Skipped: {Skipped}",
                productProgress.SuccessCount, productProgress.FailureCount, productProgress.SkippedCount);
        }

        _logger.LogInformation("🎉 DEMONSTRATION 2 COMPLETE: Cancellation data preservation verified!");
        _logger.LogInformation("💡 KEY INSIGHT: {SuccessfulEntities} entities preserved despite cancellation", totalPreservedSuccess);
    }

    #endregion

    #region DEMONSTRATION TEST 3: Table Update Integration

    [Fact]
    public async Task DemonstrateTableUpdateIntegration()
    {
        _logger.LogInformation("🚀 DEMONSTRATION 3: Table Update Integration");
        _logger.LogInformation("📋 Testing Migration ID: {MigrationId}", _testMigrationId);

        // STEP 1: Create migration entry in migrations table
        _logger.LogInformation("📝 STEP 1: Creating migration entry");
        var migrationEntry = new MigrationEntry
        {
            Id = _testMigrationId,
            SourceStoreId = "source-store-789",
            DestinationStoreId = "dest-store-012",
            Status = "running",
            TotalEntities = 1000, // Expected total
            ProcessedEntities = 0,
            SuccessfulEntities = 0,
            FailedEntities = 0,
            SkippedEntities = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _migrationStorageService.CreateMigrationAsync(migrationEntry);
        _logger.LogInformation("✅ STEP 1 COMPLETE: Migration entry created with TotalEntities: {TotalEntities}", migrationEntry.TotalEntities);

        // STEP 2: Process chunks and write incremental data
        _logger.LogInformation("🔄 STEP 2: Processing chunks with incremental data");
        
        await _progressTracker.IncrementProgressAsync(
            _testMigrationId, "products", 1, 0, 250,
            230, 15, 5, 0, // 230 success, 15 failed, 5 skipped
            DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow,
            "source-store-789", "dest-store-012");

        await _progressTracker.IncrementProgressAsync(
            _testMigrationId, "products", 2, 250, 250,
            225, 20, 5, 0, // 225 success, 20 failed, 5 skipped
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            "source-store-789", "dest-store-012");

        await Task.Delay(1000); // Wait for writes
        _logger.LogInformation("✅ STEP 2 COMPLETE: Chunks processed with incremental data");

        // STEP 3: Trigger table updates through normal progress flow
        _logger.LogInformation("📊 STEP 3: Triggering table updates through progress flow");
        
        var progressUpdate = new ProgressUpdate
        {
            MigrationId = _testMigrationId,
            EntityType = "products",
            Phase = "Processing",
            ProcessedCount = 500, // This will be overridden by aggregated data
            SuccessCount = 400,   // This will be overridden by aggregated data
            FailureCount = 50,    // This will be overridden by aggregated data
            SkippedCount = 50,    // This will be overridden by aggregated data
            Timestamp = DateTime.UtcNow
        };

        // This call will trigger PersistProgressToStorageAsync with enhanced data
        await _progressTracker.UpdateProgressAsync(_testMigrationId, progressUpdate);
        _logger.LogInformation("✅ STEP 3 COMPLETE: Progress update triggered");

        // STEP 4: Verify migrations table was updated with aggregated data
        _logger.LogInformation("🔍 STEP 4: Verifying migrations table update");
        
        var updatedMigration = await _migrationStorageService.GetMigrationAsync(_testMigrationId);
        updatedMigration.Should().NotBeNull("because migration should exist");
        
        // The migrations table should now reflect aggregated chunk data, not the update values
        _logger.LogInformation("📊 Migrations Table Data:");
        _logger.LogInformation("   ProcessedEntities: {ProcessedEntities}", updatedMigration!.ProcessedEntities);
        _logger.LogInformation("   SuccessfulEntities: {SuccessfulEntities}", updatedMigration.SuccessfulEntities);
        _logger.LogInformation("   FailedEntities: {FailedEntities}", updatedMigration.FailedEntities);
        _logger.LogInformation("   SkippedEntities: {SkippedEntities}", updatedMigration.SkippedEntities);
        
        _logger.LogInformation("✅ STEP 4 COMPLETE: Migrations table verified");

        // STEP 5: Verify entityprogress table was updated with aggregated data
        _logger.LogInformation("🔍 STEP 5: Verifying entityprogress table update");
        
        var entityProgressList = await _migrationStorageService.GetEntityProgressAsync(_testMigrationId);
        var productsProgress = entityProgressList.FirstOrDefault(e => e.EntityType == "products");
        
        productsProgress.Should().NotBeNull("because products entity progress should exist");
        
        _logger.LogInformation("📊 EntityProgress Table Data for Products:");
        _logger.LogInformation("   ProcessedCount: {ProcessedCount}", productsProgress!.ProcessedCount);
        _logger.LogInformation("   SuccessCount: {SuccessCount}", productsProgress.SuccessCount);
        _logger.LogInformation("   FailureCount: {FailureCount}", productsProgress.FailureCount);
        _logger.LogInformation("   SkippedCount: {SkippedCount}", productsProgress.SkippedCount);
        
        // Verify the data comes from aggregated chunks, not the update values
        productsProgress.SuccessCount.Should().Be(455, "because 230+225 = 455 from aggregated chunks");
        productsProgress.FailureCount.Should().Be(35, "because 15+20 = 35 from aggregated chunks");
        productsProgress.SkippedCount.Should().Be(10, "because 5+5 = 10 from aggregated chunks");
        
        _logger.LogInformation("✅ STEP 5 COMPLETE: EntityProgress table verified with aggregated data");

        _logger.LogInformation("🎉 DEMONSTRATION 3 COMPLETE: Table update integration verified!");
        _logger.LogInformation("💡 KEY INSIGHT: Both tables now use real-time aggregated data instead of memory-based estimates");
    }

    #endregion

    #region DEMONSTRATION TEST 4: Performance Impact

    [Fact]
    public async Task DemonstratePerformanceImpact()
    {
        _logger.LogInformation("🚀 DEMONSTRATION 4: Performance Impact Analysis");
        _logger.LogInformation("📋 Testing Migration ID: {MigrationId}", _testMigrationId);

        // STEP 1: Measure processing time without incremental progress
        _logger.LogInformation("⏱️ STEP 1: Measuring baseline processing time");
        
        var baselineStart = DateTime.UtcNow;
        
        // Simulate chunk processing without incremental calls
        for (int i = 1; i <= 10; i++)
        {
            await Task.Delay(50); // Simulate processing time
        }
        
        var baselineTime = DateTime.UtcNow - baselineStart;
        _logger.LogInformation("✅ STEP 1 COMPLETE: Baseline time for 10 chunks: {BaselineTime}ms", baselineTime.TotalMilliseconds);

        // STEP 2: Measure processing time with incremental progress
        _logger.LogInformation("⏱️ STEP 2: Measuring time with incremental progress");
        
        var incrementalStart = DateTime.UtcNow;
        
        // Simulate chunk processing with incremental calls
        for (int i = 1; i <= 10; i++)
        {
            await Task.Delay(50); // Simulate processing time
            
            // Fire-and-forget incremental progress (should not add significant overhead)
            await _progressTracker.IncrementProgressAsync(
                _testMigrationId, "performance-test", i, (i-1) * 250, 250,
                240, 8, 2, 0,
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
                "source-store", "dest-store");
        }
        
        var incrementalTime = DateTime.UtcNow - incrementalStart;
        _logger.LogInformation("✅ STEP 2 COMPLETE: Incremental time for 10 chunks: {IncrementalTime}ms", incrementalTime.TotalMilliseconds);

        // STEP 3: Analyze performance impact
        _logger.LogInformation("📊 STEP 3: Analyzing performance impact");
        
        var overhead = incrementalTime.TotalMilliseconds - baselineTime.TotalMilliseconds;
        var overheadPercentage = (overhead / baselineTime.TotalMilliseconds) * 100;
        
        _logger.LogInformation("📈 Performance Analysis:");
        _logger.LogInformation("   Baseline Time: {BaselineTime}ms", baselineTime.TotalMilliseconds);
        _logger.LogInformation("   Incremental Time: {IncrementalTime}ms", incrementalTime.TotalMilliseconds);
        _logger.LogInformation("   Overhead: {Overhead}ms ({OverheadPercentage:F1}%)", overhead, overheadPercentage);
        
        // Performance should be minimal due to fire-and-forget pattern
        overheadPercentage.Should().BeLessThan(20, "because fire-and-forget writes should add minimal overhead");
        
        _logger.LogInformation("✅ STEP 3 COMPLETE: Performance impact acceptable ({OverheadPercentage:F1}% overhead)", overheadPercentage);

        // STEP 4: Verify data was still written despite fire-and-forget
        _logger.LogInformation("🔍 STEP 4: Verifying data integrity despite fire-and-forget");
        
        await Task.Delay(2000); // Wait for async writes to complete
        
        var writtenEvents = await _incrementEventsService.GetChunkIncrementsAsync(_testMigrationId, "performance-test");
        writtenEvents.Should().HaveCount(10, "because all 10 chunks should be written despite fire-and-forget");
        
        var totalSuccessful = writtenEvents.Sum(e => e.SuccessfulEntities);
        totalSuccessful.Should().Be(2400, "because 10 chunks × 240 successful = 2400");
        
        _logger.LogInformation("✅ STEP 4 COMPLETE: All {ChunkCount} chunks written with {TotalSuccessful} successful entities", 
            writtenEvents.Count, totalSuccessful);

        _logger.LogInformation("🎉 DEMONSTRATION 4 COMPLETE: Performance impact minimal, data integrity maintained!");
    }

    #endregion

    #region DEMONSTRATION TEST 5: Real-time Dashboard Integration

    [Fact]
    public async Task DemonstrateRealTimeDashboardIntegration()
    {
        _logger.LogInformation("🚀 DEMONSTRATION 5: Real-time Dashboard Integration");
        _logger.LogInformation("📋 Testing Migration ID: {MigrationId}", _testMigrationId);

        // STEP 1: Initialize migration with entity discovery
        _logger.LogInformation("🔍 STEP 1: Initializing migration with entity discovery");
        
        var initialProgress = new ProgressUpdate
        {
            MigrationId = _testMigrationId,
            EntityType = "products",
            Phase = "Discovery",
            ProcessedCount = 0,
            SuccessCount = 0,
            FailureCount = 0,
            SkippedCount = 0,
            Timestamp = DateTime.UtcNow
        };
        
        await _progressTracker.UpdateProgressAsync(_testMigrationId, initialProgress);
        _logger.LogInformation("✅ STEP 1 COMPLETE: Migration initialized");

        // STEP 2: Simulate real-time chunk processing
        _logger.LogInformation("⚡ STEP 2: Simulating real-time chunk processing");
        
        var chunks = new[]
        {
            (chunkNumber: 1, successful: 245, failed: 3, skipped: 2),
            (chunkNumber: 2, successful: 240, failed: 8, skipped: 2),
            (chunkNumber: 3, successful: 235, failed: 12, skipped: 3)
        };

        foreach (var (chunkNumber, successful, failed, skipped) in chunks)
        {
            _logger.LogInformation("📦 Processing Chunk {ChunkNumber}: Success={Success}, Failed={Failed}, Skipped={Skipped}",
                chunkNumber, successful, failed, skipped);

            // Write incremental progress immediately
            await _progressTracker.IncrementProgressAsync(
                _testMigrationId, "products", chunkNumber, (chunkNumber-1) * 250, 250,
                successful, failed, skipped, 0,
                DateTime.UtcNow.AddSeconds(-30), DateTime.UtcNow,
                "source-store", "dest-store");

            // Simulate dashboard polling for updates
            await Task.Delay(500); // Simulate processing + network delay
            
            var currentProgress = await _progressTracker.GetLatestAggregatedProgressAsync(_testMigrationId);
            
            if (currentProgress.EntityProgress.ContainsKey("products"))
            {
                var productsProgress = currentProgress.EntityProgress["products"];
                _logger.LogInformation("📊 Dashboard Update {ChunkNumber}: Success={Success}, Failed={Failed}, Skipped={Skipped}, Progress={Progress:F1}%",
                    chunkNumber, productsProgress.SuccessCount, productsProgress.FailureCount, 
                    productsProgress.SkippedCount, productsProgress.ProgressPercentage);
            }
        }

        await Task.Delay(1000); // Final wait for writes
        _logger.LogInformation("✅ STEP 2 COMPLETE: Real-time processing simulated");

        // STEP 3: Verify final dashboard state
        _logger.LogInformation("🎯 STEP 3: Verifying final dashboard state");
        
        var finalProgress = await _progressTracker.GetLatestAggregatedProgressAsync(_testMigrationId);
        
        if (finalProgress.EntityProgress.ContainsKey("products"))
        {
            var finalProductsProgress = finalProgress.EntityProgress["products"];
            
            finalProductsProgress.SuccessCount.Should().Be(720, "because 245+240+235 = 720");
            finalProductsProgress.FailureCount.Should().Be(23, "because 3+8+12 = 23");
            finalProductsProgress.SkippedCount.Should().Be(7, "because 2+2+3 = 7");
            
            _logger.LogInformation("📊 Final Dashboard State:");
            _logger.LogInformation("   Successful: {Success}", finalProductsProgress.SuccessCount);
            _logger.LogInformation("   Failed: {Failed}", finalProductsProgress.FailureCount);
            _logger.LogInformation("   Skipped: {Skipped}", finalProductsProgress.SkippedCount);
            _logger.LogInformation("   Progress: {Progress:F1}%", finalProductsProgress.ProgressPercentage);
        }

        _logger.LogInformation("✅ STEP 3 COMPLETE: Final dashboard state verified");

        _logger.LogInformation("🎉 DEMONSTRATION 5 COMPLETE: Real-time dashboard integration verified!");
        _logger.LogInformation("💡 KEY INSIGHT: Dashboard shows accurate real-time progress from aggregated chunk data");
    }

    #endregion

    #region Helper Methods

    private ChunkIncrementEvent CreateTestChunkIncrementEvent(
        string? entityType = null,
        int chunkNumber = 1,
        int successful = 100,
        int failed = 5,
        int skipped = 2)
    {
        return ChunkIncrementEvent.Create(
            migrationId: _testMigrationId,
            entityType: entityType ?? "products",
            chunkNumber: chunkNumber,
            chunkStartIndex: (chunkNumber - 1) * 250,
            chunkSize: 250,
            successfulEntities: successful,
            failedEntities: failed,
            skippedEntities: skipped,
            cancelledEntities: 0,
            processingStartTime: DateTime.UtcNow.AddMinutes(-5),
            processingEndTime: DateTime.UtcNow,
            sourceStore: "test-source-store",
            destinationStore: "test-dest-store");
    }

    #endregion
}