using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Infrastructure.Middleware;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Middleware;

/// <summary>
/// Unit tests for CancellationMiddleware Phase 2 functionality
/// Tests the generic cancellation wrapper and periodic checking capabilities
/// </summary>
[Trait("Category", "Phase2Cancellation")]
[Trait("Component", "CancellationMiddleware")]
public class CancellationMiddlewareTests
{
    private readonly Mock<ILogger<CancellationMiddleware>> _mockLogger;
    private readonly CancellationMiddleware _middleware;

    private const string TestMigrationId = "test-migration-123";
    private const string TestOperationName = "TestOperation";

    public CancellationMiddlewareTests()
    {
        _mockLogger = new Mock<ILogger<CancellationMiddleware>>();
        _middleware = new CancellationMiddleware(_mockLogger.Object);
    }

    /// <summary>
    /// Test that operation executes successfully when no cancellation checker is configured
    /// </summary>
    [Fact]
    public async Task ExecuteWithCancellationAsync_WithoutCancellationChecker_ExecutesSuccessfully()
    {
        // Arrange
        var operationExecuted = false;
        var operation = new Func<CancellationToken, Task<string>>(async (ct) =>
        {
            await Task.Delay(10, ct);
            operationExecuted = true;
            return "success";
        });

        // Act
        var result = await _middleware.ExecuteWithCancellationAsync(
            operation,
            TestMigrationId,
            TestOperationName);

        // Assert
        Assert.Equal("success", result);
        Assert.True(operationExecuted);
    }

    /// <summary>
    /// Test that operation is cancelled when cancellation checker returns cancelled
    /// </summary>
    [Fact]
    public async Task ExecuteWithCancellationAsync_WhenInitialCheckReturnsCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var cancellationChecker = new Func<string, Task<CancellationCheckResponse>>(async (migrationId) =>
        {
            await Task.Delay(1);
            return new CancellationCheckResponse
            {
                IsCancelled = true,
                CancellationReason = "User requested cancellation",
                CancelledAt = DateTime.UtcNow
            };
        });

        var middleware = new CancellationMiddleware(_mockLogger.Object, cancellationChecker);

        var operationExecuted = false;
        var operation = new Func<CancellationToken, Task<string>>(async (ct) =>
        {
            await Task.Delay(100, ct);
            operationExecuted = true;
            return "success";
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            middleware.ExecuteWithCancellationAsync(operation, TestMigrationId, TestOperationName));

        Assert.Contains("User requested cancellation", exception.Message);
        Assert.False(operationExecuted);
    }

    /// <summary>
    /// Test that operation is cancelled during periodic checking
    /// </summary>
    [Fact]
    public async Task ExecuteWithCancellationAsync_WhenPeriodicCheckDetectsCancellation_CancelsOperation()
    {
        // Arrange
        var checkCount = 0;
        var cancellationChecker = new Func<string, Task<CancellationCheckResponse>>(async (migrationId) =>
        {
            await Task.Delay(1);
            checkCount++;
            
            // Return cancelled on second check (during periodic checking)
            return new CancellationCheckResponse
            {
                IsCancelled = checkCount > 1,
                CancellationReason = checkCount > 1 ? "Cancelled during execution" : null,
                CancelledAt = checkCount > 1 ? DateTime.UtcNow : null
            };
        });

        var middleware = new CancellationMiddleware(_mockLogger.Object, cancellationChecker);

        var operationStarted = false;
        var operation = new Func<CancellationToken, Task<string>>(async (ct) =>
        {
            operationStarted = true;
            // Long running operation that should be cancelled
            await Task.Delay(2000, ct);
            return "success";
        });

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            middleware.ExecuteWithCancellationAsync(
                operation, 
                TestMigrationId, 
                TestOperationName, 
                TimeSpan.FromMilliseconds(100))); // Check every 100ms

        Assert.True(operationStarted);
        Assert.True(checkCount > 1); // Should have checked multiple times
    }

    /// <summary>
    /// Test that operation completes successfully when no cancellation is detected
    /// </summary>
    [Fact]
    public async Task ExecuteWithCancellationAsync_WhenNotCancelled_CompletesSuccessfully()
    {
        // Arrange
        var checkCount = 0;
        var cancellationChecker = new Func<string, Task<CancellationCheckResponse>>(async (migrationId) =>
        {
            await Task.Delay(1);
            checkCount++;
            return new CancellationCheckResponse
            {
                IsCancelled = false,
                CancellationReason = null,
                CancelledAt = null
            };
        });

        var middleware = new CancellationMiddleware(_mockLogger.Object, cancellationChecker);

        var operation = new Func<CancellationToken, Task<string>>(async (ct) =>
        {
            await Task.Delay(200, ct);
            return "completed successfully";
        });

        // Act
        var result = await middleware.ExecuteWithCancellationAsync(
            operation, 
            TestMigrationId, 
            TestOperationName, 
            TimeSpan.FromMilliseconds(50)); // Check every 50ms

        // Assert
        Assert.Equal("completed successfully", result);
        Assert.True(checkCount >= 2); // Should have checked multiple times during execution
    }

    /// <summary>
    /// Test that cancellation checker exceptions are handled gracefully
    /// </summary>
    [Fact]
    public async Task ExecuteWithCancellationAsync_WhenCancellationCheckerThrows_ContinuesExecution()
    {
        // Arrange
        var cancellationChecker = new Func<string, Task<CancellationCheckResponse>>((migrationId) =>
        {
            throw new InvalidOperationException("Storage unavailable");
        });

        var middleware = new CancellationMiddleware(_mockLogger.Object, cancellationChecker);

        var operation = new Func<CancellationToken, Task<string>>(async (ct) =>
        {
            await Task.Delay(10, ct);
            return "completed despite checker failure";
        });

        // Act
        var result = await middleware.ExecuteWithCancellationAsync(operation, TestMigrationId, TestOperationName);

        // Assert
        Assert.Equal("completed despite checker failure", result);
    }

    /// <summary>
    /// Test the void operation overload
    /// </summary>
    [Fact]
    public async Task ExecuteWithCancellationAsync_VoidOperation_ExecutesSuccessfully()
    {
        // Arrange
        var operationExecuted = false;
        var operation = new Func<CancellationToken, Task>(async (ct) =>
        {
            await Task.Delay(10, ct);
            operationExecuted = true;
        });

        // Act
        await _middleware.ExecuteWithCancellationAsync(operation, TestMigrationId, TestOperationName);

        // Assert
        Assert.True(operationExecuted);
    }

    /// <summary>
    /// Test the extension method for simpler syntax
    /// </summary>
    [Fact]
    public async Task WithCancellationAsync_ExtensionMethod_ExecutesSuccessfully()
    {
        // Arrange
        var operationExecuted = false;
        var operation = new Func<Task<string>>(async () =>
        {
            await Task.Delay(10);
            operationExecuted = true;
            return "extension method success";
        });

        // Act
        var result = await _middleware.WithCancellationAsync(operation, TestMigrationId, TestOperationName);

        // Assert
        Assert.Equal("extension method success", result);
        Assert.True(operationExecuted);
    }

    /// <summary>
    /// Test the void extension method
    /// </summary>
    [Fact]
    public async Task WithCancellationAsync_VoidExtensionMethod_ExecutesSuccessfully()
    {
        // Arrange
        var operationExecuted = false;
        var operation = new Func<Task>(async () =>
        {
            await Task.Delay(10);
            operationExecuted = true;
        });

        // Act
        await _middleware.WithCancellationAsync(operation, TestMigrationId, TestOperationName);

        // Assert
        Assert.True(operationExecuted);
    }

    /// <summary>
    /// Test that operation exceptions are properly propagated
    /// </summary>
    [Fact]
    public async Task ExecuteWithCancellationAsync_WhenOperationThrows_PropagatesException()
    {
        // Arrange
        var operation = new Func<CancellationToken, Task<string>>((ct) =>
        {
            throw new InvalidOperationException("Operation failed");
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _middleware.ExecuteWithCancellationAsync(operation, TestMigrationId, TestOperationName));

        Assert.Equal("Operation failed", exception.Message);
    }

    /// <summary>
    /// Test that external cancellation token is respected
    /// </summary>
    [Fact]
    public async Task ExecuteWithCancellationAsync_WhenExternalTokenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var cancellationTokenSource = new CancellationTokenSource();
        var operationStarted = false;

        var operation = new Func<CancellationToken, Task<string>>(async (ct) =>
        {
            operationStarted = true;
            await Task.Delay(1000, ct); // Long operation
            return "should not complete";
        });

        // Cancel after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            cancellationTokenSource.Cancel();
        });

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            _middleware.ExecuteWithCancellationAsync(
                operation, 
                TestMigrationId, 
                TestOperationName, 
                cancellationToken: cancellationTokenSource.Token));

        Assert.True(operationStarted);
    }

    /// <summary>
    /// Test that multiple operations can be executed concurrently
    /// </summary>
    [Fact]
    public async Task ExecuteWithCancellationAsync_ConcurrentOperations_ExecuteIndependently()
    {
        // Arrange
        var operation1Completed = false;
        var operation2Completed = false;

        var operation1 = new Func<CancellationToken, Task<string>>(async (ct) =>
        {
            await Task.Delay(100, ct);
            operation1Completed = true;
            return "operation1";
        });

        var operation2 = new Func<CancellationToken, Task<string>>(async (ct) =>
        {
            await Task.Delay(100, ct);
            operation2Completed = true;
            return "operation2";
        });

        // Act
        var task1 = _middleware.ExecuteWithCancellationAsync(operation1, TestMigrationId, "Operation1");
        var task2 = _middleware.ExecuteWithCancellationAsync(operation2, TestMigrationId, "Operation2");

        var results = await Task.WhenAll(task1, task2);

        // Assert
        Assert.Equal("operation1", results[0]);
        Assert.Equal("operation2", results[1]);
        Assert.True(operation1Completed);
        Assert.True(operation2Completed);
    }

    /// <summary>
    /// Test that custom check intervals work correctly
    /// </summary>
    [Fact]
    public async Task ExecuteWithCancellationAsync_WithCustomCheckInterval_UsesCorrectInterval()
    {
        // Arrange
        var checkTimes = new List<DateTime>();
        var cancellationChecker = new Func<string, Task<CancellationCheckResponse>>(async (migrationId) =>
        {
            checkTimes.Add(DateTime.UtcNow);
            await Task.Delay(1);
            return new CancellationCheckResponse { IsCancelled = false };
        });

        var middleware = new CancellationMiddleware(_mockLogger.Object, cancellationChecker);

        var operation = new Func<CancellationToken, Task<string>>(async (ct) =>
        {
            await Task.Delay(250, ct); // Run for 250ms
            return "completed";
        });

        // Act
        await middleware.ExecuteWithCancellationAsync(
            operation, 
            TestMigrationId, 
            TestOperationName, 
            TimeSpan.FromMilliseconds(100)); // Check every 100ms

        // Assert
        Assert.True(checkTimes.Count >= 2); // Should have checked at least twice
        
        // Verify intervals are approximately correct (allowing for timing variance)
        for (int i = 1; i < checkTimes.Count; i++)
        {
            var interval = checkTimes[i] - checkTimes[i - 1];
            Assert.True(interval.TotalMilliseconds >= 90 && interval.TotalMilliseconds <= 150);
        }
    }
} 