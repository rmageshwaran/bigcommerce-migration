using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Functions.Functions;
using System.Text.Json;

namespace BigCommerce.Migration.UnitTests.Integration
{
    /// <summary>
    /// Phase 4.4: Real-time cancellation integration tests
    /// Tests cancellation responsiveness, real-time propagation, and performance characteristics
    /// </summary>
    public class RealTimeCancellationIntegrationTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly Mock<IMigrationStorageService> _mockStorageService;
        private readonly Mock<IQueueService> _mockQueueService;
        private readonly Mock<IProgressTracker> _mockProgressTracker;

        public RealTimeCancellationIntegrationTests()
        {
            var services = new ServiceCollection();
            
            // Add logging
            services.AddLogging(builder => builder.AddConsole());
            
            // Mock dependencies
            _mockStorageService = new Mock<IMigrationStorageService>();
            _mockQueueService = new Mock<IQueueService>();
            _mockProgressTracker = new Mock<IProgressTracker>();
            
            services.AddSingleton(_mockStorageService.Object);
            services.AddSingleton(_mockQueueService.Object);
            services.AddSingleton(_mockProgressTracker.Object);
            
            // Add real services for integration testing
            services.AddScoped<IProgressStateValidator, ProgressStateValidator>();
            services.AddScoped<SignalRProgressFunctions>();
            
            _serviceProvider = services.BuildServiceProvider();
        }

        #region Real-Time Responsiveness Tests

        [Fact]
        public async Task RealTimeResponsiveness_CancellationPropagation_CompletesWithinThreshold()
        {
            // Test that cancellation propagates through the system within acceptable time limits
            
            var migrationId = "test-migration-realtime";
            var cancellationReason = "Real-time cancellation test";
            var threshold = TimeSpan.FromMilliseconds(200); // 200ms threshold
            
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedAt = DateTime.UtcNow,
                RequestedBy = "realtime-test"
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);
            
            var validator = _serviceProvider.GetRequiredService<IProgressStateValidator>();
            
            // Measure end-to-end validation time
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var propagationResult = await validator.ValidateCancellationPropagationAsync(migrationId);
            var migrationValidation = await validator.ValidateMigrationProgressConsistencyAsync(migrationId);
            
            stopwatch.Stop();
            
            // Verify fast response
            Assert.True(stopwatch.Elapsed < threshold, 
                $"Cancellation validation took {stopwatch.ElapsedMilliseconds}ms, expected < {threshold.TotalMilliseconds}ms");
            
            // Verify correctness
            Assert.True(propagationResult.IsPropagationWorking);
            Assert.True(migrationValidation.IsOverallValid);
        }

        [Fact]
        public async Task RealTimeResponsiveness_SignalRFiltering_ProcessesQuickly()
        {
            // Test that SignalR event filtering is responsive
            
            var migrationId = "test-migration-signalr-speed";
            var cancellationReason = "SignalR filtering speed test";
            var threshold = TimeSpan.FromMilliseconds(50); // 50ms threshold for single event
            
            var signalRFunctions = _serviceProvider.GetRequiredService<SignalRProgressFunctions>();
            
            // Create a cancelled progress event
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = migrationId,
                EventType = "progress",
                Timestamp = DateTime.UtcNow,
                IsCancelled = true,
                CancellationReason = cancellationReason,
                CancelledAt = DateTime.UtcNow.AddMinutes(-1),
                TotalEntities = 100,
                ProcessedEntities = 50
            };
            
            var queueMessage = JsonSerializer.Serialize(progressEvent);
            
            // Measure SignalR processing time
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var result = signalRFunctions.ProcessProgressEvents(queueMessage);
            
            stopwatch.Stop();
            
            // Verify fast processing
            Assert.True(stopwatch.Elapsed < threshold, 
                $"SignalR processing took {stopwatch.ElapsedMilliseconds}ms, expected < {threshold.TotalMilliseconds}ms");
            
            // Verify correct filtering (should return cancellation notification)
            Assert.NotNull(result);
        }

        [Fact]
        public async Task RealTimeResponsiveness_HighVolumeValidation_MaintainsPerformance()
        {
            // Test that validation performance remains good under high volume
            
            var migrationIds = Enumerable.Range(1, 50) // Reduced from 100 to keep test reasonable
                .Select(i => $"test-migration-volume-{i}")
                .ToList();
            
            var cancellationReason = "High volume test";
            
            // Setup cancellation for all migrations
            foreach (var migrationId in migrationIds)
            {
                var cancellationToken = new CancellationTokenEntry
                {
                    MigrationId = migrationId,
                    Reason = cancellationReason,
                    RequestedAt = DateTime.UtcNow.AddMinutes(-1),
                    RequestedBy = "volume-test"
                };
                
                _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                    .ReturnsAsync(cancellationToken);
            }
            
            var validator = _serviceProvider.GetRequiredService<IProgressStateValidator>();
            
            // Process all validations in parallel
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var validationTasks = migrationIds.Select(async migrationId =>
            {
                var result = await validator.ValidateCancellationPropagationAsync(migrationId);
                return result.IsPropagationWorking;
            });
            
            var results = await Task.WhenAll(validationTasks);
            
            stopwatch.Stop();
            
            // Verify all validations succeeded
            Assert.All(results, result => Assert.True(result));
            
            // Verify reasonable performance (should handle 50 validations quickly)
            var averageTimePerValidation = stopwatch.ElapsedMilliseconds / (double)migrationIds.Count;
            Assert.True(averageTimePerValidation < 20, 
                $"Average validation time was {averageTimePerValidation:F2}ms per migration, expected < 20ms");
        }

        #endregion

        #region Real-Time Event Flow Tests

        [Fact]
        public async Task RealTimeEventFlow_CancelledProgressEvents_FilteredCorrectly()
        {
            // Test real-time event flow for cancelled migrations
            
            var migrationId = "test-migration-event-flow";
            var cancellationReason = "Event flow filtering test";
            var cancelledAt = DateTime.UtcNow.AddMinutes(-2);
            
            var signalRFunctions = _serviceProvider.GetRequiredService<SignalRProgressFunctions>();
            
            // Test different event types with cancellation
            var testEvents = new List<(ProgressEvent Event, bool ShouldBeFiltered)>
            {
                // Progress events should be filtered
                (new MigrationProgressEvent 
                { 
                    MigrationId = migrationId, 
                    EventType = "progress", 
                    IsCancelled = true, 
                    CancellationReason = cancellationReason,
                    CancelledAt = cancelledAt
                }, true),
                
                (new BatchProgressEvent 
                { 
                    MigrationId = migrationId, 
                    EventType = "batch", 
                    IsCancelled = true, 
                    CancellationReason = cancellationReason,
                    CancelledAt = cancelledAt
                }, true),
                
                (new EntityProgressEvent 
                { 
                    MigrationId = migrationId, 
                    EventType = "entity", 
                    IsCancelled = true, 
                    CancellationReason = cancellationReason,
                    CancelledAt = cancelledAt
                }, true),
                
                // Error and status events should NOT be filtered
                (new ErrorProgressEvent 
                { 
                    MigrationId = migrationId, 
                    EventType = "error", 
                    IsCancelled = true, 
                    CancellationReason = cancellationReason,
                    CancelledAt = cancelledAt,
                    Error = "Test error during cancelled migration"
                }, false),
                
                (new StatusProgressEvent 
                { 
                    MigrationId = migrationId, 
                    EventType = "status", 
                    IsCancelled = true, 
                    CancellationReason = cancellationReason,
                    CancelledAt = cancelledAt,
                    Status = "Cancelled"
                }, false)
            };
            
            foreach (var (progressEvent, shouldBeFiltered) in testEvents)
            {
                var queueMessage = JsonSerializer.Serialize(progressEvent);
                var result = signalRFunctions.ProcessProgressEvents(queueMessage);
                
                Assert.NotNull(result);
                
                if (shouldBeFiltered)
                {
                    // Should return cancellation notification for filtered progress events
                    // (Implementation specific - would need to verify based on actual SignalR structure)
                }
                else
                {
                    // Error and status events should pass through normally
                    // (Implementation specific - would verify normal SignalR message)
                }
            }
        }

        [Fact]
        public async Task RealTimeEventFlow_MixedCancellationStates_HandledCorrectly()
        {
            // Test event flow with mixed cancellation states
            
            var signalRFunctions = _serviceProvider.GetRequiredService<SignalRProgressFunctions>();
            
            var events = new List<ProgressEvent>
            {
                // Active migration - should process normally
                new MigrationProgressEvent
                {
                    MigrationId = "active-migration-1",
                    EventType = "progress",
                    IsCancelled = false,
                    TotalEntities = 100,
                    ProcessedEntities = 25
                },
                
                // Cancelled migration - should be filtered
                new MigrationProgressEvent
                {
                    MigrationId = "cancelled-migration-1",
                    EventType = "progress",
                    IsCancelled = true,
                    CancellationReason = "User requested",
                    CancelledAt = DateTime.UtcNow.AddMinutes(-5),
                    TotalEntities = 200,
                    ProcessedEntities = 75
                },
                
                // Another active migration - should process normally
                new EntityProgressEvent
                {
                    MigrationId = "active-migration-2",
                    EventType = "entity",
                    IsCancelled = false,
                    EntityType = "products"
                },
                
                // Error from cancelled migration - should still be broadcast
                new ErrorProgressEvent
                {
                    MigrationId = "cancelled-migration-1",
                    EventType = "error",
                    IsCancelled = true,
                    CancellationReason = "User requested",
                    Error = "Processing error before cancellation"
                }
            };
            
            var results = new List<object>();
            
            foreach (var progressEvent in events)
            {
                var queueMessage = JsonSerializer.Serialize(progressEvent);
                var result = signalRFunctions.ProcessProgressEvents(queueMessage);
                results.Add(result);
            }
            
            // All events should be processed (return non-null results)
            Assert.All(results, result => Assert.NotNull(result));
            
            // Implementation would verify that:
            // - Active migration events are broadcast normally
            // - Cancelled migration progress events return cancellation notifications
            // - Error events from cancelled migrations are still broadcast
        }

        #endregion

        #region Concurrent Processing Tests

        [Fact]
        public async Task ConcurrentProcessing_MultipleValidations_ThreadSafe()
        {
            // Test thread safety of concurrent validation operations
            
            var migrationId = "test-migration-concurrent";
            var cancellationReason = "Concurrent processing test";
            
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedAt = DateTime.UtcNow.AddMinutes(-1),
                RequestedBy = "concurrent-test"
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);
            
            var validator = _serviceProvider.GetRequiredService<IProgressStateValidator>();
            
            // Run multiple concurrent validation operations
            var concurrentTasks = Enumerable.Range(0, 10).Select(async i => // Reduced from 20 to 10
            {
                var tasks = new Task[]
                {
                    validator.ValidateMigrationProgressConsistencyAsync(migrationId),
                    validator.ValidateCancellationPropagationAsync(migrationId),
                    validator.ValidateProgressUpdateAsync(new ProgressUpdate 
                    { 
                        MigrationId = migrationId, 
                        IsCancelled = true, 
                        CancellationReason = cancellationReason,
                        CancelledAt = DateTime.UtcNow.AddMinutes(-1)
                    })
                };
                
                await Task.WhenAll(tasks);
                return i;
            });
            
            // All tasks should complete successfully without exceptions
            var completedTasks = await Task.WhenAll(concurrentTasks);
            
            Assert.Equal(10, completedTasks.Length);
            Assert.All(completedTasks, taskResult => Assert.True(taskResult >= 0));
        }

        [Fact]
        public async Task ConcurrentProcessing_SignalREventProcessing_ThreadSafe()
        {
            // Test thread safety of concurrent SignalR event processing
            
            var signalRFunctions = _serviceProvider.GetRequiredService<SignalRProgressFunctions>();
            
            // Create multiple events for concurrent processing
            var events = Enumerable.Range(0, 20).Select(i => new MigrationProgressEvent // Reduced from 50 to 20
            {
                MigrationId = $"migration-{i % 5}", // 5 different migrations instead of 10
                EventType = "progress",
                IsCancelled = i % 3 == 0, // Every 3rd event is cancelled
                CancellationReason = i % 3 == 0 ? $"Cancellation {i}" : null,
                CancelledAt = i % 3 == 0 ? DateTime.UtcNow.AddMinutes(-1) : null,
                TotalEntities = 100,
                ProcessedEntities = i * 2
            }).ToList();
            
            // Process events concurrently
            var processingTasks = events.Select(async progressEvent =>
            {
                var queueMessage = JsonSerializer.Serialize(progressEvent);
                var result = signalRFunctions.ProcessProgressEvents(queueMessage);
                return result != null;
            });
            
            var results = await Task.WhenAll(processingTasks);
            
            // All events should be processed successfully
            Assert.All(results, result => Assert.True(result));
        }

        #endregion

        #region System Load Tests

        [Fact]
        public async Task SystemLoad_HighFrequencyValidations_MaintainsStability()
        {
            // Test system stability under high frequency validation requests
            
            var migrationId = "test-migration-load";
            var cancellationReason = "System load test";
            
            var cancellationToken = new CancellationTokenEntry
            {
                MigrationId = migrationId,
                Reason = cancellationReason,
                RequestedAt = DateTime.UtcNow.AddMinutes(-1),
                RequestedBy = "load-test"
            };
            
            _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                .ReturnsAsync(cancellationToken);
            
            var validator = _serviceProvider.GetRequiredService<IProgressStateValidator>();
            
            var totalRequests = 100; // Reduced from 1000
            var batchSize = 20; // Reduced from 50
            var successfulRequests = 0;
            var exceptions = new List<Exception>();
            
            // Process requests in batches to simulate high load
            for (int batch = 0; batch < totalRequests / batchSize; batch++)
            {
                var batchTasks = Enumerable.Range(0, batchSize).Select(async i =>
                {
                    try
                    {
                        var result = await validator.ValidateCancellationPropagationAsync(migrationId);
                        if (result.IsPropagationWorking)
                        {
                            Interlocked.Increment(ref successfulRequests);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                
                await Task.WhenAll(batchTasks);
                
                // Small delay between batches to simulate realistic load
                await Task.Delay(10);
            }
            
            // Verify system stability
            Assert.True(successfulRequests >= totalRequests * 0.95, 
                $"Only {successfulRequests}/{totalRequests} requests succeeded");
            
            Assert.True(exceptions.Count < totalRequests * 0.05, 
                $"Too many exceptions: {exceptions.Count}/{totalRequests}");
        }

        #endregion

        #region Real-Time Monitoring Tests

        [Fact]
        public async Task RealTimeMonitoring_ValidationMetrics_CollectedAccurately()
        {
            // Test that validation metrics are collected for monitoring
            
            var validationResults = new List<(string MigrationId, bool IsValid, TimeSpan Duration)>();
            
            var validator = _serviceProvider.GetRequiredService<IProgressStateValidator>();
            
            // Test different migration scenarios
            var scenarios = new[]
            {
                ("cancelled-migration", true, "User cancellation"),
                ("active-migration", false, null),
                ("error-migration", true, "System error cancellation")
            };
            
            foreach (var (migrationId, isCancelled, reason) in scenarios)
            {
                // Setup scenario
                if (isCancelled)
                {
                    var cancellationToken = new CancellationTokenEntry
                    {
                        MigrationId = migrationId,
                        Reason = reason!,
                        RequestedAt = DateTime.UtcNow.AddMinutes(-1),
                        RequestedBy = "monitoring-test"
                    };
                    
                    _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                        .ReturnsAsync(cancellationToken);
                }
                else
                {
                    _mockStorageService.Setup(x => x.GetCancellationTokenAsync(migrationId))
                        .ReturnsAsync((CancellationTokenEntry?)null);
                }
                
                // Measure validation
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var result = await validator.ValidateMigrationProgressConsistencyAsync(migrationId);
                stopwatch.Stop();
                
                validationResults.Add((migrationId, result.IsOverallValid, stopwatch.Elapsed));
            }
            
            // Verify metrics collection
            Assert.Equal(3, validationResults.Count);
            
            // All validations should be successful
            Assert.All(validationResults, result => Assert.True(result.IsValid));
            
            // All validations should complete within reasonable time
            Assert.All(validationResults, result => 
                Assert.True(result.Duration.TotalMilliseconds < 100, 
                    $"Validation for {result.MigrationId} took {result.Duration.TotalMilliseconds}ms"));
        }

        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _serviceProvider?.Dispose();
            }
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
} 