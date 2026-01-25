using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace BigCommerce.Migration.Tests.Cancellation;

/// <summary>
/// Example unit tests demonstrating how to test the native cancellation feature.
/// These tests show the patterns and approaches for testing blob-based cooperative cancellation.
/// 
/// To create comprehensive tests, follow the patterns shown here and refer to:
/// docs/Cancellation-Testing-Guide.md
/// </summary>
public class CancellationExampleTests
{
    #region Basic Cancellation Store Testing Pattern

    [Fact]
    public void CancellationStore_Constructor_RequiresValidDependencies()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new Infrastructure.Services.CancellationStore(null!, Mock.Of<ILogger<Infrastructure.Services.CancellationStore>>()));
    }

    [Fact]
    public async Task ICancellationStore_CheckCancellationFlag_BasicMockingPattern()
    {
        // Arrange
        var mockCancellationStore = new Mock<ICancellationStore>();
        var migrationId = "test-migration-123";

        // Setup mock to return cancellation state
        mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(migrationId))
            .ReturnsAsync(true);

        mockCancellationStore
            .Setup(x => x.GetCancellationReasonAsync(migrationId))
            .ReturnsAsync("Test cancellation reason");

        // Act
        var isCancelled = await mockCancellationStore.Object.CheckCancellationFlagAsync(migrationId);
        var reason = await mockCancellationStore.Object.GetCancellationReasonAsync(migrationId);

        // Assert
        isCancelled.Should().BeTrue();
        reason.Should().Be("Test cancellation reason");
        
        // Verify interactions
        mockCancellationStore.Verify(x => x.CheckCancellationFlagAsync(migrationId), Times.Once);
        mockCancellationStore.Verify(x => x.GetCancellationReasonAsync(migrationId), Times.Once);
    }

    #endregion

    #region Service Layer Testing Pattern

    [Fact]
    public async Task ServiceWithCancellation_WhenCancelled_ThrowsOperationCanceledException()
    {
        // This demonstrates how to test any service that uses ICancellationStore
        // Example pattern for EntityTransformService, EntityCreateService, etc.

        // Arrange
        var mockCancellationStore = new Mock<ICancellationStore>();
        var migrationId = "cancelled-migration";

        mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(migrationId))
            .ReturnsAsync(true);

        mockCancellationStore
            .Setup(x => x.GetCancellationReasonAsync(migrationId))
            .ReturnsAsync("User requested cancellation");

        // Example: Testing a hypothetical service that checks cancellation
        var testService = new TestServiceWithCancellation(mockCancellationStore.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await testService.ProcessAsync(migrationId));

        exception.Message.Should().Contain("User requested cancellation");
        
        // Verify cancellation was checked
        mockCancellationStore.Verify(x => x.CheckCancellationFlagAsync(migrationId), Times.Once);
    }

    [Fact]
    public async Task ServiceWithCancellation_WhenNotCancelled_ProcessesNormally()
    {
        // Arrange
        var mockCancellationStore = new Mock<ICancellationStore>();
        var migrationId = "active-migration";

        mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(migrationId))
            .ReturnsAsync(false);

        var testService = new TestServiceWithCancellation(mockCancellationStore.Object);

        // Act
        var result = await testService.ProcessAsync(migrationId);

        // Assert
        result.Should().Be("Processing completed successfully");
        mockCancellationStore.Verify(x => x.CheckCancellationFlagAsync(migrationId), Times.Once);
    }

    #endregion

    #region Error Handling Testing Pattern

    [Fact]
    public async Task ServiceWithCancellation_WhenCancellationCheckFails_ContinuesProcessing()
    {
        // This tests the graceful error handling when blob storage is unavailable

        // Arrange
        var mockCancellationStore = new Mock<ICancellationStore>();
        var migrationId = "test-migration";

        // Simulate blob storage failure
        mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(migrationId))
            .ThrowsAsync(new InvalidOperationException("Blob storage unavailable"));

        var testService = new TestServiceWithCancellation(mockCancellationStore.Object);

        // Act - Should not throw, processing should continue
        var result = await testService.ProcessAsync(migrationId);

        // Assert
        result.Should().Be("Processing completed successfully");
        mockCancellationStore.Verify(x => x.CheckCancellationFlagAsync(migrationId), Times.Once);
    }

    #endregion

    #region Periodic Checking Testing Pattern

    [Fact]
    public async Task ServiceWithPeriodicCancellation_ChecksAtIntervals()
    {
        // This demonstrates testing periodic cancellation checks (e.g., every 5 entities)

        // Arrange
        var mockCancellationStore = new Mock<ICancellationStore>();
        var migrationId = "periodic-test";
        var callCount = 0;

        mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(migrationId))
            .Returns(() =>
            {
                callCount++;
                // Cancel on the 3rd check to simulate periodic cancellation
                return Task.FromResult(callCount >= 3);
            });

        mockCancellationStore
            .Setup(x => x.GetCancellationReasonAsync(migrationId))
            .ReturnsAsync("Periodic check triggered cancellation");

        var testService = new TestServiceWithPeriodicCancellation(mockCancellationStore.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await testService.ProcessItemsAsync(migrationId, 10)); // Process 10 items

        exception.Message.Should().Contain("Periodic check triggered cancellation");
        
        // Verify multiple checks occurred
        mockCancellationStore.Verify(x => x.CheckCancellationFlagAsync(migrationId), Times.AtLeast(3));
    }

    #endregion

    #region Migration ID Scoping Testing Pattern

    [Fact]
    public async Task CancellationScoping_DifferentMigrations_IndependentlyHandled()
    {
        // This tests that cancellation is properly scoped by migration ID

        // Arrange
        var mockCancellationStore = new Mock<ICancellationStore>();
        var cancelledMigrationId = "cancelled-migration";
        var activeMigrationId = "active-migration";

        // Setup different cancellation states for different migrations
        mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(cancelledMigrationId))
            .ReturnsAsync(true);

        mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(activeMigrationId))
            .ReturnsAsync(false);

        mockCancellationStore
            .Setup(x => x.GetCancellationReasonAsync(cancelledMigrationId))
            .ReturnsAsync("Selective cancellation");

        var testService = new TestServiceWithCancellation(mockCancellationStore.Object);

        // Act & Assert
        // Cancelled migration should throw
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await testService.ProcessAsync(cancelledMigrationId));
        exception.Message.Should().Contain("Selective cancellation");

        // Active migration should continue
        var result = await testService.ProcessAsync(activeMigrationId);
        result.Should().Be("Processing completed successfully");

        // Verify proper scoping
        mockCancellationStore.Verify(x => x.CheckCancellationFlagAsync(cancelledMigrationId), Times.Once);
        mockCancellationStore.Verify(x => x.CheckCancellationFlagAsync(activeMigrationId), Times.Once);
    }

    #endregion

    #region Input Validation Testing Pattern

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CancellationOperations_WithInvalidMigrationId_HandleGracefully(string? migrationId)
    {
        // This tests input validation for cancellation operations

        // Arrange
        var mockCancellationStore = new Mock<ICancellationStore>();
        
        // Setup to handle invalid input appropriately
        mockCancellationStore
            .Setup(x => x.CheckCancellationFlagAsync(It.IsAny<string>()))
            .Returns((string id) =>
            {
                if (string.IsNullOrWhiteSpace(id))
                    throw new ArgumentException("Migration ID cannot be null or empty");
                return Task.FromResult(false);
            });

        var testService = new TestServiceWithCancellation(mockCancellationStore.Object);

        // Act & Assert
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            // Should handle invalid input gracefully (either throw ArgumentException or handle gracefully)
            // Depending on your implementation choice
            var result = await testService.ProcessAsync(migrationId!);
            // Or: await Assert.ThrowsAsync<ArgumentException>(() => testService.ProcessAsync(migrationId!));
        }
    }

    #endregion

    #region Test Helper Classes

    /// <summary>
    /// Example service class that demonstrates cancellation integration patterns.
    /// Use this as a template for testing your actual services.
    /// </summary>
    private class TestServiceWithCancellation
    {
        private readonly ICancellationStore _cancellationStore;

        public TestServiceWithCancellation(ICancellationStore cancellationStore)
        {
            _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        }

        public async Task<string> ProcessAsync(string migrationId)
        {
            try
            {
                // Check for cancellation at start
                await CheckCancellationAsync(migrationId);

                // Simulate some processing
                await Task.Delay(10);

                return "Processing completed successfully";
            }
            catch (OperationCanceledException)
            {
                throw; // Re-throw cancellation exceptions
            }
        }

        private async Task CheckCancellationAsync(string migrationId)
        {
            try
            {
                var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                if (isCancelled)
                {
                    var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
                    throw new OperationCanceledException($"Migration cancelled: {reason}");
                }
            }
            catch (OperationCanceledException)
            {
                throw; // Re-throw cancellation exceptions
            }
            catch (Exception)
            {
                // Log warning but continue processing if cancellation check fails
                // This demonstrates graceful error handling
            }
        }
    }

    /// <summary>
    /// Example service that demonstrates periodic cancellation checking.
    /// </summary>
    private class TestServiceWithPeriodicCancellation
    {
        private readonly ICancellationStore _cancellationStore;

        public TestServiceWithPeriodicCancellation(ICancellationStore cancellationStore)
        {
            _cancellationStore = cancellationStore ?? throw new ArgumentNullException(nameof(cancellationStore));
        }

        public async Task ProcessItemsAsync(string migrationId, int itemCount)
        {
            for (int i = 0; i < itemCount; i++)
            {
                // Check for cancellation every 3 items
                if (i > 0 && i % 3 == 0)
                {
                    await CheckCancellationAsync(migrationId);
                }

                // Simulate processing an item
                await Task.Delay(1);
            }
        }

        private async Task CheckCancellationAsync(string migrationId)
        {
            try
            {
                var isCancelled = await _cancellationStore.CheckCancellationFlagAsync(migrationId);
                if (isCancelled)
                {
                    var reason = await _cancellationStore.GetCancellationReasonAsync(migrationId) ?? "Migration cancelled";
                    throw new OperationCanceledException($"Migration cancelled: {reason}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Continue processing if cancellation check fails
            }
        }
    }

    #endregion
}