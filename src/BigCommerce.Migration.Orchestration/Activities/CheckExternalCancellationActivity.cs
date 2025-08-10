using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for checking external cancellation state exactly once per orchestrator execution.
/// This maintains Durable Functions determinism by ensuring external state is only checked once
/// and the result is stored in the orchestrator context for subsequent use.
/// </summary>
public class CheckExternalCancellationActivity
{
    private readonly IMigrationStorageService _storageService;
    private readonly ILogger<CheckExternalCancellationActivity> _logger;

    public CheckExternalCancellationActivity(
        IMigrationStorageService storageService,
        ILogger<CheckExternalCancellationActivity> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
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