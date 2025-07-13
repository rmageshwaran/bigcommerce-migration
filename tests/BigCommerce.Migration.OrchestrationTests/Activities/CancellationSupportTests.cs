using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Activities;

/// <summary>
/// Comprehensive tests for cancellation support across all activity functions
/// Verifies that cancellation is properly implemented throughout the system
/// </summary>
public class CancellationSupportTests
{
    private readonly Mock<ILogger<DiscoverEntitiesActivity>> _loggerMock;
    private readonly Mock<IBigCommerceApiClient> _apiClientMock;
    private readonly Mock<IMigrationStorageService> _storageMock;
    private readonly Mock<IRateLimitService> _rateLimitMock;

    public CancellationSupportTests()
    {
        _loggerMock = new Mock<ILogger<DiscoverEntitiesActivity>>();
        _apiClientMock = new Mock<IBigCommerceApiClient>();
        _storageMock = new Mock<IMigrationStorageService>();
        _rateLimitMock = new Mock<IRateLimitService>();
    }

    [Fact]
    public async Task DiscoverEntitiesActivity_WithCancellation_HandlesCancellationGracefully()
    {
        // Arrange
        var activity = new DiscoverEntitiesActivity(_apiClientMock.Object, _loggerMock.Object);
        var request = new EntityDiscoveryRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            SourceStore = new StoreConfiguration { StoreId = "test", AccessToken = "token" },
            EntityConfig = new EntityConfiguration { EntityType = "products" }
        };
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act
        var result = await activity.DiscoverEntitiesAsync(request, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("products", result.EntityType);
        Assert.Equal(0, result.TotalCount);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Discovery was cancelled", result.Errors[0]);
    }

    [Fact]
    public async Task CheckMigrationCancellationActivity_WithActiveCancellation_ReturnsTrue()
    {
        // Arrange
        var activity = new CheckMigrationCancellationActivity(_storageMock.Object, new Mock<ILogger<CheckMigrationCancellationActivity>>().Object);
        var migrationId = "test-migration";
        
        _storageMock.Setup(x => x.GetCancellationTokenAsync(migrationId))
            .ReturnsAsync(new CancellationTokenEntry
            {
                MigrationId = migrationId,
                IsProcessed = false, // Active cancellation
                RequestedAt = DateTime.UtcNow
            });

        // Act
        var result = await activity.CheckMigrationCancellationAsync(migrationId);

        // Assert
        Assert.True(result.IsCancelled);
    }

    [Fact]
    public async Task CheckMigrationCancellationActivity_WithNoCancellation_ReturnsFalse()
    {
        // Arrange
        var activity = new CheckMigrationCancellationActivity(_storageMock.Object, new Mock<ILogger<CheckMigrationCancellationActivity>>().Object);
        var migrationId = "test-migration";
        
        _storageMock.Setup(x => x.GetCancellationTokenAsync(migrationId))
            .ReturnsAsync((CancellationTokenEntry?)null); // No cancellation

        // Act
        var result = await activity.CheckMigrationCancellationAsync(migrationId);

        // Assert
        Assert.False(result.IsCancelled);
    }

    [Fact]
    public async Task ValidateMigrationStoresActivity_WithCancellation_ReturnsErrorResult()
    {
        // Arrange
        var activity = new ValidateMigrationStoresActivity(_apiClientMock.Object, new Mock<ILogger<ValidateMigrationStoresActivity>>().Object);
        var request = new ValidateStoresRequest
        {
            SourceStore = new StoreConfiguration { StoreId = "source", AccessToken = "token" },
            DestinationStore = new StoreConfiguration { StoreId = "dest", AccessToken = "token" }
        };
        var cancellationToken = new CancellationToken(true);

        // Act
        var result = await activity.ValidateMigrationStoresAsync(request, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains("cancelled", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckRateLimitActivity_WithCancellation_ReturnsCannotProceed()
    {
        // Arrange
        var activity = new CheckRateLimitActivity(_rateLimitMock.Object, new Mock<ILogger<CheckRateLimitActivity>>().Object);
        var request = new CheckRateLimitRequest { StoreId = "test-store", EntityType = "products" };
        var cancellationToken = new CancellationToken(true);

        // Act
        var result = await activity.CheckRateLimitAsync(request, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.CanProceed);
    }

    [Fact]
    public async Task InitializeMigrationActivity_WithCancellation_HandlesCancellationGracefully()
    {
        // Arrange
        var activity = new InitializeMigrationActivity(_storageMock.Object, new Mock<ILogger<InitializeMigrationActivity>>().Object);
        var request = new InitializeMigrationRequest
        {
            MigrationRequest = new MigrationRequest
            {
                SourceStore = new StoreConfiguration { StoreId = "test", AccessToken = "token" },
                DestinationStore = new StoreConfiguration { StoreId = "test", AccessToken = "token" }
            }
        };
        var cancellationToken = new CancellationToken(true);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            activity.InitializeMigrationAsync(request, cancellationToken));
    }

    [Fact]
    public async Task UpdateEntityProgressActivity_WithCancellation_DoesNotThrow()
    {
        // Arrange
        var progressTrackerMock = new Mock<IProgressTracker>();
        var activity = new UpdateEntityProgressActivity(progressTrackerMock.Object, new Mock<ILogger<UpdateEntityProgressActivity>>().Object);
        var progressUpdate = new UpdateEntityProgressRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            Phase = "Processing",
            TotalEntities = 10,
            ProcessedEntities = 5,
            SuccessfulEntities = 4,
            FailedEntities = 1
        };
        var cancellationToken = new CancellationToken(true);

        // Act - Should not throw as progress updates are non-critical
        await activity.UpdateEntityProgressAsync(progressUpdate, cancellationToken);

        // Assert - No exception should be thrown
        Assert.True(true); // Test passes if no exception is thrown
    }
} 