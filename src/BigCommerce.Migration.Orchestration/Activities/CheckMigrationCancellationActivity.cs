using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Azure;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for checking if a migration has been cancelled.
/// Now enhanced with LiveCancellationManager for multi-scope cancellation support.
/// </summary>
public class CheckMigrationCancellationActivity
{
    private readonly IMigrationStorageService _storageService;
    private readonly ILiveCancellationManager _liveCancellationManager;
    private readonly ILogger<CheckMigrationCancellationActivity> _logger;

    public CheckMigrationCancellationActivity(
        IMigrationStorageService storageService,
        ILiveCancellationManager liveCancellationManager,
        ILogger<CheckMigrationCancellationActivity> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _liveCancellationManager = liveCancellationManager ?? throw new ArgumentNullException(nameof(liveCancellationManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks if a migration has been cancelled
    /// </summary>
    /// <param name="migrationId">Migration ID to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("CheckMigrationCancellation")]
    public async Task<bool> CheckMigrationCancellationAsync([ActivityTrigger] string migrationId, CancellationToken cancellationToken = default)
    {
        // CRITICAL FIX: Strip quotes if they exist in the migration ID
        var cleanMigrationId = migrationId?.Trim('"') ?? migrationId;
        if (cleanMigrationId != migrationId)
        {
            _logger.LogWarning("🔧 QUOTE FIX: Stripped quotes from migration ID. Original: {Original}, Clean: {Clean}", 
                migrationId, cleanMigrationId);
        }
        
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _logger.LogInformation("🔍 CANCELLATION CHECK: Starting check for migration {MigrationId}", cleanMigrationId);
            _logger.LogInformation("🔍 DEBUG: Using storage service: {ServiceType}", _storageService.GetType().FullName);

            // Check if a cancellation token exists for this migration
            CancellationTokenEntry? cancellationTokenEntry = null;
            try 
            {
                _logger.LogInformation("🔍 DEBUG: About to call GetCancellationTokenAsync for migration {MigrationId}", cleanMigrationId);
                _logger.LogInformation("🔍 DEBUG: Storage service type: {StorageServiceType}, Assembly: {Assembly}", 
                    _storageService.GetType().FullName, _storageService.GetType().Assembly.FullName);
                
                cancellationTokenEntry = await _storageService.GetCancellationTokenAsync(cleanMigrationId);
                _logger.LogInformation("🔍 DEBUG: GetCancellationTokenAsync returned for migration {MigrationId}", cleanMigrationId);
                
                _logger.LogInformation("🔍 CANCELLATION CHECK: Token found={TokenExists}, IsProcessed={IsProcessed} for migration {MigrationId}", 
                    cancellationTokenEntry != null, cancellationTokenEntry?.IsProcessed, cleanMigrationId);
                
                if (cancellationTokenEntry != null)
                {
                    _logger.LogInformation("🔍 TOKEN DETAILS: MigrationId={MigrationId}, Reason={Reason}, RequestedAt={RequestedAt}, Status={Status}", 
                        cancellationTokenEntry.MigrationId, cancellationTokenEntry.Reason, cancellationTokenEntry.RequestedAt, cancellationTokenEntry.Status);
                }
                else 
                {
                    _logger.LogWarning("🔍 TOKEN NOT FOUND: No cancellation token entry found for migration {MigrationId}", cleanMigrationId);
                }
            }
            catch (Azure.RequestFailedException azureEx)
            {
                _logger.LogError(azureEx, "🔍 AZURE ERROR: Azure RequestFailedException while calling GetCancellationTokenAsync for migration {MigrationId}. Status: {Status}, ErrorCode: {ErrorCode}", 
                    cleanMigrationId, azureEx.Status, azureEx.ErrorCode);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔍 ERROR: Exception while calling GetCancellationTokenAsync for migration {MigrationId}. Type: {ExceptionType}", 
                    cleanMigrationId, ex.GetType().FullName);
                throw;
            }
            
            var isCancelled = cancellationTokenEntry != null && !cancellationTokenEntry.IsProcessed;
            
            if (isCancelled)
            {
                _logger.LogWarning("🚨 MIGRATION CANCELLED: Migration {MigrationId} has been cancelled! Reason: {Reason}", 
                    cleanMigrationId, cancellationTokenEntry?.Reason);
                return true;
            }
            
            _logger.LogInformation("✅ CANCELLATION CHECK: Migration {MigrationId} not cancelled, continuing...", cleanMigrationId);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Cancellation check was cancelled for migration {MigrationId}", cleanMigrationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check cancellation status for migration {MigrationId}", cleanMigrationId);
            return false; // On error, assume not cancelled to allow migration to continue
        }
    }

    /// <summary>
    /// Enhanced cancellation check using LiveCancellationManager with multi-scope support.
    /// Provides faster response times and comprehensive cancellation status.
    /// </summary>
    /// <param name="request">Cancellation check request with scope and context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Comprehensive cancellation check result</returns>
    [Function("CheckMigrationCancellationEnhanced")]
    public async Task<CancellationCheckResult> CheckMigrationCancellationEnhancedAsync(
        [ActivityTrigger] CancellationCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.MigrationId))
            throw new ArgumentException("Migration ID is required", nameof(request));

        // Clean the migration ID (legacy compatibility)
        var cleanMigrationId = request.MigrationId.Trim('"');
        request.MigrationId = cleanMigrationId;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("⚡ ENHANCED CHECK: Starting enhanced cancellation check for migration {MigrationId}, scope {Scope}",
                request.MigrationId, request.Scope);

            // Use LiveCancellationManager for fast multi-scope checking
            var isCancelled = await _liveCancellationManager.IsCancelledAsync(request.MigrationId, request.Scope);

            // Get detailed status if cancelled
            CancellationStatus? detailedStatus = null;
            if (isCancelled)
            {
                detailedStatus = await _liveCancellationManager.GetCancellationStatusAsync(request.MigrationId);
                
                _logger.LogWarning("🚨 ENHANCED CANCELLATION: Migration {MigrationId} cancelled at scope {Scope}. Active scopes: {ActiveScopes}",
                    request.MigrationId, request.Scope, string.Join(", ", detailedStatus?.ActiveScopes ?? new List<CancellationScope>()));
            }
            else
            {
                _logger.LogDebug("✅ ENHANCED NO CANCELLATION: Migration {MigrationId} active for scope {Scope}",
                    request.MigrationId, request.Scope);
            }

            stopwatch.Stop();

            var result = new CancellationCheckResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = isCancelled,
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = Math.Max(1, (int)stopwatch.ElapsedMilliseconds),
                Success = true,
                ErrorMessage = null,
                CancellationDetails = detailedStatus,
                // Legacy compatibility properties
                Reason = detailedStatus?.ActiveCancellations.FirstOrDefault()?.Reason,
                CancelledAt = detailedStatus?.LastCancellationAt,
                RequestedBy = detailedStatus?.ActiveCancellations.FirstOrDefault()?.RequestedBy
            };

            // Performance monitoring
            if (result.ResponseTimeMs > 1000)
            {
                _logger.LogWarning("⚠️ ENHANCED SLOW RESPONSE: Enhanced cancellation check took {ResponseTimeMs}ms (target: <1000ms) for migration {MigrationId}",
                    result.ResponseTimeMs, request.MigrationId);
            }
            else
            {
                _logger.LogDebug("⚡ ENHANCED FAST RESPONSE: Enhanced cancellation check completed in {ResponseTimeMs}ms for migration {MigrationId}",
                    result.ResponseTimeMs, request.MigrationId);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation("🛑 ENHANCED OPERATION CANCELLED: Enhanced cancellation check was cancelled for migration {MigrationId}",
                request.MigrationId);

            return new CancellationCheckResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = true, // Assume cancelled if operation was cancelled
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = Math.Max(1, (int)stopwatch.ElapsedMilliseconds),
                Success = false,
                ErrorMessage = "Enhanced operation was cancelled",
                CancellationDetails = null
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ ENHANCED CHECK FAILED: Error during enhanced cancellation check for migration {MigrationId}, scope {Scope}",
                request.MigrationId, request.Scope);

            return new CancellationCheckResult
            {
                MigrationId = request.MigrationId,
                Scope = request.Scope,
                IsCancelled = false, // On error, assume not cancelled to allow processing to continue
                CheckedAt = DateTime.UtcNow,
                ResponseTimeMs = Math.Max(1, (int)stopwatch.ElapsedMilliseconds),
                Success = false,
                ErrorMessage = ex.Message,
                CancellationDetails = null
            };
        }
    }

    /// <summary>
    /// Backward compatible wrapper that uses enhanced cancellation checking internally.
    /// Returns simple boolean result for legacy compatibility.
    /// </summary>
    /// <param name="migrationId">Migration ID to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if migration is cancelled, false otherwise</returns>
    [Function("CheckMigrationCancellationCompat")]
    public async Task<bool> CheckMigrationCancellationCompatAsync(
        [ActivityTrigger] string migrationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new CancellationCheckRequest
            {
                MigrationId = migrationId,
                Scope = CancellationScope.Migration // Default to migration-level scope
            };

            var result = await CheckMigrationCancellationEnhancedAsync(request, cancellationToken);
            
            _logger.LogInformation("🔄 COMPAT MODE: Enhanced check for migration {MigrationId} returned {IsCancelled} (response time: {ResponseTimeMs}ms)",
                migrationId, result.IsCancelled, result.ResponseTimeMs);

            return result.IsCancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ COMPAT CHECK FAILED: Error in compatibility check for migration {MigrationId}", migrationId);
            return false; // On error, assume not cancelled to allow migration to continue
        }
    }
} 