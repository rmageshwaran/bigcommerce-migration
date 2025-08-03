using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity for processing cancellation requests with real-time SignalR integration.
/// Handles the orchestration of cancellation across multiple scopes and instances.
/// Designed for Azure Durable Functions deterministic compliance.
/// </summary>
public class ProcessCancellationActivity
{
    private readonly ILiveCancellationManager _liveCancellationManager;
    private readonly ILogger<ProcessCancellationActivity> _logger;

    public ProcessCancellationActivity(
        ILiveCancellationManager liveCancellationManager,
        ILogger<ProcessCancellationActivity> logger)
    {
        _liveCancellationManager = liveCancellationManager ?? throw new ArgumentNullException(nameof(liveCancellationManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes a cancellation request, creating the cancellation token and propagating to all instances.
    /// Includes real-time SignalR notifications for immediate UI updates.
    /// </summary>
    /// <param name="request">Cancellation processing request with scope and context</param>
    /// <param name="cancellationToken">Cancellation token for this operation</param>
    /// <returns>Result of the cancellation processing operation</returns>
    [Function("ProcessCancellationActivity")]
    public async Task<CancellationProcessResult> ProcessCancellationAsync(
        [ActivityTrigger] CancellationProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.MigrationId))
            throw new ArgumentException("Migration ID is required", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Cancellation reason is required", nameof(request));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("🚀 PROCESSING CANCELLATION: Starting cancellation processing for migration {MigrationId}, scope {Scope}, reason: {Reason}",
                request.MigrationId, request.Scope, request.Reason);

            // Step 1: Create the cancellation using LiveCancellationManager
            var cancellationResult = await _liveCancellationManager.CancelAsync(
                request.MigrationId,
                request.Scope,
                request.Reason);

            if (!cancellationResult.Success)
            {
                _logger.LogError("❌ CANCELLATION FAILED: Failed to create cancellation for migration {MigrationId}: {ErrorDetails}",
                    request.MigrationId, cancellationResult.ErrorDetails);

                stopwatch.Stop();

                return new CancellationProcessResult
                {
                    MigrationId = request.MigrationId,
                    Scope = request.Scope,
                    Success = false,
                    ErrorMessage = cancellationResult.ErrorDetails,
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    PropagatedInstances = 0,
                    CancellationId = null
                };
            }

            _logger.LogInformation("✅ CANCELLATION CREATED: Successfully created cancellation for migration {MigrationId}",
                request.MigrationId);

            // Step 2: Propagate to all instances for immediate notification
            bool propagationSucceeded = true;
            string? propagationError = null;
            
            try
            {
                await _liveCancellationManager.PropagateToAllInstancesAsync(
                    request.MigrationId,
                    request.Scope,
                    request.EntityType,
                    request.BatchId,
                    request.StoreId);

                _logger.LogInformation("📡 PROPAGATION SENT: Cancellation propagated to all instances for migration {MigrationId}",
                    request.MigrationId);
            }
            catch (Exception propagationEx)
            {
                propagationSucceeded = false;
                propagationError = propagationEx.Message;
                _logger.LogWarning(propagationEx, "⚠️ PROPAGATION FAILED: Failed to propagate cancellation for migration {MigrationId}, but cancellation was created successfully", 
                    request.MigrationId);
            }

            // Step 3: Handle cascade cancellation if requested
            int cascadedCancellations = 0;
            if (request.CascadeToChildren)
            {
                cascadedCancellations = await ProcessCascadeCancellationAsync(request, cancellationToken);
            }

            stopwatch.Stop();

            var result = new CancellationProcessResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                Success = true,
                ErrorMessage = null,
                ProcessedAt = DateTime.UtcNow,
                ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds,
                PropagatedInstances = 1, // TODO: Task 7.3 will track actual instance count
                CancellationId = cancellationResult.CancellationId,
                CascadedCancellations = cascadedCancellations,
                Reason = request.Reason,
                RequestedBy = request.RequestedBy,
                PropagationCompleted = propagationSucceeded,
                PropagationError = propagationError
            };

            _logger.LogInformation("🎯 CANCELLATION PROCESSED: Successfully processed cancellation for migration {MigrationId} in {ProcessingTimeMs}ms",
                request.MigrationId, result.ProcessingTimeMs);

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation("🛑 OPERATION CANCELLED: Cancellation processing was cancelled for migration {MigrationId}",
                request.MigrationId);

            return new CancellationProcessResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                Success = false,
                ErrorMessage = "Operation was cancelled",
                ProcessedAt = DateTime.UtcNow,
                ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds,
                PropagatedInstances = 0,
                CancellationId = null
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ PROCESSING FAILED: Error processing cancellation for migration {MigrationId}, scope {Scope}",
                request.MigrationId, request.Scope);

            return new CancellationProcessResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                Success = false,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.UtcNow,
                ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds,
                PropagatedInstances = 0,
                CancellationId = null
            };
        }
    }

    /// <summary>
    /// Processes batch cancellation for multiple migrations or scopes simultaneously.
    /// Useful for cancelling multiple related operations at once.
    /// </summary>
    /// <param name="request">Batch cancellation request with multiple targets</param>
    /// <param name="cancellationToken">Cancellation token for this operation</param>
    /// <returns>Result of the batch cancellation processing</returns>
    [Function("ProcessBatchCancellationActivity")]
    public async Task<BatchCancellationProcessResult> ProcessBatchCancellationAsync(
        [ActivityTrigger] BatchCancellationProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (request.CancellationRequests == null || request.CancellationRequests.Count == 0)
            throw new ArgumentException("At least one cancellation request is required", nameof(request));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = new List<CancellationProcessResult>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("🔥 BATCH PROCESSING: Starting batch cancellation processing for {RequestCount} requests",
                request.CancellationRequests.Count);

            foreach (var cancellationRequest in request.CancellationRequests)
            {
                try
                {
                    // Process each cancellation individually
                    var result = await ProcessCancellationAsync(cancellationRequest, cancellationToken);
                    results.Add(result);

                    if (result.Success)
                    {
                        _logger.LogDebug("✅ BATCH ITEM SUCCESS: Processed cancellation for migration {MigrationId}",
                            result.MigrationId);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ BATCH ITEM FAILED: Failed to process cancellation for migration {MigrationId}: {ErrorMessage}",
                            result.MigrationId, result.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ BATCH ITEM ERROR: Error processing individual cancellation for migration {MigrationId}",
                        cancellationRequest.MigrationId);

                    results.Add(new CancellationProcessResult
                    {
                        MigrationId = cancellationRequest.MigrationId,
                        Scope = cancellationRequest.Scope,
                        Success = false,
                        ErrorMessage = ex.Message,
                        ProcessedAt = DateTime.UtcNow,
                        ProcessingTimeMs = 0,
                        PropagatedInstances = 0,
                        CancellationId = null
                    });
                }

                // Check for operation cancellation between items
                cancellationToken.ThrowIfCancellationRequested();
            }

            stopwatch.Stop();

            var successCount = results.Count(r => r.Success);
            var failureCount = results.Count - successCount;

            _logger.LogInformation("🎯 BATCH COMPLETED: Processed {SuccessCount}/{TotalCount} cancellations successfully in {ProcessingTimeMs}ms",
                successCount, results.Count, stopwatch.ElapsedMilliseconds);

            return new BatchCancellationProcessResult
            {
                TotalRequests = request.CancellationRequests.Count,
                SuccessfulCancellations = successCount,
                FailedCancellations = failureCount,
                ProcessedAt = DateTime.UtcNow,
                TotalProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = failureCount == 0,
                Results = results,
                ErrorMessage = failureCount > 0 ? $"{failureCount} out of {results.Count} cancellations failed" : null
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation("🛑 BATCH CANCELLED: Batch cancellation processing was cancelled after processing {ProcessedCount}/{TotalCount} items",
                results.Count, request.CancellationRequests.Count);

            return new BatchCancellationProcessResult
            {
                TotalRequests = request.CancellationRequests.Count,
                SuccessfulCancellations = results.Count(r => r.Success),
                FailedCancellations = results.Count - results.Count(r => r.Success),
                ProcessedAt = DateTime.UtcNow,
                TotalProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = false,
                Results = results,
                ErrorMessage = "Batch operation was cancelled"
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ BATCH FAILED: Error during batch cancellation processing");

            return new BatchCancellationProcessResult
            {
                TotalRequests = request.CancellationRequests.Count,
                SuccessfulCancellations = results.Count(r => r.Success),
                FailedCancellations = results.Count - results.Count(r => r.Success) + 1, // +1 for the batch failure
                ProcessedAt = DateTime.UtcNow,
                TotalProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Success = false,
                Results = results,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Processes cascade cancellation to child scopes when requested.
    /// For example, cancelling an EntityType will cascade to all Batches and Stores within that type.
    /// </summary>
    /// <param name="request">Original cancellation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of cascaded cancellations created</returns>
    private async Task<int> ProcessCascadeCancellationAsync(
        CancellationProcessRequest request,
        CancellationToken cancellationToken)
    {
        var cascadedCount = 0;

        try
        {
            // TODO: Task 7.3 will implement the full cascade logic with actual child scope discovery
            // For now, we'll log the intention and return 0

            _logger.LogInformation("🔄 CASCADE PLACEHOLDER: Cascade cancellation requested for migration {MigrationId}, scope {Scope}. Task 7.3 will implement full cascade logic.",
                request.MigrationId, request.Scope);

            // Placeholder for cascade logic that will be implemented in Task 7.3
            switch (request.Scope)
            {
                case CancellationScope.Migration:
                    // Would cascade to all EntityTypes, Batches, and Stores
                    _logger.LogDebug("🔄 CASCADE: Migration-level cancellation would cascade to all child scopes");
                    break;
                case CancellationScope.EntityType:
                    // Would cascade to all Batches and Stores for this EntityType
                    _logger.LogDebug("🔄 CASCADE: EntityType-level cancellation would cascade to Batches and Stores");
                    break;
                case CancellationScope.Batch:
                    // Would cascade to all Stores for this Batch
                    _logger.LogDebug("🔄 CASCADE: Batch-level cancellation would cascade to Stores");
                    break;
                case CancellationScope.Store:
                    // No cascade needed - Store is leaf level
                    _logger.LogDebug("🔄 CASCADE: Store-level cancellation requires no cascade");
                    break;
            }

            return cascadedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ CASCADE FAILED: Error during cascade cancellation processing for migration {MigrationId}",
                request.MigrationId);
            return cascadedCount;
        }
    }
}