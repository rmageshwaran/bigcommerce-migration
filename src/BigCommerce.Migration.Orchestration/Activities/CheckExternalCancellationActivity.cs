using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for checking external cancellation state exactly once per orchestrator execution.
/// This maintains Durable Functions determinism by ensuring external state is only checked once
/// and the result is stored in the orchestrator context for subsequent use.
/// Now enhanced with LiveCancellationManager for multi-scope cancellation support.
/// </summary>
public class CheckExternalCancellationActivity
{
    private readonly IMigrationStorageService _storageService;
    private readonly ILiveCancellationManager _liveCancellationManager;
    private readonly ILogger<CheckExternalCancellationActivity> _logger;

    public CheckExternalCancellationActivity(
        IMigrationStorageService storageService,
        ILiveCancellationManager liveCancellationManager,
        ILogger<CheckExternalCancellationActivity> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _liveCancellationManager = liveCancellationManager ?? throw new ArgumentNullException(nameof(liveCancellationManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks external cancellation state exactly once per orchestrator execution.
    /// This is the ONLY place where external cancellation state should be checked in orchestrators
    /// to maintain determinism across replays.
    /// </summary>
    /// <param name="request">Request containing migration ID and current state</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cancellation response with current external state</returns>
    [Function("CheckExternalCancellationOnce")]
    public async Task<CheckExternalCancellationResponse> CheckExternalCancellationOnceAsync(
        [ActivityTrigger] CheckExternalCancellationRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (!request.IsValid())
            throw new ArgumentException("Invalid request: Migration ID is required", nameof(request));

        var migrationId = request.MigrationId;
        
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("🔍 DETERMINISTIC CHECK: Checking external cancellation state once for migration {MigrationId}", migrationId);

            // Check if a cancellation token exists for this migration in external storage
            var cancellationTokenEntry = await _storageService.GetCancellationTokenAsync(migrationId);
            
            var response = new CheckExternalCancellationResponse();

            if (cancellationTokenEntry != null)
            {
                response.IsCancelled = !cancellationTokenEntry.IsProcessed;
                response.CancellationReason = cancellationTokenEntry.Reason ?? "Unknown reason";
                response.CancelledAt = cancellationTokenEntry.RequestedAt;
                response.IsProcessed = cancellationTokenEntry.IsProcessed;

                _logger.LogInformation("🔍 EXTERNAL STATE: Migration {MigrationId} - IsCancelled={IsCancelled}, IsProcessed={IsProcessed}, Reason='{Reason}'", 
                    migrationId, response.IsCancelled, response.IsProcessed, response.CancellationReason);
                
                if (response.IsCancelled)
                {
                    _logger.LogWarning("🚨 EXTERNAL CANCELLATION DETECTED: Migration {MigrationId} has an unprocessed cancellation token! Reason: {Reason}", 
                        migrationId, response.CancellationReason);
                }
                else if (response.IsProcessed)
                {
                    _logger.LogInformation("✅ PROCESSED CANCELLATION: Migration {MigrationId} had a cancellation token but it's already processed", migrationId);
                }
            }
            else
            {
                response.IsCancelled = false;
                response.IsProcessed = false;
                _logger.LogInformation("✅ NO CANCELLATION: No cancellation token found for migration {MigrationId}", migrationId);
            }

            // Log the deterministic check for debugging
            _logger.LogInformation("🎯 DETERMINISTIC RESULT: Migration {MigrationId} external check complete - " +
                                  "IsCancelled={IsCancelled}, IsProcessed={IsProcessed}", 
                migrationId, response.IsCancelled, response.IsProcessed);

            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("External cancellation check was cancelled for migration {MigrationId}", migrationId);
            
            // Activity should return proper result, not throw - assume not cancelled if check fails
            return new CheckExternalCancellationResponse
            {
                IsCancelled = false,
                CancellationReason = "Cancellation check was interrupted",
                CancelledAt = null,
                IsProcessed = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ERROR: Failed to check external cancellation state for migration {MigrationId}", migrationId);
            
            // On error, return a safe default (not cancelled) to allow migration to continue
            // This prevents a storage outage from blocking all migrations
            return new CheckExternalCancellationResponse
            {
                IsCancelled = false,
                IsProcessed = false,
                CancellationReason = $"Error checking cancellation: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Enhanced external cancellation check using LiveCancellationManager.
    /// Provides multi-scope cancellation checking with faster response times.
    /// Maintains determinism by storing results in orchestrator context.
    /// </summary>
    /// <param name="request">Enhanced cancellation check request with scope and context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Enhanced cancellation response with comprehensive status</returns>
    [Function("CheckExternalCancellationEnhanced")]
    public async Task<EnhancedCheckExternalCancellationResponse> CheckExternalCancellationEnhancedAsync(
        [ActivityTrigger] EnhancedCheckExternalCancellationRequest request,
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

            _logger.LogInformation("⚡ ENHANCED DETERMINISTIC CHECK: Starting enhanced external cancellation check for migration {MigrationId}, scope {Scope}",
                request.MigrationId, request.Scope);

            // Use LiveCancellationManager for fast multi-scope checking
            var isCancelled = await _liveCancellationManager.IsCancelledAsync(request.MigrationId, request.Scope);

            // Get comprehensive cancellation status
            var cancellationStatus = await _liveCancellationManager.GetCancellationStatusAsync(request.MigrationId);

            stopwatch.Stop();

            var response = new EnhancedCheckExternalCancellationResponse
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = isCancelled,
                CancellationStatus = cancellationStatus,
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = true,
                ErrorMessage = null,
                // Legacy compatibility
                CancellationReason = cancellationStatus?.ActiveCancellations.FirstOrDefault()?.Reason ?? "Unknown reason",
                CancelledAt = cancellationStatus?.LastCancellationAt,
                IsProcessed = !isCancelled // If not cancelled, then any previous cancellation was processed
            };

            if (isCancelled)
            {
                _logger.LogWarning("🚨 ENHANCED EXTERNAL CANCELLATION: Migration {MigrationId} cancelled at scope {Scope}. Active scopes: {ActiveScopes}, Total requests: {TotalRequests}",
                    request.MigrationId, request.Scope, 
                    string.Join(", ", cancellationStatus?.ActiveScopes ?? new List<CancellationScope>()),
                    cancellationStatus?.TotalCancellationRequests ?? 0);
            }
            else
            {
                _logger.LogInformation("✅ ENHANCED NO EXTERNAL CANCELLATION: Migration {MigrationId} active for scope {Scope} (response time: {ResponseTimeMs}ms)",
                    request.MigrationId, request.Scope, response.ResponseTimeMs);
            }

            // Performance monitoring
            if (response.ResponseTimeMs > 1000)
            {
                _logger.LogWarning("⚠️ ENHANCED SLOW EXTERNAL CHECK: External cancellation check took {ResponseTimeMs}ms (target: <1000ms) for migration {MigrationId}",
                    response.ResponseTimeMs, request.MigrationId);
            }

            _logger.LogInformation("🎯 ENHANCED DETERMINISTIC RESULT: Migration {MigrationId} enhanced external check complete - " +
                                  "IsCancelled={IsCancelled}, Scope={Scope}, ResponseTime={ResponseTimeMs}ms",
                request.MigrationId, response.IsCancelled, response.Scope, response.ResponseTimeMs);

            return response;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation("🛑 ENHANCED EXTERNAL CHECK CANCELLED: Enhanced external cancellation check was cancelled for migration {MigrationId}",
                request.MigrationId);

            return new EnhancedCheckExternalCancellationResponse
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = true, // Assume cancelled if operation was cancelled
                CancellationStatus = null,
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = false,
                ErrorMessage = "Enhanced external check operation was cancelled",
                CancellationReason = "Operation cancelled",
                CancelledAt = DateTime.UtcNow,
                IsProcessed = false
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ ENHANCED EXTERNAL CHECK FAILED: Error during enhanced external cancellation check for migration {MigrationId}, scope {Scope}",
                request.MigrationId, request.Scope);

            return new EnhancedCheckExternalCancellationResponse
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = false, // On error, assume not cancelled to allow processing to continue
                CancellationStatus = null,
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = false,
                ErrorMessage = ex.Message,
                CancellationReason = "Check failed",
                CancelledAt = null,
                IsProcessed = false
            };
        }
    }

    /// <summary>
    /// Backward compatible wrapper that uses enhanced external cancellation checking internally.
    /// Returns traditional CheckExternalCancellationResponse for legacy compatibility.
    /// </summary>
    /// <param name="request">Traditional external cancellation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Traditional external cancellation response</returns>
    [Function("CheckExternalCancellationCompat")]
    public async Task<CheckExternalCancellationResponse> CheckExternalCancellationCompatAsync(
        [ActivityTrigger] CheckExternalCancellationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var enhancedRequest = new EnhancedCheckExternalCancellationRequest
            {
                MigrationId = request.MigrationId,
                Scope = CancellationScope.Migration, // Default to migration-level scope for legacy compatibility
                CurrentState = request.CurrentState
            };

            var enhancedResponse = await CheckExternalCancellationEnhancedAsync(enhancedRequest, cancellationToken);

            _logger.LogInformation("🔄 EXTERNAL COMPAT MODE: Enhanced external check for migration {MigrationId} returned {IsCancelled} (response time: {ResponseTimeMs}ms)",
                request.MigrationId, enhancedResponse.IsCancelled, enhancedResponse.ResponseTimeMs);

            // Convert enhanced response to legacy format
            return new CheckExternalCancellationResponse
            {
                IsCancelled = enhancedResponse.IsCancelled,
                CancellationReason = enhancedResponse.CancellationReason,
                CancelledAt = enhancedResponse.CancelledAt,
                IsProcessed = enhancedResponse.IsProcessed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ EXTERNAL COMPAT CHECK FAILED: Error in compatibility external check for migration {MigrationId}", request.MigrationId);
            
            return new CheckExternalCancellationResponse
            {
                IsCancelled = false, // On error, assume not cancelled to allow migration to continue
                CancellationReason = "Check failed",
                CancelledAt = null,
                IsProcessed = false
            };
        }
    }
}

/// <summary>
/// Helper methods for working with deterministic cancellation states
/// </summary>
public static class DeterministicCancellationHelper
{
    /// <summary>
    /// Creates a deterministic cancellation state from external check response
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="externalResponse">External cancellation check response</param>
    /// <returns>Deterministic cancellation state</returns>
    public static DeterministicCancellationState CreateFromExternalCheck(
        string migrationId, 
        CheckExternalCancellationResponse externalResponse)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

        if (externalResponse == null)
            throw new ArgumentNullException(nameof(externalResponse));

        var state = DeterministicCancellationState.Create(migrationId);
        
        if (externalResponse.IsCancelled)
        {
            state.MarkAsCancelled(externalResponse.CancellationReason, externalResponse.CancelledAt);
        }
        
        state.MarkStateAsChecked();

        return state;
    }

    /// <summary>
    /// Updates an existing deterministic state with external check results
    /// </summary>
    /// <param name="existingState">Existing state to update</param>
    /// <param name="externalResponse">External cancellation check response</param>
    /// <returns>Updated state</returns>
    public static DeterministicCancellationState UpdateFromExternalCheck(
        DeterministicCancellationState existingState,
        CheckExternalCancellationResponse externalResponse)
    {
        if (existingState == null)
            throw new ArgumentNullException(nameof(existingState));

        if (externalResponse == null)
            throw new ArgumentNullException(nameof(externalResponse));

        var updatedState = existingState.Clone();

        if (externalResponse.IsCancelled && !updatedState.IsCancelled)
        {
            updatedState.MarkAsCancelled(externalResponse.CancellationReason, externalResponse.CancelledAt);
        }

        if (!updatedState.StateChecked)
        {
            updatedState.MarkStateAsChecked();
        }

        return updatedState;
    }
} 