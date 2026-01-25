using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Activities.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity function for orchestrator collision detection
/// Phase 3.1: Wraps OrchestratorCollisionDetectionService to maintain Durable Functions determinism
/// </summary>
public class OrchestratorCollisionDetectionActivity
{
    private readonly OrchestratorCollisionDetectionService _collisionDetectionService;
    private readonly ILogger<OrchestratorCollisionDetectionActivity> _logger;

    public OrchestratorCollisionDetectionActivity(
        OrchestratorCollisionDetectionService collisionDetectionService,
        ILogger<OrchestratorCollisionDetectionActivity> logger)
    {
        _collisionDetectionService = collisionDetectionService ?? throw new ArgumentNullException(nameof(collisionDetectionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Attempts to acquire an orchestrator lock for collision detection
    /// </summary>
    /// <param name="request">Collision detection request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collision detection result</returns>
    [Function("OrchestratorCollisionDetectionActivity")]
    public async Task<OrchestratorCollisionResult> TryAcquireOrchestratorLockAsync(
        [ActivityTrigger] OrchestratorCollisionDetectionRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("🔒 [COLLISION-ACTIVITY] Checking orchestrator collision for migration {MigrationId}, instance {InstanceId}", 
                request.MigrationId, request.InstanceId);

            var result = await _collisionDetectionService.TryAcquireOrchestratorLockAsync(
                request.MigrationId, 
                request.InstanceId, 
                cancellationToken);

            _logger.LogInformation("🔒 [COLLISION-ACTIVITY] Collision check completed for migration {MigrationId}: CanProceed={CanProceed}, LockAcquired={LockAcquired}, CollisionDetected={CollisionDetected}", 
                request.MigrationId, result.CanProceed, result.LockAcquired, result.CollisionDetected);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Orchestrator collision detection was cancelled for migration {MigrationId}", request.MigrationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ERROR: Failed to check orchestrator collision for migration {MigrationId}", request.MigrationId);
            
            // Return error result instead of throwing to maintain orchestrator stability
            return new OrchestratorCollisionResult
            {
                CanProceed = false,
                MigrationId = request.MigrationId,
                InstanceId = request.InstanceId,
                LockAcquired = false,
                HasError = true,
                Message = $"Error during collision detection: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Releases an orchestrator lock
    /// </summary>
    /// <param name="request">Lock release request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if lock was released successfully</returns>
    [Function("ReleaseOrchestratorLockActivity")]
    public async Task<bool> ReleaseOrchestratorLockAsync(
        [ActivityTrigger] OrchestratorLockReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("🔓 [COLLISION-ACTIVITY] Releasing orchestrator lock for migration {MigrationId}, instance {InstanceId}", 
                request.MigrationId, request.InstanceId);

            var result = await _collisionDetectionService.ReleaseOrchestratorLockAsync(
                request.MigrationId, 
                request.InstanceId, 
                cancellationToken);

            _logger.LogInformation("🔓 [COLLISION-ACTIVITY] Lock release completed for migration {MigrationId}: Success={Success}", 
                request.MigrationId, result);

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Orchestrator lock release was cancelled for migration {MigrationId}", request.MigrationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ERROR: Failed to release orchestrator lock for migration {MigrationId}", request.MigrationId);
            return false;
        }
    }

    // Note: CreateCollisionCancellationResultActivity removed in Phase 2.3.1
    // Collision results are now created directly in the orchestrator for better type safety
}

/// <summary>
/// Request model for orchestrator collision detection
/// </summary>
public class OrchestratorCollisionDetectionRequest
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Orchestrator instance identifier
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
}

/// <summary>
/// Request model for orchestrator lock release
/// </summary>
public class OrchestratorLockReleaseRequest
{
    /// <summary>
    /// Migration identifier
    /// </summary>
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// Orchestrator instance identifier
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
}

// Note: CollisionCancellationRequest class removed in Phase 2.3.1
// No longer needed since collision results are created directly in orchestrator 