using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using BigCommerce.Migration.Orchestration.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for starting chunked category migration progress tracking
/// Task 4.1.2: SignalR integration for real-time chunked migration updates
/// Uses centralized SignalREventFactory for consistent event creation
/// </summary>
public class StartChunkedMigrationProgressActivity
{
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ISignalREventFactory _signalREventFactory; // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation
    private readonly ILogger<StartChunkedMigrationProgressActivity> _logger;

    public StartChunkedMigrationProgressActivity(
        IProgressEventPublisher progressEventPublisher,
        ISignalREventFactory signalREventFactory,
        ILogger<StartChunkedMigrationProgressActivity> logger)
    {
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory)); // 🎯 CENTRALIZED SIGNALR: Store factory reference
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts chunked category migration progress tracking with initial SignalR event
    /// </summary>
    /// <param name="startRequest">Chunked migration start request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("StartChunkedMigrationProgressActivity")]
    public async Task StartChunkedMigrationProgressAsync(
        [ActivityTrigger] StartChunkedMigrationProgressRequest startRequest, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Validate required properties (will be caught by continue-on-error below)
            if (string.IsNullOrWhiteSpace(startRequest.MigrationId))
                throw new ArgumentException("MigrationId is required", nameof(startRequest));
            
            var migrationId = startRequest.MigrationId;
            
            _logger.LogInformation("🚀 [CHUNKED-START] Starting chunked category migration progress tracking for MigrationId: {MigrationId}. " +
                "Categories: {TotalCategories}, Levels: {TotalLevels}, Estimated: {EstimatedTimeMinutes}min", 
                migrationId, startRequest.TotalCategories, startRequest.TotalLevels, startRequest.EstimatedTimeMinutes);

            // 🎯 CENTRALIZED SIGNALR: Create migration progress event using factory
            var progressEvent = _signalREventFactory.CreateMigrationProgress(migrationId, new MigrationProgressOptions
            {
                OverallProgress = 0.0, // Starting at 0%
                Status = "processing",
                TotalEntities = startRequest.TotalCategories,
                ProcessedEntities = 0,
                FailedEntities = 0,
                SuccessfulEntities = 0,
                CurrentEntityType = "categories",
                CurrentActivity = "Chunked Category Migration Starting",
                CurrentBatchNumber = 0,
                StartTime = DateTime.UtcNow,
                GroupName = $"migration-{migrationId}",
                CurrentBatch = new CurrentBatchDetails
                {
                    BatchNumber = 0,
                    BatchSize = 0,
                    ProcessedInBatch = 0,
                    BatchProgressPercentage = 0.0,
                    BatchProcessingSpeed = 0.0,
                    BatchElapsedTime = TimeSpan.Zero,
                    EstimatedBatchTimeRemaining = TimeSpan.Zero
                }
            });

            // Publish the SignalR event
            await _progressEventPublisher.PublishMigrationProgressAsync(progressEvent, cancellationToken);

            // 🎯 CHUNKED-SPECIFIC: Create custom status event for chunked migration specifics
            var chunkedStatusEvent = _signalREventFactory.CreateStatusProgress(migrationId, new StatusProgressOptions
            {
                Status = "chunked-migration-started",
                Message = $"Started chunked category migration: {startRequest.TotalCategories} categories across {startRequest.TotalLevels} hierarchy levels",
                Data = new Dictionary<string, object>
                {
                    ["TotalCategories"] = startRequest.TotalCategories,
                    ["TotalLevels"] = startRequest.TotalLevels,
                    ["EstimatedTimeMinutes"] = startRequest.EstimatedTimeMinutes,
                    ["ProcessingStrategy"] = "Level-by-Level Chunked Processing",
                    ["BulkCreationEnabled"] = true
                },
                GroupName = $"migration-{migrationId}"
            });

            await _progressEventPublisher.PublishStatusAsync(chunkedStatusEvent, cancellationToken);
            
            _logger.LogInformation("✅ [CHUNKED-START] Successfully published chunked migration start events for MigrationId: {MigrationId}", migrationId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("🚫 [CHUNKED-START] Start chunked migration progress was cancelled for MigrationId: {MigrationId}", 
                startRequest.MigrationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CHUNKED-START] Failed to start chunked migration progress for MigrationId: {MigrationId}: {ErrorMessage}", 
                startRequest.MigrationId, ex.Message);
            
            // ✅ CONTINUE-ON-ERROR: Don't throw exception - allow migration to continue without progress tracking
            _logger.LogWarning("🔄 [CHUNKED-START] Continuing migration despite progress tracking failure (continue-on-error policy)");
        }
    }
}

/// <summary>
/// Request model for starting chunked category migration progress
/// </summary>
public class StartChunkedMigrationProgressRequest
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of categories to migrate
    /// </summary>
    public int TotalCategories { get; set; }
    
    /// <summary>
    /// Total number of hierarchy levels to process
    /// </summary>
    public int TotalLevels { get; set; }
    
    /// <summary>
    /// Estimated processing time in minutes
    /// </summary>
    public double EstimatedTimeMinutes { get; set; }
}