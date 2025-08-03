using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for completing a migration
/// </summary>
public class CompleteMigrationActivity
{
    private readonly IMigrationStorageService _storageService;
    private readonly IOpenSearchService _openSearchService;
    private readonly ICancellationTokenRepository _cancellationRepository;
    private readonly ILogger<CompleteMigrationActivity> _logger;

    public CompleteMigrationActivity(
        IMigrationStorageService storageService,
        IOpenSearchService openSearchService,
        ICancellationTokenRepository cancellationRepository,
        ILogger<CompleteMigrationActivity> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _openSearchService = openSearchService ?? throw new ArgumentNullException(nameof(openSearchService));
        _cancellationRepository = cancellationRepository ?? throw new ArgumentNullException(nameof(cancellationRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Completes the migration process
    /// </summary>
    /// <param name="request">Migration completion request</param>
    [Function("CompleteMigration")]
    public async Task CompleteMigrationAsync([ActivityTrigger] CompleteMigrationRequest request)
    {
        try
        {
            _logger.LogInformation("Completing migration {MigrationId} with status {Status}", 
                request.MigrationId, request.Result.Status);

            // Update migration status in storage
            var migrationEntry = await _storageService.GetMigrationAsync(request.MigrationId);
            if (migrationEntry != null)
            {
                // ✅ CRITICAL FIX: Check for cancellation tokens and override status if found
                var cancellationToken = await _cancellationRepository.GetAsync(request.MigrationId);
                if (cancellationToken != null)
                {
                    // Override status to Cancelled if cancellation token exists (handles race conditions)
                    Console.WriteLine($"🚨 [STATUS-OVERRIDE] Cancellation token found - overriding status from {request.Result.Status} to Cancelled for migration {request.MigrationId}");
                    migrationEntry.Status = MigrationStatus.Cancelled;
                    migrationEntry.ErrorMessage = $"Migration was cancelled: {cancellationToken.Reason}";
                }
                else
                {
                    migrationEntry.Status = request.Result.Status;
                    migrationEntry.ErrorMessage = request.Result.Errors.Any() ? string.Join("; ", request.Result.Errors) : null;
                }
                
                migrationEntry.UpdatedAt = DateTime.UtcNow;
                
                // ✅ CRITICAL FIX: Update statistics fields for history API display
                            // ✅ LOGGING FIX: Use Console.WriteLine to bypass logging framework stripping
            Console.WriteLine($"🚨 [STATS-DEBUG] CompleteMigrationActivity START - Migration: {request.MigrationId}");
            Console.WriteLine($"🚨 [STATS-DEBUG] Input Statistics - Total: {request.Result.Statistics.TotalEntitiesProcessed}, Failed: {request.Result.Statistics.TotalEntitiesFailed}");
            Console.WriteLine($"🚨 [STATS-DEBUG] Input Status: {request.Result.Status}");
                _logger.LogInformation("🔧 [STATS-FIX] FROM-STATS: TotalEntitiesProcessed={Total}, TotalEntitiesSuccessful={Successful}, TotalEntitiesFailed={Failed}", 
                    request.Result.Statistics.TotalEntitiesProcessed, request.Result.Statistics.TotalEntitiesSuccessful, request.Result.Statistics.TotalEntitiesFailed);
                
                // ✅ CRITICAL FIX: DON'T overwrite TotalEntities - it should remain the original source total (192)
                // migrationEntry.TotalEntities = request.Result.Statistics.TotalEntitiesProcessed; // ❌ This was wrong - would set 192 to 60
                migrationEntry.ProcessedEntities = request.Result.Statistics.TotalEntitiesProcessed;
                migrationEntry.FailedEntities = request.Result.Statistics.TotalEntitiesFailed;
                
                _logger.LogInformation("🔧 [STATS-FIX] AFTER: TotalEntities={Total}, ProcessedEntities={Processed}, FailedEntities={Failed}", 
                    migrationEntry.TotalEntities, migrationEntry.ProcessedEntities, migrationEntry.FailedEntities);
                
                // ✅ LOGGING FIX: Debug before storage update
                Console.WriteLine($"🚨 [STATS-DEBUG] BEFORE Storage Update - Total: {migrationEntry.TotalEntities}, Processed: {migrationEntry.ProcessedEntities}, Failed: {migrationEntry.FailedEntities}, Status: {migrationEntry.Status}");
                
                await _storageService.UpdateMigrationAsync(migrationEntry);
                
                // ✅ CRITICAL FIX: Update EntityProgress table for UI display
                // The UI reads from EntityProgress table for entity breakdown, but this table was not being updated during cancellation
                Console.WriteLine($"🚨 [ENTITY-PROGRESS-DEBUG] Starting EntityProgress update for migration {request.MigrationId}");
                Console.WriteLine($"🚨 [ENTITY-PROGRESS-DEBUG] EntityResults count: {request.Result.EntityResults?.Count ?? 0}");
                
                if (request.Result.EntityResults == null || request.Result.EntityResults.Count == 0)
                {
                    Console.WriteLine($"🚨 [ENTITY-PROGRESS-ERROR] No EntityResults found - cannot update EntityProgress table");
                }
                else
                {
                    foreach (var entityResult in request.Result.EntityResults)
                {
                    var entityProgressEntry = new EntityProgressEntry
                    {
                        MigrationId = request.MigrationId,
                        EntityType = entityResult.Key,
                        TotalCount = migrationEntry.TotalEntities, // ✅ CRITICAL FIX: Use migration total, not EntityResult total (which is 0)
                        ProcessedCount = entityResult.Value.ProcessedEntities,
                        SuccessCount = entityResult.Value.SuccessfulEntities,
                        FailureCount = entityResult.Value.FailedEntities,
                        ProgressPercentage = migrationEntry.TotalEntities > 0 
                            ? (double)entityResult.Value.ProcessedEntities / migrationEntry.TotalEntities * 100 
                            : 0,
                        Status = entityResult.Value.IsSuccess ? "Completed" : "Failed",
                        StartTime = DateTime.UtcNow, // ✅ SIMPLIFIED: Use current UTC time for new EntityProgress entries
                        EndTime = DateTime.UtcNow,
                        ProcessingTime = entityResult.Value.ProcessingTime,
                        CreatedAt = DateTime.UtcNow, // ✅ CRITICAL FIX: Set CreatedAt to avoid DateTime Unspecified error
                        UpdatedAt = DateTime.UtcNow   // ✅ CRITICAL FIX: Set UpdatedAt to avoid DateTime Unspecified error
                    };
                    
                    Console.WriteLine($"🚨 [ENTITY-PROGRESS-DEBUG] About to create EntityProgress for {entityResult.Key}: Total={entityProgressEntry.TotalCount}, Success={entityProgressEntry.SuccessCount}, Failed={entityProgressEntry.FailureCount}");
                    await _storageService.CreateOrUpdateEntityProgressAsync(entityProgressEntry);
                    Console.WriteLine($"🚨 [ENTITY-PROGRESS-SUCCESS] ✅ Successfully updated EntityProgress for {entityResult.Key}: Total={entityProgressEntry.TotalCount}, Success={entityProgressEntry.SuccessCount}, Failed={entityProgressEntry.FailureCount}");
                }
                }
                
                // ✅ LOGGING FIX: Debug after storage update
                Console.WriteLine($"🚨 [STATS-DEBUG] AFTER Storage Update COMPLETED for Migration: {request.MigrationId}");
            }

            // Log completion event to OpenSearch
            await _openSearchService.LogMigrationEventAsync(
                "migration_completed",
                request.MigrationId,
                new
                {
                    Status = request.Result.Status.ToString(),
                    Duration = request.Result.Duration,
                    EntityResults = request.Result.EntityResults,
                    Statistics = request.Result.Statistics,
                    ErrorCount = request.Result.Errors.Count
                });

            _logger.LogInformation("Migration {MigrationId} completed successfully with status {Status}", 
                request.MigrationId, request.Result.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete migration {MigrationId}", request.MigrationId);
            throw;
        }
    }
}

/// <summary>
/// Request model for migration completion
/// </summary>
public class CompleteMigrationRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public MigrationResult Result { get; set; } = new();
} 