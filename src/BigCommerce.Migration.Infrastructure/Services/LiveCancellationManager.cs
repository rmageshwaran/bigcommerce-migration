using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Live cancellation manager providing real-time, multi-level cancellation capabilities.
/// Implements continue-on-error policy and maintains performance targets.
/// 
/// Performance Targets:
/// - Cancellation checks: &lt;50ms
/// - Propagation time: &lt;5 seconds
/// - Supports 100+ concurrent requests
/// </summary>
public class LiveCancellationManager : ILiveCancellationManager
{
    private readonly ICancellationTokenRepository _cancellationTokenRepository;
    private readonly ISignalREventFactory _signalREventFactory;
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ILogger<LiveCancellationManager> _logger;

    /// <summary>
    /// Initializes a new instance of the LiveCancellationManager
    /// </summary>
    /// <param name="cancellationTokenRepository">Repository for cancellation token operations</param>
    /// <param name="signalREventFactory">🎯 CENTRALIZED SIGNALR: Factory for consistent event creation</param>
    /// <param name="progressEventPublisher">Service for publishing real-time events</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public LiveCancellationManager(
        ICancellationTokenRepository cancellationTokenRepository,
        ISignalREventFactory signalREventFactory,
        IProgressEventPublisher progressEventPublisher,
        ILogger<LiveCancellationManager> logger)
    {
        _cancellationTokenRepository = cancellationTokenRepository ?? throw new ArgumentNullException(nameof(cancellationTokenRepository));
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory));
        _progressEventPublisher = progressEventPublisher ?? throw new ArgumentNullException(nameof(progressEventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CancellationResult> CancelAsync(
        string migrationId,
        CancellationScope scope,
        string reason,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null or empty", nameof(reason));

        try
        {
            _logger.LogInformation("🛑 LIVE CANCELLATION: Initiating cancellation for migration {MigrationId}, scope {Scope}", 
                migrationId, scope);

            // Create cancellation entry using existing interface for backward compatibility
            // TODO: Task 7.1.5 will implement the full CreateScopedAsync method
            var cancellationEntry = await _cancellationTokenRepository.CreateAsync(migrationId, reason);

            _logger.LogInformation("✅ CANCELLATION CREATED: Entry created for migration {MigrationId}, scope {Scope}", 
                migrationId, scope);

            // Propagate to all instances immediately
            await PropagateToAllInstancesAsync(migrationId, scope, entityType, batchId, storeId);

            // Return success result
            var result = new CancellationResult
            {
                Success = true,
                MigrationId = migrationId,
                Scope = scope,
                RequestedAt = DateTime.UtcNow,
                Message = $"Cancellation request processed successfully for {scope} scope",
                EstimatedCompletionTimeSeconds = EstimateCompletionTime(scope)
            };

            _logger.LogInformation("🎉 CANCELLATION SUCCESS: Migration {MigrationId} cancellation initiated for {Scope}", 
                migrationId, scope);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ CANCELLATION FAILED: Error initiating cancellation for migration {MigrationId}, scope {Scope}", 
                migrationId, scope);

            // Return failure result (continue-on-error policy)
            return new CancellationResult
            {
                Success = false,
                MigrationId = migrationId,
                Scope = scope,
                RequestedAt = DateTime.UtcNow,
                Message = "Cancellation request failed",
                ErrorDetails = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsCancelledAsync(
        string migrationId,
        CancellationScope scope = CancellationScope.Migration,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null)
    {
        try
        {
            // Fast lookup with performance target <50ms
            var startTime = DateTime.UtcNow;

            // Check direct cancellation for this scope using enhanced repository method
            var isDirectlyCancelled = await _cancellationTokenRepository.IsActiveCancellationAsync(
                migrationId, scope, entityType, batchId, storeId);

            if (isDirectlyCancelled)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogDebug("🔍 CANCELLATION CHECK: Direct cancellation found for {MigrationId} {Scope} in {ElapsedMs}ms", 
                    migrationId, scope, elapsed);
                return true;
            }

            // Check hierarchical cancellation (higher-level scopes)
            var isHierarchicallyCancelled = await IsHierarchicallyCancelledAsync(
                migrationId, scope, entityType, batchId, storeId);

            var totalElapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogDebug("🔍 CANCELLATION CHECK: Migration {MigrationId} {Scope} check completed in {ElapsedMs}ms, result: {IsCancelled}", 
                migrationId, scope, totalElapsed, isHierarchicallyCancelled);

            return isHierarchicallyCancelled;
        }
        catch (Exception ex)
        {
            // Continue-on-error policy: Return false on errors to allow processing to continue
            _logger.LogError(ex, "⚠️ CANCELLATION CHECK ERROR: Error checking cancellation status for migration {MigrationId}, scope {Scope}. Returning false to continue processing.", 
                migrationId, scope);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<CancellationStatus> GetCancellationStatusAsync(string migrationId)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
            throw new ArgumentException("Migration ID cannot be null or empty", nameof(migrationId));

        try
        {
            _logger.LogInformation("📊 STATUS REQUEST: Getting cancellation status for migration {MigrationId}", migrationId);

            // Get all active cancellations for this migration using enhanced repository method
            var activeCancellations = await _cancellationTokenRepository.GetActiveByMigrationAsync(migrationId);

            var status = new CancellationStatus
            {
                MigrationId = migrationId,
                HasActiveCancellation = activeCancellations.Any(),
                ActiveScopes = activeCancellations.Select(c => c.Scope).Distinct().ToList(),
                TotalCancellationRequests = activeCancellations.Count,
                LastCancellationAt = activeCancellations.Any() ? activeCancellations.Max(c => c.RequestedAt) : null,
                ActiveCancellations = activeCancellations
            };

            _logger.LogInformation("📊 STATUS RESULT: Migration {MigrationId} has {ActiveCount} active cancellations across {ScopeCount} scopes", 
                migrationId, activeCancellations.Count, status.ActiveScopes.Count);

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ STATUS ERROR: Error getting cancellation status for migration {MigrationId}", migrationId);
            
            // Return safe default status on error
            return new CancellationStatus
            {
                MigrationId = migrationId,
                HasActiveCancellation = false,
                ActiveScopes = new List<CancellationScope>(),
                TotalCancellationRequests = 0,
                ActiveCancellations = new List<EnhancedCancellationTokenEntry>()
            };
        }
    }

    /// <inheritdoc />
    public async Task PropagateToAllInstancesAsync(
        string migrationId,
        CancellationScope scope,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null)
    {
        try
        {
            _logger.LogInformation("📡 PROPAGATION: Starting cancellation propagation for migration {MigrationId}, scope {Scope}", 
                migrationId, scope);

            // 🎯 CENTRALIZED SIGNALR: Use factory to create cancellation progress event
            var cancellationEvent = _signalREventFactory.CreateCancellationProgress(migrationId, new CancellationProgressOptions
            {
                Scope = scope,
                Status = "propagating",
                Reason = "Cancellation requested",
                EntityType = entityType,
                BatchId = batchId,
                StoreId = storeId,
                PropagatedAt = DateTime.UtcNow
            });

            // Publish to all instances via SignalR
            await _progressEventPublisher.PublishAsync(cancellationEvent);

            _logger.LogInformation("✅ PROPAGATION SUCCESS: Cancellation propagated for migration {MigrationId}, scope {Scope}", 
                migrationId, scope);
        }
        catch (Exception ex)
        {
            // Continue-on-error policy: Log error but don't throw
            _logger.LogError(ex, "⚠️ PROPAGATION ERROR: Error propagating cancellation for migration {MigrationId}, scope {Scope}. Continuing with local cancellation.", 
                migrationId, scope);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHierarchicallyCancelledAsync(
        string migrationId,
        CancellationScope currentScope,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null)
    {
        try
        {
            // TODO: Task 7.1.5 will implement full hierarchical checking
            // For now, just check if migration-level cancellation exists
            var existingCancellation = await _cancellationTokenRepository.GetAsync(migrationId);
            if (existingCancellation != null)
                return true;

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ HIERARCHY CHECK ERROR: Error checking hierarchical cancellation for migration {MigrationId}, scope {Scope}", 
                migrationId, currentScope);
            return false; // Continue-on-error policy
        }
    }

    /// <inheritdoc />
    public async Task MarkCancellationProcessedAsync(
        string migrationId,
        CancellationScope scope,
        string? entityType = null,
        string? batchId = null,
        string? storeId = null)
    {
        try
        {
            _logger.LogInformation("✅ MARKING PROCESSED: Marking cancellation as processed for migration {MigrationId}, scope {Scope}", 
                migrationId, scope);

            // TODO: Task 7.1.5 will implement the full UpdateScopedAsync method
            // For now, just log that processing is complete
            await Task.CompletedTask; // To satisfy async signature
            _logger.LogInformation("✅ PROCESSED: Cancellation marked as processed for migration {MigrationId}, scope {Scope}", 
                migrationId, scope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ MARK PROCESSED ERROR: Error marking cancellation as processed for migration {MigrationId}, scope {Scope}", 
                migrationId, scope);
            // Continue-on-error: Don't throw, just log
        }
    }

    /// <summary>
    /// Estimates completion time based on cancellation scope
    /// </summary>
    /// <param name="scope">Cancellation scope</param>
    /// <returns>Estimated completion time in seconds</returns>
    private static int EstimateCompletionTime(CancellationScope scope)
    {
        return scope switch
        {
            CancellationScope.Migration => 30, // 30 seconds for full migration
            CancellationScope.EntityType => 15, // 15 seconds for entity type
            CancellationScope.Batch => 5, // 5 seconds for batch
            CancellationScope.Store => 3, // 3 seconds for store
            _ => 10 // Default 10 seconds
        };
    }
}