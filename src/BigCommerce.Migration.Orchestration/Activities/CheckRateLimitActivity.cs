using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity function for checking rate limits
/// </summary>
public class CheckRateLimitActivity
{
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<CheckRateLimitActivity> _logger;

    public CheckRateLimitActivity(
        IRateLimitService rateLimitService,
        ILogger<CheckRateLimitActivity> logger)
    {
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks rate limits for a store
    /// </summary>
    /// <param name="request">Rate limit check request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [Function("CheckRateLimit")]
    public async Task<RateLimitResult> CheckRateLimitAsync([ActivityTrigger] dynamic request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var storeId = (string)request.StoreId;
            
            _logger.LogDebug("Checking rate limit for store {StoreId}", storeId);

            var result = await _rateLimitService.CheckRateLimitAsync(storeId);
            
            return result ?? new RateLimitResult { CanProceed = true, DelayMs = 0 };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Rate limit check was cancelled");
            return new RateLimitResult { CanProceed = false, DelayMs = 0 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check rate limit");
            // On error, allow proceeding to avoid blocking migration
            return new RateLimitResult { CanProceed = true, DelayMs = 0 };
        }
    }
} 