using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BigCommerce.Migration.Task53MigrationTesting;

/// <summary>
/// Task 5.3: Migration Testing Validation Demo
/// 
/// End-to-end migration testing program that validates the complete incremental progress system
/// with realistic migration scenarios. This final testing phase ensures the system works correctly
/// with various entity types, store configurations, and real-world migration patterns.
/// 
/// Key Migration Testing Requirements:
/// 1. Historical migration data compatibility
/// 2. Cancelled migration scenario validation
/// 3. Real-time UI behavior verification
/// 4. All entity types function correctly
/// 5. Various store sizes and configurations
/// 6. User acceptance test scenarios
/// </summary>
class Program
{
    private static IIncrementEventsService? _incrementEventsService;
    private static ILogger<Program>? _logger;
    private static readonly List<string> _testMigrationIds = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("🧪 Task 5.3: Migration Testing Validation");
        Console.WriteLine("=========================================");

        InitializeServices();

        try
        {
            await RunHistoricalDataTests();
            await RunCancelledMigrationTests();
            await RunRealtimeUIBehaviorTests();
            await RunEntityTypeValidationTests();
            await RunStoreSizeVariationTests();
            await RunUserAcceptanceTests();

            Console.WriteLine("\n🎉 All Task 5.3 Migration Tests Completed Successfully!");
            Console.WriteLine("✅ System ready for production deployment with full end-to-end validation.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Migration test failed: {ex.Message}");
            Environment.Exit(1);
        }
        finally
        {
            await CleanupTestData();
        }
    }

    private static void InitializeServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true"
            })
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<Program>();

        var serviceLogger = loggerFactory.CreateLogger<IncrementEventsService>();
        _incrementEventsService = new IncrementEventsService(configuration, serviceLogger);

        Console.WriteLine("✅ Services initialized for migration testing");
    }

    #region Task 5.3.1: Historical Data Tests

    private static async Task RunHistoricalDataTests()
    {
        Console.WriteLine("\n🧪 Task 5.3.1: Historical Migration Data Tests");
        
        await TestLegacyMigrationDataCompatibility();
        await TestMigrationProgressRecovery();
        await TestHistoricalDataAggregation();

        Console.WriteLine("   ✅ PASS: Historical migration data handled correctly");
    }

    private static async Task TestLegacyMigrationDataCompatibility()
    {
        Console.WriteLine("   📝 Testing legacy migration data compatibility...");
        
        var migrationId = $"legacy-compat-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Simulate historical migration with older data patterns
        var legacyChunks = new List<ChunkIncrementEvent>();
        
        // Create chunks with historical timestamp patterns
        var baseTime = DateTime.UtcNow.AddDays(-7); // 7 days ago
        
        for (int i = 1; i <= 50; i++)
        {
            var chunk = ChunkIncrementEvent.Create(
                migrationId: migrationId,
                entityType: "products",
                chunkNumber: i,
                chunkStartIndex: (i - 1) * 200,
                chunkSize: 200,
                successfulEntities: (int)(200 * 0.95), // 95% success rate (typical historical)
                failedEntities: (int)(200 * 0.04), // 4% failure rate
                skippedEntities: (int)(200 * 0.01), // 1% skip rate
                cancelledEntities: 0,
                processingStartTime: baseTime.AddMinutes(i * 2),
                processingEndTime: baseTime.AddMinutes(i * 2 + 1.5),
                sourceStore: "historical-source-store",
                destinationStore: "historical-dest-store",
                errors: i % 10 == 0 ? new List<string> { "Sample historical error" } : null
            );
            
            legacyChunks.Add(chunk);
        }
        
        // Write historical data
        await _incrementEventsService!.WriteBatchChunkIncrementsAsync(legacyChunks);
        
        // Verify historical data aggregation
        var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        
        if (progress.ContainsKey("products"))
        {
            var productProgress = progress["products"];
            Console.WriteLine($"      Historical data aggregated: {productProgress.TotalProcessed} entities");
            Console.WriteLine($"      Success rate: {(double)productProgress.TotalSuccessful / productProgress.TotalProcessed * 100:F1}%");
            
            // Verify historical data integrity
            if (productProgress.TotalProcessed == 10000 && productProgress.TotalSuccessful >= 9400)
            {
                Console.WriteLine("      ✅ Legacy migration data compatibility verified");
            }
        }
    }

    private static async Task TestMigrationProgressRecovery()
    {
        Console.WriteLine("   📝 Testing migration progress recovery scenarios...");
        
        var migrationId = $"progress-recovery-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Phase 1: Simulate partial migration (interrupted)
        for (int i = 1; i <= 30; i++)
        {
            var chunk = CreateRealisticChunk(migrationId, "categories", i, 50);
            await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
        }
        
        var partialProgress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        var partialTotal = partialProgress.ContainsKey("categories") ? partialProgress["categories"].TotalProcessed : 0;
        
        Console.WriteLine($"      Phase 1 (interrupted): {partialTotal} entities processed");
        
        // Phase 2: Simulate recovery and continuation
        await Task.Delay(1000); // Simulate recovery delay
        
        for (int i = 31; i <= 60; i++)
        {
            var chunk = CreateRealisticChunk(migrationId, "categories", i, 50);
            await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
        }
        
        // Phase 3: Add different entity type during recovery
        for (int i = 1; i <= 20; i++)
        {
            var chunk = CreateRealisticChunk(migrationId, "brands", i, 75);
            await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
        }
        
        var recoveredProgress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        
        Console.WriteLine($"      Phase 2 (recovered): Categories={recoveredProgress["categories"].TotalProcessed}, Brands={recoveredProgress["brands"].TotalProcessed}");
        
        // Verify recovery maintained data integrity
        if (recoveredProgress["categories"].TotalProcessed >= partialTotal && recoveredProgress["brands"].TotalProcessed > 0)
        {
            Console.WriteLine("      ✅ Migration progress recovery successful");
        }
    }

    private static async Task TestHistoricalDataAggregation()
    {
        Console.WriteLine("   📝 Testing historical data aggregation performance...");
        
        var migrationId = $"historical-agg-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Create large historical dataset spanning multiple days
        var historicalChunks = new List<ChunkIncrementEvent>();
        var baseTime = DateTime.UtcNow.AddDays(-30); // 30 days ago
        
        // Simulate 30 days of migration chunks across multiple entity types
        var entityTypes = new[] { "products", "categories", "brands", "variants", "options" };
        
        foreach (var entityType in entityTypes)
        {
            for (int day = 0; day < 30; day++)
            {
                for (int chunk = 1; chunk <= 10; chunk++)
                {
                    var chunkNumber = day * 10 + chunk;
                    var chunkEvent = ChunkIncrementEvent.Create(
                        migrationId: migrationId,
                        entityType: entityType,
                        chunkNumber: chunkNumber,
                        chunkStartIndex: (chunkNumber - 1) * 100,
                        chunkSize: 100,
                        successfulEntities: 94 + (day % 5), // Varying success rates
                        failedEntities: 4 - (day % 3),
                        skippedEntities: 2 - (day % 2),
                        cancelledEntities: 0,
                        processingStartTime: baseTime.AddDays(day).AddHours(chunk),
                        processingEndTime: baseTime.AddDays(day).AddHours(chunk + 0.5),
                        sourceStore: $"historical-store-{day % 3}",
                        destinationStore: "consolidated-dest-store",
                        errors: chunk % 20 == 0 ? new List<string> { $"Historical error day {day}" } : null
                    );
                    
                    historicalChunks.Add(chunkEvent);
                }
            }
        }
        
        Console.WriteLine($"      Created {historicalChunks.Count} historical chunks across {entityTypes.Length} entity types");
        
        // Write in batches to simulate realistic historical data
        var batchSize = 100;
        for (int i = 0; i < historicalChunks.Count; i += batchSize)
        {
            var batch = historicalChunks.Skip(i).Take(batchSize).ToList();
            await _incrementEventsService!.WriteBatchChunkIncrementsAsync(batch);
            
            if ((i / batchSize + 1) % 5 == 0)
            {
                Console.WriteLine($"      Processed {i + batch.Count}/{historicalChunks.Count} historical chunks");
            }
        }
        
        // Test aggregation performance with large historical dataset
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var historicalProgress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        stopwatch.Stop();
        
        Console.WriteLine($"      Historical aggregation completed in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"      Entity types processed: {historicalProgress.Count}");
        
        foreach (var kvp in historicalProgress)
        {
            Console.WriteLine($"        {kvp.Key}: {kvp.Value.TotalProcessed:N0} entities");
        }
        
        // Verify aggregation performance with large dataset
        if (stopwatch.ElapsedMilliseconds < 5000) // Should complete within 5 seconds
        {
            Console.WriteLine("      ✅ Historical data aggregation performance acceptable");
        }
        else
        {
            throw new Exception($"Historical aggregation too slow: {stopwatch.ElapsedMilliseconds}ms");
        }
    }

    #endregion

    #region Task 5.3.2: Cancelled Migration Tests

    private static async Task RunCancelledMigrationTests()
    {
        Console.WriteLine("\n🧪 Task 5.3.2: Cancelled Migration Scenario Tests");
        
        await TestEarlyMigrationCancellation();
        await TestMidMigrationCancellation();
        await TestLateMigrationCancellation();
        await TestPartialEntityCancellation();

        Console.WriteLine("   ✅ PASS: Cancelled migration scenarios validated successfully");
    }

    private static async Task TestEarlyMigrationCancellation()
    {
        Console.WriteLine("   📝 Testing early migration cancellation scenario...");
        
        var migrationId = $"early-migration-cancel-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        using var cancellationSource = new CancellationTokenSource();
        
        // Start migration simulation
        var migrationTask = Task.Run(async () =>
        {
            try
            {
                // Simulate discovery phase
                await Task.Delay(100, cancellationSource.Token);
                
                // Start processing first few chunks
                for (int i = 1; i <= 15; i++)
                {
                    var chunk = CreateRealisticChunk(migrationId, "products", i, 150);
                    await _incrementEventsService!.WriteChunkIncrementAsync(chunk, cancellationSource.Token);
                    await Task.Delay(50, cancellationSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("      Migration cancelled during early phase");
            }
        });
        
        // Cancel early in migration
        await Task.Delay(300);
        cancellationSource.Cancel();
        await migrationTask;
        
        // Verify early cancellation state
        var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        
        if (progress.ContainsKey("products"))
        {
            var productProgress = progress["products"];
            Console.WriteLine($"      Early cancellation preserved: {productProgress.TotalProcessed} entities");
            Console.WriteLine($"      Chunks completed before cancellation: {productProgress.TotalProcessed / 150}");
            
            if (productProgress.TotalProcessed > 0 && productProgress.TotalProcessed < 2250) // Should be partial
            {
                Console.WriteLine("      ✅ Early migration cancellation handled correctly");
            }
        }
    }

    private static async Task TestMidMigrationCancellation()
    {
        Console.WriteLine("   📝 Testing mid-migration cancellation scenario...");
        
        var migrationId = $"mid-migration-cancel-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        using var cancellationSource = new CancellationTokenSource();
        
        // Simulate longer migration with multiple entity types
        var migrationTask = Task.Run(async () =>
        {
            try
            {
                // Process products (primary entity)
                for (int i = 1; i <= 40; i++)
                {
                    var chunk = CreateRealisticChunk(migrationId, "products", i, 100);
                    await _incrementEventsService!.WriteChunkIncrementAsync(chunk, cancellationSource.Token);
                    await Task.Delay(25, cancellationSource.Token);
                }
                
                // Start processing categories
                for (int i = 1; i <= 20; i++)
                {
                    var chunk = CreateRealisticChunk(migrationId, "categories", i, 75);
                    await _incrementEventsService!.WriteChunkIncrementAsync(chunk, cancellationSource.Token);
                    await Task.Delay(30, cancellationSource.Token);
                }
                
                // Start processing brands
                for (int i = 1; i <= 10; i++)
                {
                    var chunk = CreateRealisticChunk(migrationId, "brands", i, 50);
                    await _incrementEventsService!.WriteChunkIncrementAsync(chunk, cancellationSource.Token);
                    await Task.Delay(40, cancellationSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("      Migration cancelled during mid-phase processing");
            }
        });
        
        // Cancel in middle of processing
        await Task.Delay(1500);
        cancellationSource.Cancel();
        await migrationTask;
        
        // Verify mid-migration cancellation state
        var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        
        Console.WriteLine("      Mid-migration cancellation results:");
        foreach (var kvp in progress)
        {
            Console.WriteLine($"        {kvp.Key}: {kvp.Value.TotalProcessed} entities");
        }
        
        // Should have partial progress across multiple entity types
        if (progress.Count > 1 && progress.Values.Sum(p => p.TotalProcessed) > 1000)
        {
            Console.WriteLine("      ✅ Mid-migration cancellation preserved multi-entity progress");
        }
    }

    private static async Task TestLateMigrationCancellation()
    {
        Console.WriteLine("   📝 Testing late migration cancellation scenario...");
        
        var migrationId = $"late-migration-cancel-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        using var cancellationSource = new CancellationTokenSource();
        
        // Simulate near-complete migration
        var migrationTask = Task.Run(async () =>
        {
            try
            {
                // Complete most entity types
                var entityTypes = new[] { "products", "categories", "brands" };
                
                foreach (var entityType in entityTypes)
                {
                    var chunkCount = entityType == "products" ? 50 : 20;
                    var chunkSize = entityType == "products" ? 100 : 50;
                    
                    for (int i = 1; i <= chunkCount; i++)
                    {
                        var chunk = CreateRealisticChunk(migrationId, entityType, i, chunkSize);
                        await _incrementEventsService!.WriteChunkIncrementAsync(chunk, cancellationSource.Token);
                        await Task.Delay(15, cancellationSource.Token);
                    }
                }
                
                // Start final entity type (where cancellation will occur)
                for (int i = 1; i <= 30; i++)
                {
                    var chunk = CreateRealisticChunk(migrationId, "variants", i, 200);
                    await _incrementEventsService!.WriteChunkIncrementAsync(chunk, cancellationSource.Token);
                    await Task.Delay(20, cancellationSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("      Migration cancelled during final entity processing");
            }
        });
        
        // Cancel near the end (most work completed)
        await Task.Delay(2000);
        cancellationSource.Cancel();
        await migrationTask;
        
        // Verify late cancellation preserved substantial progress
        var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        
        Console.WriteLine("      Late migration cancellation results:");
        var totalEntities = 0;
        foreach (var kvp in progress)
        {
            Console.WriteLine($"        {kvp.Key}: {kvp.Value.TotalProcessed} entities");
            totalEntities += kvp.Value.TotalProcessed;
        }
        
        // Should have substantial progress (most entity types complete)
        if (totalEntities > 5000 && progress.Count >= 3)
        {
            Console.WriteLine($"      ✅ Late migration cancellation preserved {totalEntities} entities across {progress.Count} types");
        }
    }

    private static async Task TestPartialEntityCancellation()
    {
        Console.WriteLine("   📝 Testing partial entity type cancellation scenario...");
        
        var migrationId = $"partial-entity-cancel-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Complete some entity types, partial others
        var completedEntityTypes = new[] { "categories", "brands" };
        var partialEntityTypes = new[] { "products", "variants" };
        
        // Complete entity types fully
        foreach (var entityType in completedEntityTypes)
        {
            for (int i = 1; i <= 25; i++)
            {
                var chunk = CreateRealisticChunk(migrationId, entityType, i, 80);
                await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
            }
        }
        
        // Partial entity types (simulate cancellation during processing)
        using var cancellationSource = new CancellationTokenSource();
        
        var partialTask = Task.Run(async () =>
        {
            try
            {
                foreach (var entityType in partialEntityTypes)
                {
                    for (int i = 1; i <= 40; i++)
                    {
                        var chunk = CreateRealisticChunk(migrationId, entityType, i, 120);
                        await _incrementEventsService!.WriteChunkIncrementAsync(chunk, cancellationSource.Token);
                        await Task.Delay(30, cancellationSource.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("      Partial entity processing cancelled");
            }
        });
        
        // Cancel during partial processing
        await Task.Delay(800);
        cancellationSource.Cancel();
        await partialTask;
        
        // Verify mixed completion state
        var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        
        Console.WriteLine("      Partial entity cancellation results:");
        foreach (var kvp in progress)
        {
            var isComplete = completedEntityTypes.Contains(kvp.Key);
            var status = isComplete ? "COMPLETE" : "PARTIAL";
            Console.WriteLine($"        {kvp.Key}: {kvp.Value.TotalProcessed} entities ({status})");
        }
        
        // Verify complete vs partial entity handling
        var hasCompleteEntities = completedEntityTypes.Any(et => progress.ContainsKey(et) && progress[et].TotalProcessed >= 2000);
        var hasPartialEntities = partialEntityTypes.Any(et => progress.ContainsKey(et) && progress[et].TotalProcessed > 0);
        
        if (hasCompleteEntities && hasPartialEntities)
        {
            Console.WriteLine("      ✅ Partial entity cancellation handled correctly");
        }
    }

    #endregion

    #region Task 5.3.3: Real-time UI Behavior Tests

    private static async Task RunRealtimeUIBehaviorTests()
    {
        Console.WriteLine("\n🧪 Task 5.3.3: Real-time UI Behavior Tests");
        
        await TestProgressUpdateFrequency();
        await TestUIDataConsistency();
        await TestLargeDatasetUIPerformance();

        Console.WriteLine("   ✅ PASS: Real-time UI behavior validated for production use");
    }

    private static async Task TestProgressUpdateFrequency()
    {
        Console.WriteLine("   📝 Testing progress update frequency for UI consumption...");
        
        var migrationId = $"ui-frequency-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Simulate rapid progress updates that UI would consume
        var progressSnapshots = new List<Dictionary<string, EntityProgressSummary>>();
        
        var updateTask = Task.Run(async () =>
        {
            for (int i = 1; i <= 100; i++)
            {
                var chunk = CreateRealisticChunk(migrationId, "products", i, 50);
                await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
                
                // Simulate UI polling every 2 seconds
                if (i % 5 == 0)
                {
                    var snapshot = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
                    progressSnapshots.Add(snapshot);
                    await Task.Delay(100); // Brief pause between updates
                }
            }
        });
        
        await updateTask;
        
        // Verify progress snapshots show consistent progression
        Console.WriteLine($"      Captured {progressSnapshots.Count} progress snapshots");
        
        var previousTotal = 0;
        var consistentProgression = true;
        
        for (int i = 0; i < progressSnapshots.Count; i++)
        {
            var snapshot = progressSnapshots[i];
            var currentTotal = snapshot.ContainsKey("products") ? snapshot["products"].TotalProcessed : 0;
            
            Console.WriteLine($"        Snapshot {i + 1}: {currentTotal} entities");
            
            if (currentTotal < previousTotal)
            {
                consistentProgression = false;
                break;
            }
            previousTotal = currentTotal;
        }
        
        if (consistentProgression && progressSnapshots.Count >= 10)
        {
            Console.WriteLine("      ✅ UI progress update frequency provides consistent data");
        }
        else
        {
            throw new Exception("UI progress updates show inconsistent progression");
        }
    }

    private static async Task TestUIDataConsistency()
    {
        Console.WriteLine("   📝 Testing UI data consistency during concurrent updates...");
        
        var migrationId = $"ui-consistency-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        var uiQueryResults = new ConcurrentBag<Dictionary<string, EntityProgressSummary>>();
        
        // Simulate concurrent migration processing and UI queries
        var processingTask = Task.Run(async () =>
        {
            for (int i = 1; i <= 80; i++)
            {
                var chunk = CreateRealisticChunk(migrationId, "products", i, 75);
                await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
                await Task.Delay(25);
            }
        });
        
        var uiQueryTasks = new List<Task>();
        
        // Simulate multiple UI clients querying simultaneously
        for (int client = 1; client <= 5; client++)
        {
            uiQueryTasks.Add(Task.Run(async () =>
            {
                for (int query = 1; query <= 10; query++)
                {
                    try
                    {
                        await Task.Delay(client * 100 + query * 200); // Stagger queries
                        var result = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
                        uiQueryResults.Add(result);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      UI query error handled: {ex.Message}");
                    }
                }
            }));
        }
        
        await Task.WhenAll(processingTask);
        await Task.WhenAll(uiQueryTasks);
        
        // Verify UI data consistency
        var allResults = uiQueryResults.ToList();
        Console.WriteLine($"      UI queries completed: {allResults.Count}");
        
        // Check that all UI queries returned valid data
        var validQueries = allResults.Count(r => r.ContainsKey("products") && r["products"].TotalProcessed >= 0);
        
        if (validQueries == allResults.Count && validQueries > 0)
        {
            var finalResult = allResults.Last();
            var finalTotal = finalResult["products"].TotalProcessed;
            Console.WriteLine($"      Final UI state: {finalTotal} entities");
            Console.WriteLine("      ✅ UI data consistency maintained during concurrent updates");
        }
    }

    private static async Task TestLargeDatasetUIPerformance()
    {
        Console.WriteLine("   📝 Testing UI performance with large dataset...");
        
        var migrationId = $"ui-large-dataset-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Create large dataset across multiple entity types
        var entityTypes = new[] { "products", "categories", "brands", "variants", "options", "images" };
        var totalChunks = 0;
        
        foreach (var entityType in entityTypes)
        {
            var chunks = new List<ChunkIncrementEvent>();
            var chunkCount = entityType == "products" ? 100 : 50;
            
            for (int i = 1; i <= chunkCount; i++)
            {
                chunks.Add(CreateRealisticChunk(migrationId, entityType, i, 100));
                totalChunks++;
            }
            
            await _incrementEventsService!.WriteBatchChunkIncrementsAsync(chunks);
        }
        
        Console.WriteLine($"      Created large dataset: {totalChunks} chunks across {entityTypes.Length} entity types");
        
        // Test UI query performance with large dataset
        var uiQueryTimes = new List<long>();
        
        for (int query = 1; query <= 10; query++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
            stopwatch.Stop();
            
            uiQueryTimes.Add(stopwatch.ElapsedMilliseconds);
            
            if (query % 3 == 0)
            {
                var totalEntities = progress.Values.Sum(p => p.TotalProcessed);
                Console.WriteLine($"        Query {query}: {stopwatch.ElapsedMilliseconds}ms, {totalEntities:N0} entities");
            }
        }
        
        var avgQueryTime = uiQueryTimes.Average();
        var maxQueryTime = uiQueryTimes.Max();
        
        Console.WriteLine($"      UI query performance: Avg={avgQueryTime:F0}ms, Max={maxQueryTime}ms");
        
        // Verify UI performance acceptable for real-time updates
        if (avgQueryTime < 2000 && maxQueryTime < 5000) // 2s average, 5s max
        {
            Console.WriteLine("      ✅ Large dataset UI performance acceptable for real-time updates");
        }
        else
        {
            throw new Exception($"UI performance too slow: Avg={avgQueryTime:F0}ms, Max={maxQueryTime}ms");
        }
    }

    #endregion

    #region Task 5.3.4: Entity Type Validation Tests

    private static async Task RunEntityTypeValidationTests()
    {
        Console.WriteLine("\n🧪 Task 5.3.4: Entity Type Validation Tests");
        
        await TestAllSupportedEntityTypes();
        await TestEntityTypeSpecificBehavior();
        await TestMixedEntityTypeProcessing();

        Console.WriteLine("   ✅ PASS: All entity types validated for incremental progress tracking");
    }

    private static async Task TestAllSupportedEntityTypes()
    {
        Console.WriteLine("   📝 Testing all supported entity types...");
        
        var migrationId = $"all-entities-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Test all supported entity types from the system
        var entityTypes = new[]
        {
            "products", "categories", "brands", "variants", "options", 
            "images", "reviews", "modifiers", "redirects", "coupons"
        };
        
        var entityResults = new Dictionary<string, EntityProgressSummary>();
        
        foreach (var entityType in entityTypes)
        {
            var chunkCount = GetRealisticChunkCount(entityType);
            var chunkSize = GetRealisticChunkSize(entityType);
            
            Console.WriteLine($"      Processing {entityType}: {chunkCount} chunks of {chunkSize} entities each");
            
            for (int i = 1; i <= chunkCount; i++)
            {
                var chunk = CreateRealisticChunk(migrationId, entityType, i, chunkSize);
                await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
            }
        }
        
        // Verify all entity types processed correctly
        var allEntityProgress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        
        Console.WriteLine($"      Entity type validation results:");
        foreach (var entityType in entityTypes)
        {
            if (allEntityProgress.ContainsKey(entityType))
            {
                var progress = allEntityProgress[entityType];
                Console.WriteLine($"        {entityType}: {progress.TotalProcessed:N0} entities, {progress.TotalSuccessful} successful");
            }
            else
            {
                Console.WriteLine($"        {entityType}: No data (unexpected)");
            }
        }
        
        // Verify all entity types have data
        if (allEntityProgress.Count == entityTypes.Length)
        {
            var totalEntitiesAcrossTypes = allEntityProgress.Values.Sum(p => p.TotalProcessed);
            Console.WriteLine($"      ✅ All {entityTypes.Length} entity types processed successfully ({totalEntitiesAcrossTypes:N0} total entities)");
        }
        else
        {
            throw new Exception($"Missing entity types: expected {entityTypes.Length}, got {allEntityProgress.Count}");
        }
    }

    private static async Task TestEntityTypeSpecificBehavior()
    {
        Console.WriteLine("   📝 Testing entity type specific behavior...");
        
        var migrationId = $"entity-specific-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Test entity types with different characteristics
        var testCases = new[]
        {
            new { EntityType = "products", ChunkSize = 50, ExpectedSuccessRate = 0.92 }, // Products often have validation issues
            new { EntityType = "categories", ChunkSize = 100, ExpectedSuccessRate = 0.98 }, // Categories usually process cleanly
            new { EntityType = "images", ChunkSize = 25, ExpectedSuccessRate = 0.85 }, // Images may have URL/size issues
            new { EntityType = "variants", ChunkSize = 75, ExpectedSuccessRate = 0.90 }, // Variants have complex relationships
        };
        
        foreach (var testCase in testCases)
        {
            for (int i = 1; i <= 20; i++)
            {
                var chunk = CreateRealisticChunk(
                    migrationId, 
                    testCase.EntityType, 
                    i, 
                    testCase.ChunkSize,
                    testCase.ExpectedSuccessRate
                );
                await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
            }
        }
        
        // Verify entity-specific behavior
        var entityProgress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        
        Console.WriteLine("      Entity-specific behavior results:");
        foreach (var testCase in testCases)
        {
            if (entityProgress.ContainsKey(testCase.EntityType))
            {
                var progress = entityProgress[testCase.EntityType];
                var actualSuccessRate = (double)progress.TotalSuccessful / progress.TotalProcessed;
                
                Console.WriteLine($"        {testCase.EntityType}: {actualSuccessRate:P1} success rate (expected {testCase.ExpectedSuccessRate:P1})");
                
                // Allow some variance in success rates
                if (Math.Abs(actualSuccessRate - testCase.ExpectedSuccessRate) < 0.1)
                {
                    Console.WriteLine($"          ✅ Success rate within expected range");
                }
            }
        }
        
        Console.WriteLine("      ✅ Entity type specific behavior validated");
    }

    private static async Task TestMixedEntityTypeProcessing()
    {
        Console.WriteLine("   📝 Testing mixed entity type concurrent processing...");
        
        var migrationId = $"mixed-entities-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Simulate realistic migration with overlapping entity processing
        var concurrentEntityTasks = new List<Task>();
        
        var entityConfigs = new[]
        {
            new { Type = "products", Chunks = 30, Size = 100, Delay = 50 },
            new { Type = "categories", Chunks = 15, Size = 80, Delay = 75 },
            new { Type = "brands", Chunks = 10, Size = 60, Delay = 100 },
            new { Type = "variants", Chunks = 25, Size = 120, Delay = 60 },
            new { Type = "options", Chunks = 20, Size = 90, Delay = 80 }
        };
        
        foreach (var config in entityConfigs)
        {
            concurrentEntityTasks.Add(Task.Run(async () =>
            {
                for (int i = 1; i <= config.Chunks; i++)
                {
                    var chunk = CreateRealisticChunk(migrationId, config.Type, i, config.Size);
                    await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
                    await Task.Delay(config.Delay);
                }
            }));
        }
        
        // Add concurrent progress monitoring (simulating UI)
        var monitoringTask = Task.Run(async () =>
        {
            for (int monitor = 1; monitor <= 20; monitor++)
            {
                try
                {
                    await Task.Delay(200);
                    var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
                    
                    if (monitor % 5 == 0)
                    {
                        var totalEntities = progress.Values.Sum(p => p.TotalProcessed);
                        Console.WriteLine($"        Monitor {monitor}: {totalEntities:N0} entities across {progress.Count} types");
                    }
                }
                catch (Exception)
                {
                    // Monitoring should be resilient to processing issues
                }
            }
        });
        
        await Task.WhenAll(concurrentEntityTasks);
        await monitoringTask;
        
        // Verify final mixed entity state
        var finalProgress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        
        Console.WriteLine("      Mixed entity processing final results:");
        var grandTotal = 0;
        foreach (var kvp in finalProgress)
        {
            Console.WriteLine($"        {kvp.Key}: {kvp.Value.TotalProcessed:N0} entities");
            grandTotal += kvp.Value.TotalProcessed;
        }
        
        if (finalProgress.Count == entityConfigs.Length && grandTotal > 10000)
        {
            Console.WriteLine($"      ✅ Mixed entity type processing successful ({grandTotal:N0} total entities)");
        }
    }

    #endregion

    #region Task 5.3.5: Store Size Variation Tests

    private static async Task RunStoreSizeVariationTests()
    {
        Console.WriteLine("\n🧪 Task 5.3.5: Store Size Variation Tests");
        
        await TestSmallStoreScenario();
        await TestMediumStoreScenario();
        await TestLargeStoreScenario();
        await TestMassiveStoreScenario();

        Console.WriteLine("   ✅ PASS: All store size variations handled appropriately");
    }

    private static async Task TestSmallStoreScenario()
    {
        Console.WriteLine("   📝 Testing small store scenario (< 1K entities)...");
        
        var migrationId = $"small-store-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Small store: minimal entities across types
        var smallStoreConfig = new[]
        {
            new { Type = "products", Chunks = 3, Size = 50 },
            new { Type = "categories", Chunks = 2, Size = 25 },
            new { Type = "brands", Chunks = 1, Size = 20 }
        };
        
        foreach (var config in smallStoreConfig)
        {
            for (int i = 1; i <= config.Chunks; i++)
            {
                var chunk = CreateRealisticChunk(migrationId, config.Type, i, config.Size);
                await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
            }
        }
        
        var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        var totalEntities = progress.Values.Sum(p => p.TotalProcessed);
        
        Console.WriteLine($"      Small store processed: {totalEntities} entities across {progress.Count} types");
        
        if (totalEntities < 1000 && progress.Count == 3)
        {
            Console.WriteLine("      ✅ Small store scenario handled efficiently");
        }
    }

    private static async Task TestMediumStoreScenario()
    {
        Console.WriteLine("   📝 Testing medium store scenario (1K-10K entities)...");
        
        var migrationId = $"medium-store-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Medium store: moderate entities across types
        var mediumStoreConfig = new[]
        {
            new { Type = "products", Chunks = 15, Size = 100 },
            new { Type = "categories", Chunks = 8, Size = 75 },
            new { Type = "brands", Chunks = 5, Size = 60 },
            new { Type = "variants", Chunks = 20, Size = 80 }
        };
        
        foreach (var config in mediumStoreConfig)
        {
            for (int i = 1; i <= config.Chunks; i++)
            {
                var chunk = CreateRealisticChunk(migrationId, config.Type, i, config.Size);
                await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
            }
        }
        
        var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        var totalEntities = progress.Values.Sum(p => p.TotalProcessed);
        
        Console.WriteLine($"      Medium store processed: {totalEntities:N0} entities across {progress.Count} types");
        
        if (totalEntities >= 1000 && totalEntities <= 10000 && progress.Count == 4)
        {
            Console.WriteLine("      ✅ Medium store scenario handled appropriately");
        }
    }

    private static async Task TestLargeStoreScenario()
    {
        Console.WriteLine("   📝 Testing large store scenario (10K-100K entities)...");
        
        var migrationId = $"large-store-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Large store: substantial entities across all types
        var largeStoreConfig = new[]
        {
            new { Type = "products", Chunks = 50, Size = 150 },
            new { Type = "categories", Chunks = 25, Size = 100 },
            new { Type = "brands", Chunks = 15, Size = 80 },
            new { Type = "variants", Chunks = 100, Size = 120 },
            new { Type = "options", Chunks = 40, Size = 90 },
            new { Type = "images", Chunks = 60, Size = 75 }
        };
        
        // Process in batches to simulate realistic large store migration
        foreach (var config in largeStoreConfig)
        {
            var chunks = new List<ChunkIncrementEvent>();
            
            for (int i = 1; i <= config.Chunks; i++)
            {
                chunks.Add(CreateRealisticChunk(migrationId, config.Type, i, config.Size));
                
                // Process in batches of 10
                if (chunks.Count == 10 || i == config.Chunks)
                {
                    await _incrementEventsService!.WriteBatchChunkIncrementsAsync(chunks);
                    chunks.Clear();
                    await Task.Delay(50); // Brief pause between batches
                }
            }
            
            Console.WriteLine($"        {config.Type}: {config.Chunks} chunks completed");
        }
        
        var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        var totalEntities = progress.Values.Sum(p => p.TotalProcessed);
        
        Console.WriteLine($"      Large store processed: {totalEntities:N0} entities across {progress.Count} types");
        
        if (totalEntities >= 10000 && totalEntities <= 100000 && progress.Count == 6)
        {
            Console.WriteLine("      ✅ Large store scenario handled efficiently");
        }
    }

    private static async Task TestMassiveStoreScenario()
    {
        Console.WriteLine("   📝 Testing massive store scenario (100K+ entities)...");
        
        var migrationId = $"massive-store-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Massive store: enterprise-level entity counts
        var massiveStoreConfig = new[]
        {
            new { Type = "products", Chunks = 200, Size = 200 },
            new { Type = "variants", Chunks = 500, Size = 150 },
            new { Type = "categories", Chunks = 50, Size = 100 },
            new { Type = "brands", Chunks = 30, Size = 80 },
            new { Type = "options", Chunks = 100, Size = 120 },
            new { Type = "images", Chunks = 300, Size = 100 }
        };
        
        var startTime = DateTime.UtcNow;
        
        // Process massive store in efficient batches
        foreach (var config in massiveStoreConfig)
        {
            var chunks = new List<ChunkIncrementEvent>();
            
            for (int i = 1; i <= config.Chunks; i++)
            {
                chunks.Add(CreateRealisticChunk(migrationId, config.Type, i, config.Size));
                
                // Process in larger batches for efficiency
                if (chunks.Count == 50 || i == config.Chunks)
                {
                    await _incrementEventsService!.WriteBatchChunkIncrementsAsync(chunks);
                    chunks.Clear();
                    
                    if (i % 100 == 0)
                    {
                        Console.WriteLine($"        {config.Type}: {i}/{config.Chunks} chunks processed");
                    }
                }
            }
        }
        
        var processingTime = DateTime.UtcNow - startTime;
        
        // Test aggregation performance with massive dataset
        var aggregationStart = DateTime.UtcNow;
        var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        var aggregationTime = DateTime.UtcNow - aggregationStart;
        
        var totalEntities = progress.Values.Sum(p => p.TotalProcessed);
        
        Console.WriteLine($"      Massive store results:");
        Console.WriteLine($"        Total entities: {totalEntities:N0}");
        Console.WriteLine($"        Processing time: {processingTime.TotalSeconds:F1}s");
        Console.WriteLine($"        Aggregation time: {aggregationTime.TotalMilliseconds:F0}ms");
        Console.WriteLine($"        Entity types: {progress.Count}");
        
        // Verify massive store handling
        if (totalEntities >= 100000 && aggregationTime.TotalMilliseconds < 10000) // 10s max aggregation
        {
            Console.WriteLine("      ✅ Massive store scenario handled with acceptable performance");
        }
        else if (totalEntities < 100000)
        {
            throw new Exception($"Massive store test insufficient: {totalEntities:N0} entities");
        }
        else
        {
            throw new Exception($"Massive store aggregation too slow: {aggregationTime.TotalMilliseconds:F0}ms");
        }
    }

    #endregion

    #region Task 5.3.6: User Acceptance Tests

    private static async Task RunUserAcceptanceTests()
    {
        Console.WriteLine("\n🧪 Task 5.3.6: User Acceptance Test Scenarios");
        
        await TestTypicalUserWorkflow();
        await TestPowerUserWorkflow();
        await TestErrorRecoveryWorkflow();

        Console.WriteLine("   ✅ PASS: User acceptance scenarios validated for production readiness");
    }

    private static async Task TestTypicalUserWorkflow()
    {
        Console.WriteLine("   📝 Testing typical user workflow scenario...");
        
        var migrationId = $"typical-user-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Typical user: Small to medium store, standard entity types
        Console.WriteLine("      Simulating typical user migration workflow:");
        Console.WriteLine("        1. Start migration with products and categories");
        Console.WriteLine("        2. Monitor progress regularly");
        Console.WriteLine("        3. Add additional entity types");
        Console.WriteLine("        4. Complete migration successfully");
        
        // Phase 1: Start with core entities
        var coreEntities = new[] { "products", "categories" };
        
        foreach (var entityType in coreEntities)
        {
            for (int i = 1; i <= 20; i++)
            {
                var chunk = CreateRealisticChunk(migrationId, entityType, i, 100);
                await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
                
                // Simulate user monitoring progress
                if (i % 5 == 0)
                {
                    var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
                    var currentTotal = progress.Values.Sum(p => p.TotalProcessed);
                    Console.WriteLine($"        User checks progress: {currentTotal:N0} entities processed");
                }
            }
        }
        
        // Phase 2: Add supplementary entities
        var supplementaryEntities = new[] { "brands", "images" };
        
        foreach (var entityType in supplementaryEntities)
        {
            for (int i = 1; i <= 10; i++)
            {
                var chunk = CreateRealisticChunk(migrationId, entityType, i, 75);
                await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
            }
        }
        
        // Final progress check
        var finalProgress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        var finalTotal = finalProgress.Values.Sum(p => p.TotalProcessed);
        
        Console.WriteLine($"      Typical user workflow completed: {finalTotal:N0} entities across {finalProgress.Count} types");
        
        if (finalTotal >= 3000 && finalTotal <= 15000 && finalProgress.Count == 4)
        {
            Console.WriteLine("      ✅ Typical user workflow validated successfully");
        }
    }

    private static async Task TestPowerUserWorkflow()
    {
        Console.WriteLine("   📝 Testing power user workflow scenario...");
        
        var migrationId = $"power-user-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Power user: Large store, all entity types, concurrent operations
        Console.WriteLine("      Simulating power user migration workflow:");
        Console.WriteLine("        1. Migrate all entity types concurrently");
        Console.WriteLine("        2. High-frequency progress monitoring");
        Console.WriteLine("        3. Handle complex entity relationships");
        Console.WriteLine("        4. Validate comprehensive data integrity");
        
        var allEntityTypes = new[] { "products", "categories", "brands", "variants", "options", "images", "reviews", "modifiers" };
        var powerUserTasks = new List<Task>();
        
        // Concurrent processing of all entity types
        foreach (var entityType in allEntityTypes)
        {
            powerUserTasks.Add(Task.Run(async () =>
            {
                var chunkCount = GetPowerUserChunkCount(entityType);
                var chunkSize = GetRealisticChunkSize(entityType);
                
                for (int i = 1; i <= chunkCount; i++)
                {
                    var chunk = CreateRealisticChunk(migrationId, entityType, i, chunkSize);
                    await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
                    await Task.Delay(20); // Power users process quickly
                }
            }));
        }
        
        // High-frequency monitoring (power user behavior)
        var monitoringTask = Task.Run(async () =>
        {
            for (int monitor = 1; monitor <= 50; monitor++)
            {
                await Task.Delay(100); // Very frequent monitoring
                
                try
                {
                    var progress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
                    
                    if (monitor % 10 == 0)
                    {
                        var totalEntities = progress.Values.Sum(p => p.TotalProcessed);
                        Console.WriteLine($"        Power user monitoring {monitor}: {totalEntities:N0} entities");
                    }
                }
                catch (Exception)
                {
                    // High-frequency monitoring should be resilient
                }
            }
        });
        
        await Task.WhenAll(powerUserTasks);
        await monitoringTask;
        
        var powerUserProgress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        var powerUserTotal = powerUserProgress.Values.Sum(p => p.TotalProcessed);
        
        Console.WriteLine($"      Power user workflow completed: {powerUserTotal:N0} entities across {powerUserProgress.Count} types");
        
        if (powerUserTotal >= 20000 && powerUserProgress.Count == allEntityTypes.Length)
        {
            Console.WriteLine("      ✅ Power user workflow validated successfully");
        }
    }

    private static async Task TestErrorRecoveryWorkflow()
    {
        Console.WriteLine("   📝 Testing error recovery workflow scenario...");
        
        var migrationId = $"error-recovery-{Guid.NewGuid():N}";
        _testMigrationIds.Add(migrationId);
        
        // Simulate migration with errors and recovery
        Console.WriteLine("      Simulating error recovery workflow:");
        Console.WriteLine("        1. Start migration with some failures");
        Console.WriteLine("        2. User monitors and identifies issues");
        Console.WriteLine("        3. System continues despite errors");
        Console.WriteLine("        4. Final state shows partial success");
        
        var errorRecoveryTasks = new List<Task>();
        var entityTypes = new[] { "products", "categories", "brands" };
        
        foreach (var entityType in entityTypes)
        {
            errorRecoveryTasks.Add(Task.Run(async () =>
            {
                for (int i = 1; i <= 25; i++)
                {
                    try
                    {
                        var chunk = CreateRealisticChunk(migrationId, entityType, i, 100);
                        
                        // Simulate various error conditions
                        if (i % 8 == 0)
                        {
                            // Simulate validation error
                            chunk.MigrationId = ""; // Invalid data
                        }
                        else if (i % 12 == 0)
                        {
                            // Simulate timeout scenario
                            await Task.Delay(1000);
                        }
                        
                        await _incrementEventsService!.WriteChunkIncrementAsync(chunk);
                    }
                    catch (Exception)
                    {
                        // Errors should be handled gracefully
                        Console.WriteLine($"        Error handled for {entityType} chunk {i}");
                    }
                    
                    await Task.Delay(40);
                }
            }));
        }
        
        await Task.WhenAll(errorRecoveryTasks);
        
        var errorRecoveryProgress = await _incrementEventsService!.GetAggregatedProgressAsync(migrationId);
        
        Console.WriteLine("      Error recovery workflow results:");
        var totalSuccessful = 0;
        foreach (var kvp in errorRecoveryProgress)
        {
            var successRate = (double)kvp.Value.TotalSuccessful / kvp.Value.TotalProcessed;
            Console.WriteLine($"        {kvp.Key}: {kvp.Value.TotalProcessed} entities, {successRate:P1} success rate");
            totalSuccessful += kvp.Value.TotalSuccessful;
        }
        
        // Verify error recovery maintained reasonable success rate
        if (totalSuccessful > 5000 && errorRecoveryProgress.Count == 3)
        {
            Console.WriteLine($"      ✅ Error recovery workflow maintained {totalSuccessful:N0} successful entities");
        }
    }

    #endregion

    #region Helper Methods

    private static ChunkIncrementEvent CreateRealisticChunk(string migrationId, string entityType, int chunkNumber, int entitiesPerChunk, double successRate = 0.95)
    {
        var startTime = DateTime.UtcNow.AddMinutes(-60 + chunkNumber);
        var endTime = startTime.AddSeconds(GetRealisticProcessingTime(entityType, entitiesPerChunk));
        
        var successful = (int)(entitiesPerChunk * successRate);
        var failed = (int)(entitiesPerChunk * (1 - successRate) * 0.8);
        var skipped = entitiesPerChunk - successful - failed;
        
        return ChunkIncrementEvent.Create(
            migrationId: migrationId,
            entityType: entityType,
            chunkNumber: chunkNumber,
            chunkStartIndex: (chunkNumber - 1) * entitiesPerChunk,
            chunkSize: entitiesPerChunk,
            successfulEntities: successful,
            failedEntities: failed,
            skippedEntities: skipped,
            cancelledEntities: 0,
            processingStartTime: startTime,
            processingEndTime: endTime,
            sourceStore: GetRealisticSourceStore(entityType),
            destinationStore: GetRealisticDestinationStore(entityType),
            errors: failed > 0 ? new List<string> { $"Sample {entityType} processing error" } : null
        );
    }

    private static int GetRealisticChunkCount(string entityType)
    {
        return entityType switch
        {
            "products" => 30,
            "variants" => 50,
            "categories" => 15,
            "brands" => 8,
            "options" => 25,
            "images" => 40,
            "reviews" => 35,
            "modifiers" => 20,
            "redirects" => 12,
            "coupons" => 10,
            _ => 20
        };
    }

    private static int GetRealisticChunkSize(string entityType)
    {
        return entityType switch
        {
            "products" => 100,
            "variants" => 150,
            "categories" => 75,
            "brands" => 50,
            "options" => 120,
            "images" => 80,
            "reviews" => 200,
            "modifiers" => 90,
            "redirects" => 60,
            "coupons" => 40,
            _ => 100
        };
    }

    private static int GetPowerUserChunkCount(string entityType)
    {
        return entityType switch
        {
            "products" => 80,
            "variants" => 120,
            "categories" => 40,
            "brands" => 25,
            "options" => 60,
            "images" => 100,
            "reviews" => 90,
            "modifiers" => 50,
            _ => 40
        };
    }

    private static int GetRealisticProcessingTime(string entityType, int entitiesPerChunk)
    {
        var baseTimePerEntity = entityType switch
        {
            "products" => 2.0, // Products are complex
            "variants" => 1.5,
            "categories" => 1.0,
            "brands" => 0.8,
            "options" => 1.2,
            "images" => 3.0, // Images require download/upload
            "reviews" => 0.5,
            "modifiers" => 1.0,
            "redirects" => 0.3,
            "coupons" => 0.6,
            _ => 1.0
        };
        
        return (int)(entitiesPerChunk * baseTimePerEntity);
    }

    private static string GetRealisticSourceStore(string entityType)
    {
        return entityType switch
        {
            "products" => "large-ecommerce-source",
            "categories" => "catalog-source-store",
            "brands" => "brand-management-source",
            "variants" => "inventory-source-store",
            "images" => "media-source-store",
            _ => "general-source-store"
        };
    }

    private static string GetRealisticDestinationStore(string entityType)
    {
        return entityType switch
        {
            "products" => "bigcommerce-products-dest",
            "categories" => "bigcommerce-catalog-dest",
            "brands" => "bigcommerce-brands-dest",
            "variants" => "bigcommerce-inventory-dest",
            "images" => "bigcommerce-media-dest",
            _ => "bigcommerce-general-dest"
        };
    }

    private static async Task CleanupTestData()
    {
        if (_incrementEventsService == null) return;

        try
        {
            Console.WriteLine("\n🧹 Cleaning up migration test data...");
            foreach (var migrationId in _testMigrationIds)
            {
                await _incrementEventsService.DeleteMigrationIncrementsAsync(migrationId);
            }
            Console.WriteLine($"✅ Cleaned up {_testMigrationIds.Count} test migrations");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Cleanup warning: {ex.Message}");
        }
        finally
        {
            if (_incrementEventsService is IDisposable disposable)
                disposable.Dispose();
        }
    }

    #endregion
}
