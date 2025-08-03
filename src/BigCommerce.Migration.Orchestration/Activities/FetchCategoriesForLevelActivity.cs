using System;
using System.Threading;
using System.Threading.Tasks;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Azure Functions activity for fetching categories at specific hierarchy levels
/// Implements memory-safe level-by-level category discovery with timeout handling
/// Designed for Azure Functions constraints with continue-on-error policy
/// </summary>
public class FetchCategoriesForLevelActivity
{
    private readonly IChunkedHierarchicalDiscoveryStrategy _discoveryStrategy;
    private readonly ILiveCancellationManager _liveCancellationManager;
    private readonly ILogger<FetchCategoriesForLevelActivity> _logger;

    /// <summary>
    /// Initializes a new instance of FetchCategoriesForLevelActivity
    /// </summary>
    /// <param name="discoveryStrategy">Chunked hierarchical discovery strategy for level-based fetching</param>
    /// <param name="liveCancellationManager">Live cancellation manager for real-time cancellation checks</param>
    /// <param name="logger">Logger for comprehensive activity monitoring</param>
    public FetchCategoriesForLevelActivity(
        IChunkedHierarchicalDiscoveryStrategy discoveryStrategy,
        ILiveCancellationManager liveCancellationManager,
        ILogger<FetchCategoriesForLevelActivity> logger)
    {
        _discoveryStrategy = discoveryStrategy ?? throw new ArgumentNullException(nameof(discoveryStrategy));
        _liveCancellationManager = liveCancellationManager ?? throw new ArgumentNullException(nameof(liveCancellationManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the level fetching activity for Azure Functions orchestration
    /// </summary>
    /// <param name="input">Level fetch activity input with requests and timeout configuration</param>
    /// <param name="context">Function execution context for Azure Functions</param>
    /// <returns>Level fetch activity result with processing metrics and error details</returns>
    [Function("FetchCategoriesForLevelActivity")]
    public async Task<LevelFetchActivityResult> RunAsync(
        [ActivityTrigger] LevelFetchActivityInput input,
        FunctionContext context)
    {
        var result = new LevelFetchActivityResult
        {
            Level = input?.LevelRequest?.Level ?? -1,
            Success = false
        };

        try
        {
            // Input validation (Azure Functions constraint compliance)
            var validationResult = ValidateInput(input);
            if (!validationResult.IsValid)
            {
                return CreateValidationErrorResult(validationResult, result);
            }

            var levelRequest = input!.LevelRequest!;
            var discoveryRequest = input.DiscoveryRequest!;

            // 🛑 LIVE CANCELLATION: Check for cancellation before starting level fetching
            var isCancelled = await _liveCancellationManager.IsCancelledAsync(
                discoveryRequest.MigrationId,
                CancellationScope.Batch,
                "categories",
                $"level-{levelRequest.Level}",
                null);

            if (isCancelled)
            {
                _logger.LogInformation("🚫 Level {Level} fetching was cancelled for Migration {MigrationId}", 
                    levelRequest.Level, discoveryRequest.MigrationId);
                
                result.Success = false;
                result.Errors.Add(new ProcessingError
                {
                    ErrorType = ProcessingErrorType.SystemError,
                    ErrorMessage = $"Level {levelRequest.Level} fetching was cancelled",
                    Timestamp = DateTime.UtcNow
                });
                
                return result;
            }

            _logger.LogInformation("🔍 Starting level fetching for Level {Level}, Migration {MigrationId}", 
                levelRequest.Level, discoveryRequest.MigrationId);

            // Set up cancellation token for Azure Functions timeout compliance (max 4 minutes)
            using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(input.TimeoutMinutes));
            var cancellationToken = timeoutCancellation.Token;

            // Track processing time and memory usage
            var startTime = DateTime.UtcNow;
            var initialMemory = await GetMemoryUsageAsync();

            try
            {
                // Execute level-based discovery using chunked strategy
                var levelResult = await _discoveryStrategy.DiscoverLevelAsync(
                    levelRequest, 
                    discoveryRequest, 
                    cancellationToken);

                // Map discovery result to activity result
                result = MapDiscoveryResultToActivityResult(levelResult, result);
                result.Success = true;

                var endTime = DateTime.UtcNow;
                result.ProcessingTimeMinutes = (endTime - startTime).TotalMinutes;
                result.MemoryUsageMB = Math.Max(await GetMemoryUsageAsync(), initialMemory);

                _logger.LogInformation("✅ Level fetching completed for Level {Level} - {SuccessCount}/{TotalCategories} categories in {ProcessingTimeMinutes:F2} minutes", 
                    levelResult.Level, levelResult.SuccessCount, levelResult.TotalCategories, result.ProcessingTimeMinutes);

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Handle timeout gracefully (continue-on-error policy)
                _logger.LogWarning("⏰ Level {Level} fetching timed out after {TimeoutMinutes:F1} minutes", 
                    levelRequest.Level, input.TimeoutMinutes);

                result.Success = false;
                result.ProcessingTimeMinutes = input.TimeoutMinutes;
                result.Errors.Add(new ProcessingError
                {
                    ErrorType = ProcessingErrorType.TimeoutError,
                    ErrorMessage = $"Level fetching timed out after {input.TimeoutMinutes:F1} minutes",
                    Timestamp = DateTime.UtcNow
                });

                return result;
            }
        }
        catch (Exception ex)
        {
            // Comprehensive error handling with continue-on-error policy
            _logger.LogError(ex, "❌ Critical error during level {Level} fetching", result.Level);

            result.Success = false;
            result.Errors.Add(new ProcessingError
            {
                ErrorType = DetermineErrorType(ex),
                ErrorMessage = ex.Message,
                Exception = ex,
                Timestamp = DateTime.UtcNow
            });

            return result;
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Validates the input parameters for Azure Functions constraints
    /// </summary>
    private Core.Services.ValidationResult ValidateInput(LevelFetchActivityInput? input)
    {
        if (input == null)
        {
            var nullResult = new Core.Services.ValidationResult();
            nullResult.Errors.Add("Input cannot be null");
            return nullResult;
        }

        return input.Validate();
    }

    /// <summary>
    /// Creates a validation error result for invalid input
    /// </summary>
    private LevelFetchActivityResult CreateValidationErrorResult(Core.Services.ValidationResult validationResult, LevelFetchActivityResult result)
    {
        _logger.LogError("❌ Input validation failed: {Errors}", string.Join(", ", validationResult.Errors));

        result.Success = false;
        foreach (var error in validationResult.Errors)
        {
            result.Errors.Add(new ProcessingError
            {
                ErrorType = ProcessingErrorType.ValidationError,
                ErrorMessage = error,
                Timestamp = DateTime.UtcNow
            });
        }

        return result;
    }

    /// <summary>
    /// Maps LevelProcessingResult from discovery strategy to LevelFetchActivityResult
    /// </summary>
    private LevelFetchActivityResult MapDiscoveryResultToActivityResult(LevelProcessingResult levelResult, LevelFetchActivityResult activityResult)
    {
        activityResult.Level = levelResult.Level;
        activityResult.SuccessCount = levelResult.SuccessCount;
        activityResult.FailureCount = levelResult.FailureCount;
        activityResult.TotalCategories = levelResult.TotalCategories;
        activityResult.ProcessingTimeMinutes = levelResult.ProcessingTimeMinutes;
        activityResult.MemoryUsageMB = levelResult.PeakMemoryUsageMB;
        
        // ✅ CRITICAL FIX: Copy the discovered category IDs
        activityResult.FetchedCategoryIds = levelResult.CategoryIds ?? new List<string>();

        // Copy errors from discovery result
        foreach (var error in levelResult.Errors)
        {
            activityResult.Errors.Add(error);
        }

        return activityResult;
    }

    /// <summary>
    /// Determines the error type from exception for proper categorization
    /// </summary>
    private ProcessingErrorType DetermineErrorType(Exception exception)
    {
        return exception switch
        {
            ArgumentException => ProcessingErrorType.ValidationError,
            InvalidOperationException => ProcessingErrorType.ValidationError,
            TimeoutException => ProcessingErrorType.TimeoutError,
            OperationCanceledException => ProcessingErrorType.TimeoutError,
            OutOfMemoryException => ProcessingErrorType.SystemError,
            _ => ProcessingErrorType.SystemError
        };
    }

    /// <summary>
    /// Gets current memory usage for Azure Functions monitoring
    /// </summary>
    private async Task<double> GetMemoryUsageAsync()
    {
        await Task.Yield();
        
        // Force garbage collection for accurate reading
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var memoryBytes = GC.GetTotalMemory(false);
        return memoryBytes / (1024.0 * 1024.0); // Convert to MB
    }

    #endregion
}