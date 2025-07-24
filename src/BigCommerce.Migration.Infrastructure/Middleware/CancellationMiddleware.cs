using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Middleware;

/// <summary>
/// Response from a cancellation check
/// </summary>
public class CancellationCheckResponse
{
    /// <summary>
    /// Whether the operation is cancelled
    /// </summary>
    public bool IsCancelled { get; set; }
    
    /// <summary>
    /// Reason for cancellation
    /// </summary>
    public string? CancellationReason { get; set; }
    
    /// <summary>
    /// When the operation was cancelled
    /// </summary>
    public DateTime? CancelledAt { get; set; }
}

/// <summary>
/// Middleware for adding cancellation support to long-running operations
/// Generic implementation that works with any cancellation checking mechanism
/// </summary>
public class CancellationMiddleware
{
    private readonly ILogger<CancellationMiddleware> _logger;
    private readonly Func<string, Task<CancellationCheckResponse>>? _cancellationChecker;

    /// <summary>
    /// Initializes a new instance of the CancellationMiddleware
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="cancellationChecker">Optional function to check for cancellation</param>
    public CancellationMiddleware(
        ILogger<CancellationMiddleware> logger,
        Func<string, Task<CancellationCheckResponse>>? cancellationChecker = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cancellationChecker = cancellationChecker;
    }

    /// <summary>
    /// Wraps a long-running operation with periodic cancellation checks
    /// </summary>
    /// <typeparam name="T">Return type of the operation</typeparam>
    /// <param name="operation">The operation to execute</param>
    /// <param name="migrationId">Migration ID for cancellation checking</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <param name="checkInterval">Interval between cancellation checks</param>
    /// <param name="cancellationToken">Standard cancellation token</param>
    /// <returns>Result of the operation</returns>
    public async Task<T> ExecuteWithCancellationAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string migrationId,
        string operationName,
        TimeSpan? checkInterval = null,
        CancellationToken cancellationToken = default)
    {
        var interval = checkInterval ?? TimeSpan.FromSeconds(30); // Default check every 30 seconds
        var executionId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogInformation("🛡️ [CANCEL-{ExecutionId}] Starting {OperationName} with cancellation middleware for migration {MigrationId}",
            executionId, operationName, migrationId);

        // Check for cancellation before starting
        var initialCheck = await CheckMigrationCancellationAsync(migrationId);
        if (initialCheck.IsCancelled)
        {
            _logger.LogInformation("🛑 [CANCEL-{ExecutionId}] {OperationName} cancelled before starting. Reason: {Reason}",
                executionId, operationName, initialCheck.CancellationReason);
            
            throw new OperationCanceledException($"Operation {operationName} was cancelled: {initialCheck.CancellationReason}");
        }

        // Create a linked cancellation token source for periodic checking
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = linkedCts.Token;

        // Start periodic cancellation checking task
        var cancellationCheckTask = StartPeriodicCancellationCheck(migrationId, operationName, executionId, interval, linkedCts);

        try
        {
            // Execute the operation
            var result = await operation(linkedToken);

            _logger.LogInformation("✅ [CANCEL-{ExecutionId}] {OperationName} completed successfully for migration {MigrationId}",
                executionId, operationName, migrationId);

            return result;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation("🛑 [CANCEL-{ExecutionId}] {OperationName} was cancelled for migration {MigrationId}. Message: {Message}",
                executionId, operationName, migrationId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [CANCEL-{ExecutionId}] {OperationName} failed for migration {MigrationId}",
                executionId, operationName, migrationId);
            throw;
        }
        finally
        {
            // Stop the periodic checking task
            linkedCts.Cancel();
            
            try
            {
                await cancellationCheckTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when we cancel the checking task
            }
        }
    }

    /// <summary>
    /// Wraps a void operation with periodic cancellation checks
    /// </summary>
    /// <param name="operation">The operation to execute</param>
    /// <param name="migrationId">Migration ID for cancellation checking</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <param name="checkInterval">Interval between cancellation checks</param>
    /// <param name="cancellationToken">Standard cancellation token</param>
    public async Task ExecuteWithCancellationAsync(
        Func<CancellationToken, Task> operation,
        string migrationId,
        string operationName,
        TimeSpan? checkInterval = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithCancellationAsync(async (ct) =>
        {
            await operation(ct);
            return 0; // Dummy return value for void operations
        }, migrationId, operationName, checkInterval, cancellationToken);
    }

    /// <summary>
    /// Checks if migration is cancelled using the configured cancellation checker
    /// </summary>
    private async Task<CancellationCheckResponse> CheckMigrationCancellationAsync(string migrationId)
    {
        try
        {
            if (_cancellationChecker != null)
            {
                return await _cancellationChecker(migrationId);
            }
            
            // If no cancellation checker is configured, assume not cancelled
            return new CancellationCheckResponse
            {
                IsCancelled = false,
                CancellationReason = null,
                CancelledAt = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check migration cancellation status for {MigrationId}", migrationId);
            
            // Return safe default - assume not cancelled if check fails
            return new CancellationCheckResponse
            {
                IsCancelled = false,
                CancellationReason = null,
                CancelledAt = null
            };
        }
    }

    /// <summary>
    /// Starts a task that periodically checks for migration cancellation
    /// </summary>
    private async Task StartPeriodicCancellationCheck(
        string migrationId,
        string operationName,
        string executionId,
        TimeSpan interval,
        CancellationTokenSource linkedCts)
    {
        try
        {
            while (!linkedCts.Token.IsCancellationRequested)
            {
                await Task.Delay(interval, linkedCts.Token);

                // Check for external cancellation
                var cancellationCheck = await CheckMigrationCancellationAsync(migrationId);
                if (cancellationCheck.IsCancelled)
                {
                    _logger.LogInformation("🛑 [CANCEL-{ExecutionId}] External cancellation detected for {OperationName} in migration {MigrationId}. Reason: {Reason}",
                        executionId, operationName, migrationId, cancellationCheck.CancellationReason);

                    // Cancel the linked token source to stop the operation
                    linkedCts.Cancel();
                    break;
                }

                _logger.LogDebug("🔍 [CANCEL-{ExecutionId}] Periodic cancellation check passed for {OperationName} in migration {MigrationId}",
                    executionId, operationName, migrationId);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the task is cancelled
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [CANCEL-{ExecutionId}] Error in periodic cancellation check for {OperationName} in migration {MigrationId}",
                executionId, operationName, migrationId);
        }
    }
}

/// <summary>
/// Configuration options for cancellation middleware
/// </summary>
public class CancellationMiddlewareOptions
{
    /// <summary>
    /// Default interval between cancellation checks
    /// </summary>
    public TimeSpan DefaultCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to enable debug logging for periodic checks
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// Maximum duration before forcing operation cancellation (safety net)
    /// </summary>
    public TimeSpan? MaxOperationDuration { get; set; } = TimeSpan.FromHours(2);
}

/// <summary>
/// Extension methods for easier use of cancellation middleware
/// </summary>
public static class CancellationMiddlewareExtensions
{
    /// <summary>
    /// Executes an operation with automatic cancellation checking
    /// </summary>
    public static async Task<T> WithCancellationAsync<T>(
        this CancellationMiddleware middleware,
        Func<Task<T>> operation,
        string migrationId,
        string operationName,
        TimeSpan? checkInterval = null)
    {
        return await middleware.ExecuteWithCancellationAsync(
            _ => operation(),
            migrationId,
            operationName,
            checkInterval);
    }

    /// <summary>
    /// Executes a void operation with automatic cancellation checking
    /// </summary>
    public static async Task WithCancellationAsync(
        this CancellationMiddleware middleware,
        Func<Task> operation,
        string migrationId,
        string operationName,
        TimeSpan? checkInterval = null)
    {
        await middleware.ExecuteWithCancellationAsync(
            _ => operation(),
            migrationId,
            operationName,
            checkInterval);
    }
} 