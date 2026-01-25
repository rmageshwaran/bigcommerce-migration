using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Tests;

/// <summary>
/// Simple demonstration of the incremental progress workflow
/// Shows the key integration points without complex dependencies
/// </summary>
public class SimpleWorkflowDemo : IDisposable
{
    private readonly IIncrementEventsService _incrementEventsService;
    private readonly ILogger<SimpleWorkflowDemo> _logger;
    private readonly string _testMigrationId;

    public SimpleWorkflowDemo()
    {
        _testMigrationId = $"demo-{Guid.NewGuid():N}";

        // Setup configuration for Azurite
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true"
            }!)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<SimpleWorkflowDemo>();

        _incrementEventsService = new IncrementEventsService(configuration, 
            loggerFactory.CreateLogger<IncrementEventsService>());
    }

    [Fact]
    public async Task DemonstrateIncrementalProgressWorkflow()
    {
        _logger.LogInformation("🚀 DEMONSTRATING: Incremental Progress Workflow");
        _logger.LogInformation("📋 Migration ID: {MigrationId}", _testMigrationId);

        // STEP 1: Simulate ProcessEntityChunkActivity writing chunk increments immediately
        _logger.LogInformation("📦 STEP 1: Simulating chunk processing with immediate persistence");
        
        var chunks = new[]
        {
            (entityType: "products", chunkNumber: 1, successful: 245, failed: 3, skipped: 2),
            (entityType: "products", chunkNumber: 2, successful: 238, failed: 7, skipped: 5),
            (entityType: "categories", chunkNumber: 1, successful: 95, failed: 3, skipped: 2)
        };

        foreach (var (entityType, chunkNumber, successful, failed, skipped) in chunks)
        {
            _logger.LogInformation("   Processing {EntityType} Chunk {ChunkNumber}: Success={Success}, Failed={Failed}, Skipped={Skipped}",
                entityType, chunkNumber, successful, failed, skipped);

            var chunkEvent = ChunkIncrementEvent.Create(
                migrationId: _testMigrationId,
                entityType: entityType,
                chunkNumber: chunkNumber,
                chunkStartIndex: (chunkNumber - 1) * 250,
                chunkSize: 250,
                successfulEntities: successful,
                failedEntities: failed,
                skippedEntities: skipped,
                cancelledEntities: 0,
                processingStartTime: DateTime.UtcNow.AddMinutes(-2),
                processingEndTime: DateTime.UtcNow,
                sourceStore: "source-123",
                destinationStore: "dest-456");

            // This is the NEW path - immediate persistence
            await _incrementEventsService.WriteChunkIncrementAsync(chunkEvent);
        }

        _logger.LogInformation("✅ STEP 1 COMPLETE: All chunks written immediately to database");

        // STEP 2: Demonstrate real-time aggregation (what GetLatestAggregatedProgressAsync would do)
        _logger.LogInformation("📊 STEP 2: Demonstrating real-time aggregation");
        
        await Task.Delay(1000); // Give time for writes to complete
        
        var aggregatedProgress = await _incrementEventsService.GetAggregatedProgressAsync(_testMigrationId);
        
        _logger.LogInformation("📈 Aggregated Results:");
        foreach (var (entityType, summary) in aggregatedProgress)
        {
            _logger.LogInformation("   {EntityType}: Success={Success}, Failed={Failed}, Skipped={Skipped}, Chunks={Chunks}",
                entityType, summary.TotalSuccessful, summary.TotalFailed, summary.TotalSkipped, summary.TotalChunks);
        }

        // STEP 3: Verify the data is preserved (this is what would update the main tables)
        _logger.LogInformation("🔍 STEP 3: Verifying data preservation");
        
        aggregatedProgress.Should().HaveCount(2, "because we have products and categories");
        
        var productsProgress = aggregatedProgress["products"];
        productsProgress.TotalSuccessful.Should().Be(483, "because 245 + 238 = 483");
        productsProgress.TotalFailed.Should().Be(10, "because 3 + 7 = 10");
        productsProgress.TotalSkipped.Should().Be(7, "because 2 + 5 = 7");
        productsProgress.TotalChunks.Should().Be(2, "because we processed 2 product chunks");

        var categoriesProgress = aggregatedProgress["categories"];
        categoriesProgress.TotalSuccessful.Should().Be(95);
        categoriesProgress.TotalFailed.Should().Be(3);
        categoriesProgress.TotalSkipped.Should().Be(2);
        categoriesProgress.TotalChunks.Should().Be(1);

        _logger.LogInformation("✅ STEP 3 COMPLETE: Data verified - ready for table updates");

        // STEP 4: Show cancellation safety
        _logger.LogInformation("🛑 STEP 4: Demonstrating cancellation safety");
        _logger.LogInformation("   If migration were cancelled now, we would preserve:");
        _logger.LogInformation("   • Products: {Success} successful, {Failed} failed, {Skipped} skipped",
            productsProgress.TotalSuccessful, productsProgress.TotalFailed, productsProgress.TotalSkipped);
        _logger.LogInformation("   • Categories: {Success} successful, {Failed} failed, {Skipped} skipped",
            categoriesProgress.TotalSuccessful, categoriesProgress.TotalFailed, categoriesProgress.TotalSkipped);

        var totalPreserved = productsProgress.TotalSuccessful + categoriesProgress.TotalSuccessful;
        _logger.LogInformation("   • Total preserved: {TotalPreserved} entities (instead of 0 with old approach)", totalPreserved);

        totalPreserved.Should().BeGreaterThan(500, "because substantial work was preserved");

        _logger.LogInformation("✅ STEP 4 COMPLETE: Cancellation safety demonstrated");

        _logger.LogInformation("🎉 WORKFLOW DEMONSTRATION COMPLETE!");
        _logger.LogInformation("💡 This shows how chunk data flows immediately to database and can be aggregated for table updates");
    }

    public void Dispose()
    {
        try
        {
            _incrementEventsService.DeleteMigrationIncrementsAsync(_testMigrationId).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup test data for {MigrationId}", _testMigrationId);
        }
    }
}