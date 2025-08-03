using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Real-time cancellation check activity using LiveCancellationManager.
/// Provides multi-scope cancellation checking with <1 second response time.
/// Designed for Azure Durable Functions deterministic compliance.
/// </summary>
public class CheckLiveCancellationActivity
{
    private readonly ILiveCancellationManager _liveCancellationManager;
    private readonly ILogger<CheckLiveCancellationActivity> _logger;

    public CheckLiveCancellationActivity(
        ILiveCancellationManager liveCancellationManager,
        ILogger<CheckLiveCancellationActivity> logger)
    {
        _liveCancellationManager = liveCancellationManager ?? throw new ArgumentNullException(nameof(liveCancellationManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Performs real-time cancellation check with multi-scope support.
    /// Target response time: &lt;1 second
    /// </summary>
    /// <param name="request">Cancellation check request with scope and context</param>
    /// <param name="cancellationToken">Cancellation token for this operation</param>
    /// <returns>Comprehensive cancellation check result</returns>
    [Function("CheckLiveCancellationActivity")]
    public async Task<CancellationCheckResult> CheckLiveCancellationAsync(
        [ActivityTrigger] CancellationCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.MigrationId))
            throw new ArgumentException("Migration ID is required", nameof(request));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("⚡ LIVE CHECK: Starting real-time cancellation check for migration {MigrationId}, scope {Scope}",
                request.MigrationId, request.Scope);

            // Fast cancellation check using LiveCancellationManager
            var isCancelled = await _liveCancellationManager.IsCancelledAsync(
                request.MigrationId, 
                request.Scope);

            // Get detailed status if cancelled
            CancellationStatus? detailedStatus = null;
            if (isCancelled)
            {
                detailedStatus = await _liveCancellationManager.GetCancellationStatusAsync(request.MigrationId);
                
                _logger.LogWarning("🚨 CANCELLATION DETECTED: Migration {MigrationId} cancelled at scope {Scope}. Active scopes: {ActiveScopes}",
                    request.MigrationId, request.Scope, string.Join(", ", detailedStatus?.ActiveScopes ?? new List<CancellationScope>()));
            }
            else
            {
                _logger.LogDebug("✅ NO CANCELLATION: Migration {MigrationId} active for scope {Scope}",
                    request.MigrationId, request.Scope);
            }

            stopwatch.Stop();

            var result = new CancellationCheckResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = isCancelled,
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = true,
                ErrorMessage = null,
                CancellationDetails = detailedStatus,
                // Legacy compatibility fields
                Reason = detailedStatus?.Reason,
                RequestedBy = detailedStatus?.RequestedBy,
                CancelledAt = detailedStatus?.RequestedAt
            };

            // Performance monitoring: Log if response time > 1000ms
            if (result.ResponseTimeMs > 1000)
            {
                _logger.LogWarning("⚠️ SLOW RESPONSE: Cancellation check took {ResponseTimeMs}ms (target: <1000ms) for migration {MigrationId}",
                    result.ResponseTimeMs, request.MigrationId);
            }
            else
            {
                _logger.LogDebug("⚡ FAST RESPONSE: Cancellation check completed in {ResponseTimeMs}ms for migration {MigrationId}",
                    result.ResponseTimeMs, request.MigrationId);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation("🛑 OPERATION CANCELLED: Cancellation check was cancelled for migration {MigrationId}", 
                request.MigrationId);

            return new CancellationCheckResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = true, // Assume cancelled if operation was cancelled
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = false,
                ErrorMessage = "Operation was cancelled",
                CancellationDetails = null
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ CHECK FAILED: Error during cancellation check for migration {MigrationId}, scope {Scope}",
                request.MigrationId, request.Scope);

            return new CancellationCheckResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = false, // On error, assume not cancelled to allow processing to continue
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = false,
                ErrorMessage = ex.Message,
                CancellationDetails = null
            };
        }
    }

    /// <summary>
    /// Enhanced cancellation check that includes hierarchical scope checking.
    /// Checks from specific scope up to migration level for comprehensive detection.
    /// </summary>
    /// <param name="request">Cancellation check request</param>
    /// <param name="cancellationToken">Cancellation token for this operation</param>
    /// <returns>Result with hierarchical cancellation information</returns>
    [Function("CheckHierarchicalCancellationActivity")]
    public async Task<CancellationCheckResult> CheckHierarchicalCancellationAsync(
        [ActivityTrigger] CancellationCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.MigrationId))
            throw new ArgumentException("Migration ID is required", nameof(request));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("🔍 HIERARCHICAL CHECK: Starting hierarchical cancellation check for migration {MigrationId}, starting at scope {Scope}",
                request.MigrationId, request.Scope);

            // Check scopes hierarchically: Store -> Batch -> EntityType -> Migration
            var scopesToCheck = GetHierarchicalScopes(request.Scope);
            
            foreach (var scope in scopesToCheck)
            {
                var isCancelled = await _liveCancellationManager.IsCancelledAsync(request.MigrationId, scope);
                
                if (isCancelled)
                {
                    var detailedStatus = await _liveCancellationManager.GetCancellationStatusAsync(request.MigrationId);
                    
                    _logger.LogWarning("🚨 HIERARCHICAL CANCELLATION: Migration {MigrationId} cancelled at scope {CancelledScope} (checked from {RequestedScope})",
                        request.MigrationId, scope, request.Scope);

                    stopwatch.Stop();

                    return new CancellationCheckResult
                    {
                        MigrationId = request.MigrationId,
                        Scope = scope, // Return the scope where cancellation was found
                        IsCancelled = true,
                        CheckedAt = DateTime.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                        Success = true,
                        ErrorMessage = null,
                        CancellationDetails = detailedStatus
                    };
                }
            }

            stopwatch.Stop();

            _logger.LogDebug("✅ NO HIERARCHICAL CANCELLATION: Migration {MigrationId} active at all scopes from {Scope} upward",
                request.MigrationId, request.Scope);

            return new CancellationCheckResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = false,
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = true,
                ErrorMessage = null,
                CancellationDetails = null
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ HIERARCHICAL CHECK FAILED: Error during hierarchical cancellation check for migration {MigrationId}",
                request.MigrationId);

            return new CancellationCheckResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = false,
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = false,
                ErrorMessage = ex.Message,
                CancellationDetails = null
            };
        }
    }

    /// <summary>
    /// Gets the hierarchical scopes to check, from most specific to most general.
    /// Example: Store -> Batch -> EntityType -> Migration
    /// </summary>
    /// <param name="startingScope">The scope to start checking from</param>
    /// <returns>Ordered list of scopes to check</returns>
    private static CancellationScope[] GetHierarchicalScopes(CancellationScope startingScope)
    {
        return startingScope switch
        {
            CancellationScope.Store => new[] { CancellationScope.Store, CancellationScope.Batch, CancellationScope.EntityType, CancellationScope.Migration },
            CancellationScope.Batch => new[] { CancellationScope.Batch, CancellationScope.EntityType, CancellationScope.Migration },
            CancellationScope.EntityType => new[] { CancellationScope.EntityType, CancellationScope.Migration },
            CancellationScope.Migration => new[] { CancellationScope.Migration },
            _ => new[] { CancellationScope.Migration }
        };
    }
}