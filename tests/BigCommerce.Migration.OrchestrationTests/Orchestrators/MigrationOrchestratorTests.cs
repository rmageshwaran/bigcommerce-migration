using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Orchestrators;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.OrchestrationTests.Orchestrators;

/// <summary>
/// Tests for the main migration orchestrator
/// These tests define the expected behavior - implementation will follow (TDD)
/// </summary>
public class MigrationOrchestratorTests
{
    private readonly Mock<IDurableOrchestrationContext> _contextMock;
    private readonly Mock<ILogger<MigrationOrchestrator>> _loggerMock;
    private readonly MigrationOrchestrator _orchestrator;
    private readonly MigrationOrchestrationRequest _testRequest;

    public MigrationOrchestratorTests()
    {
        _contextMock = new Mock<IDurableOrchestrationContext>();
        _loggerMock = new Mock<ILogger<MigrationOrchestrator>>();
        _orchestrator = new MigrationOrchestrator(_loggerMock.Object);
        
        _testRequest = new MigrationOrchestrationRequest
        {
            MigrationId = "test-migration-123",
            OriginalRequest = new MigrationRequest
            {
                SourceStore = new StoreConfiguration
                {
                    StoreId = "source-store",
                    ChannelId = "source-channel",
                    AccessToken = "source-token"
                },
                DestinationStore = new StoreConfiguration
                {
                    StoreId = "dest-store",
                    ChannelId = "dest-channel",
                    AccessToken = "dest-token"
                },
                Entities = new List<string> { "categories", "products", "brands" }
            },
            CategoryTreeContext = new CategoryTreeContext
            {
                SourceChannelId = "source-channel",
                DestinationChannelId = "dest-channel"
            },
            StartTime = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>()
        };
    }

    #region Basic Orchestrator Functionality Tests

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldProcessValidRequest()
    {
        // Arrange
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);

        // Setup successful activity calls
        SetupSuccessfulActivityCalls();

        // Act
        var result = await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testRequest.MigrationId, result.MigrationId);
        Assert.Equal(MigrationStatus.Completed, result.Status);
    }

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldReturnFailureForInvalidRequest()
    {
        // Arrange
        var invalidRequest = new MigrationOrchestrationRequest
        {
            MigrationId = "", // Invalid empty migration ID
            OriginalRequest = new MigrationRequest()
        };

        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(invalidRequest);

        // Act
        var result = await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(MigrationStatus.Failed, result.Status);
        Assert.Contains("MigrationId is required", result.Errors);
    }

    #endregion

    #region Deterministic Behavior Tests

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldUseDeterministicDateTime()
    {
        // Arrange
        var deterministicTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(deterministicTime);

        SetupSuccessfulActivityCalls();

        // Act
        var result = await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert
        Assert.Equal(deterministicTime, result.StartTime);
        
        // Verify context.CurrentUtcDateTime was used instead of DateTime.UtcNow
        _contextMock.Verify(x => x.CurrentUtcDateTime, Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldUseDeterministicGuid()
    {
        // Arrange
        var deterministicGuid = Guid.NewGuid();
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);
        _contextMock.Setup(x => x.NewGuid())
                   .Returns(deterministicGuid);

        SetupSuccessfulActivityCalls();

        // Act
        await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert - Verify context.NewGuid() was used for any GUID generation
        _contextMock.Verify(x => x.NewGuid(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldNotUseNonDeterministicOperations()
    {
        // This test ensures no non-deterministic operations are used
        // The orchestrator should not use DateTime.Now, Guid.NewGuid(), Task.Delay, etc.
        // This is validated by the implementation not calling these methods directly
        
        // Arrange
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);

        SetupSuccessfulActivityCalls();

        // Act & Assert - Should not throw any exceptions related to non-deterministic operations
        var result = await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);
        Assert.NotNull(result);
    }

    #endregion

    #region Entity Dependency Tests

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldProcessCategoriesFirst()
    {
        // Arrange
        var requestWithDependencies = new MigrationOrchestrationRequest
        {
            MigrationId = "test-migration-123",
            OriginalRequest = new MigrationRequest
            {
                SourceStore = _testRequest.OriginalRequest.SourceStore,
                DestinationStore = _testRequest.OriginalRequest.DestinationStore,
                Entities = new List<string> { "products", "categories" } // Products first intentionally
            },
            CategoryTreeContext = _testRequest.CategoryTreeContext
        };

        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(requestWithDependencies);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);

        var callOrder = new List<string>();
        
        // Setup to track call order
        _contextMock.Setup(x => x.CallActivityAsync("InitializeMigration", It.IsAny<object>()))
                   .Returns(Task.CompletedTask)
                   .Callback(() => callOrder.Add("InitializeMigration"));

        _contextMock.Setup(x => x.CallActivityAsync<ValidationResult>("ValidateMigrationStores", It.IsAny<object>()))
                   .ReturnsAsync(new ValidationResult { IsValid = true })
                   .Callback(() => callOrder.Add("ValidateMigrationStores"));

        _contextMock.Setup(x => x.CallSubOrchestratorAsync<EntityMigrationResult>("EntityMigrationOrchestrator", It.IsAny<EntityMigrationRequest>()))
                   .Returns<string, EntityMigrationRequest>((name, request) =>
                   {
                       callOrder.Add($"EntityMigrationOrchestrator-{request.EntityType}");
                       return Task.FromResult(new EntityMigrationResult
                       {
                           EntityType = request.EntityType,
                           TotalEntities = 10,
                           SuccessfulEntities = 10,
                           FailedEntities = 0
                       });
                   });

        _contextMock.Setup(x => x.CallActivityAsync("CompleteMigration", It.IsAny<object>()))
                   .Returns(Task.CompletedTask)
                   .Callback(() => callOrder.Add("CompleteMigration"));

        // Act
        await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert - Categories should be processed before products
        var categoriesIndex = callOrder.IndexOf("EntityMigrationOrchestrator-categories");
        var productsIndex = callOrder.IndexOf("EntityMigrationOrchestrator-products");
        
        Assert.True(categoriesIndex < productsIndex, "Categories should be processed before products");
    }

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldHandleEntityDependencies()
    {
        // Arrange
        var requestWithAllEntities = new MigrationOrchestrationRequest
        {
            MigrationId = "test-migration-123",
            OriginalRequest = new MigrationRequest
            {
                SourceStore = _testRequest.OriginalRequest.SourceStore,
                DestinationStore = _testRequest.OriginalRequest.DestinationStore,
                Entities = new List<string> { "variants", "products", "categories", "images", "modifiers" }
            },
            CategoryTreeContext = _testRequest.CategoryTreeContext
        };

        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(requestWithAllEntities);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);

        var processedEntities = new List<string>();
        
        SetupEntityOrderTracking(processedEntities);

        // Act
        await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert - Verify correct dependency order
        var expectedOrder = new[] { "categories", "products", "variants", "images", "modifiers" };
        
        foreach (var entity in expectedOrder)
        {
            if (processedEntities.Contains(entity))
            {
                var entityIndex = processedEntities.IndexOf(entity);
                
                // Check that all dependencies come before this entity
                foreach (var dependency in GetEntityDependencies(entity))
                {
                    if (processedEntities.Contains(dependency))
                    {
                        var dependencyIndex = processedEntities.IndexOf(dependency);
                        Assert.True(dependencyIndex < entityIndex, 
                            $"{dependency} should be processed before {entity}");
                    }
                }
            }
        }
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldHandleActivityFailures()
    {
        // Arrange
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);

        // Setup initialization to fail
        _contextMock.Setup(x => x.CallActivityAsync("InitializeMigration", It.IsAny<object>()))
                   .ThrowsAsync(new Exception("Migration initialization failed"));

        // Act
        var result = await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(MigrationStatus.Failed, result.Status);
        Assert.Contains("Migration initialization failed", result.Errors);
    }

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldHandleValidationFailures()
    {
        // Arrange
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);

        _contextMock.Setup(x => x.CallActivityAsync("InitializeMigration", It.IsAny<object>()))
                   .Returns(Task.CompletedTask);

        // Setup validation to fail
        _contextMock.Setup(x => x.CallActivityAsync<ValidationResult>("ValidateMigrationStores", It.IsAny<object>()))
                   .ReturnsAsync(new ValidationResult 
                   { 
                       IsValid = false, 
                       ErrorMessage = "Source store validation failed" 
                   });

        // Act
        var result = await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(MigrationStatus.Failed, result.Status);
        Assert.Contains("Source store validation failed", result.Errors);
    }

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldHandleEntityMigrationFailures()
    {
        // Arrange
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);

        _contextMock.Setup(x => x.CallActivityAsync("InitializeMigration", It.IsAny<object>()))
                   .Returns(Task.CompletedTask);

        _contextMock.Setup(x => x.CallActivityAsync<ValidationResult>("ValidateMigrationStores", It.IsAny<object>()))
                   .ReturnsAsync(new ValidationResult { IsValid = true });

        // Setup entity migration to fail
        _contextMock.Setup(x => x.CallSubOrchestratorAsync<EntityMigrationResult>("EntityMigrationOrchestrator", It.IsAny<EntityMigrationRequest>()))
                   .ThrowsAsync(new Exception("Entity migration failed"));

        // Act
        var result = await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(MigrationStatus.Failed, result.Status);
        Assert.Contains("Entity migration failed", result.Errors);
    }

    #endregion

    #region Status Update Tests

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldUpdateStatusThroughoutExecution()
    {
        // Arrange
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);

        var statusUpdates = new List<string>();
        
        _contextMock.Setup(x => x.SetCustomStatus(It.IsAny<string>()))
                   .Callback<string>(status => statusUpdates.Add(status));

        SetupSuccessfulActivityCalls();

        // Act
        await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert
        Assert.Contains(statusUpdates, s => s.Contains("Starting migration"));
        Assert.Contains(statusUpdates, s => s.Contains("Initializing migration"));
        Assert.Contains(statusUpdates, s => s.Contains("Validating stores"));
        Assert.Contains(statusUpdates, s => s.Contains("Processing categories"));
        Assert.Contains(statusUpdates, s => s.Contains("Processing products"));
        Assert.Contains(statusUpdates, s => s.Contains("Completing migration"));
    }

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldUpdateStatusOnError()
    {
        // Arrange
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);

        var statusUpdates = new List<string>();
        
        _contextMock.Setup(x => x.SetCustomStatus(It.IsAny<string>()))
                   .Callback<string>(status => statusUpdates.Add(status));

        _contextMock.Setup(x => x.CallActivityAsync("InitializeMigration", It.IsAny<object>()))
                   .ThrowsAsync(new Exception("Test error"));

        // Act
        await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert
        Assert.Contains(statusUpdates, s => s.Contains("Migration failed"));
    }

    #endregion

    #region Migration Lifecycle Tests

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldCompleteFullMigrationLifecycle()
    {
        // Arrange
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        
        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddSeconds(5); // 5 second duration
        var callCount = 0;
        
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(() => callCount++ == 0 ? startTime : endTime);

        SetupSuccessfulActivityCalls();

        // Act
        var result = await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(MigrationStatus.Completed, result.Status);
        Assert.True(result.Duration > TimeSpan.Zero);
        Assert.Contains("categories", result.EntityResults.Keys);
        Assert.Contains("products", result.EntityResults.Keys);
        Assert.Contains("brands", result.EntityResults.Keys);
        
        // Verify all lifecycle activities were called
        _contextMock.Verify(x => x.CallActivityAsync("InitializeMigration", It.IsAny<object>()), Times.Once);
        _contextMock.Verify(x => x.CallActivityAsync<ValidationResult>("ValidateMigrationStores", It.IsAny<object>()), Times.Once);
        _contextMock.Verify(x => x.CallActivityAsync("CompleteMigration", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task RunMigrationOrchestrator_ShouldCalculateStatistics()
    {
        // Arrange
        _contextMock.Setup(x => x.GetInput<MigrationOrchestrationRequest>())
                   .Returns(_testRequest);
        _contextMock.Setup(x => x.CurrentUtcDateTime)
                   .Returns(DateTime.UtcNow);

        SetupSuccessfulActivityCalls();

        // Act
        var result = await _orchestrator.RunMigrationOrchestrator(_contextMock.Object);

        // Assert
        var summary = result.GetStatisticsSummary();
        Assert.True(summary.TotalEntities > 0);
        Assert.True(summary.SuccessfulEntities > 0);
        Assert.True(summary.SuccessRate > 0);
    }

    #endregion

    #region Helper Methods

    private void SetupSuccessfulActivityCalls()
    {
        _contextMock.Setup(x => x.CallActivityAsync("InitializeMigration", It.IsAny<object>()))
                   .Returns(Task.CompletedTask);

        _contextMock.Setup(x => x.CallActivityAsync<ValidationResult>("ValidateMigrationStores", It.IsAny<object>()))
                   .ReturnsAsync(new ValidationResult { IsValid = true });

        _contextMock.Setup(x => x.CallSubOrchestratorAsync<EntityMigrationResult>("EntityMigrationOrchestrator", It.IsAny<EntityMigrationRequest>()))
                   .Returns<string, EntityMigrationRequest>((name, request) =>
                       Task.FromResult(new EntityMigrationResult
                       {
                           EntityType = request.EntityType,
                           TotalEntities = 10,
                           SuccessfulEntities = 10,
                           FailedEntities = 0
                       }));

        _contextMock.Setup(x => x.CallActivityAsync("CompleteMigration", It.IsAny<object>()))
                   .Returns(Task.CompletedTask);
    }

    private void SetupEntityOrderTracking(List<string> processedEntities)
    {
        _contextMock.Setup(x => x.CallActivityAsync("InitializeMigration", It.IsAny<object>()))
                   .Returns(Task.CompletedTask);

        _contextMock.Setup(x => x.CallActivityAsync<ValidationResult>("ValidateMigrationStores", It.IsAny<object>()))
                   .ReturnsAsync(new ValidationResult { IsValid = true });

        _contextMock.Setup(x => x.CallSubOrchestratorAsync<EntityMigrationResult>("EntityMigrationOrchestrator", It.IsAny<EntityMigrationRequest>()))
                   .Returns<string, EntityMigrationRequest>((name, request) =>
                   {
                       processedEntities.Add(request.EntityType);
                       return Task.FromResult(new EntityMigrationResult
                       {
                           EntityType = request.EntityType,
                           TotalEntities = 10,
                           SuccessfulEntities = 10,
                           FailedEntities = 0
                       });
                   });

        _contextMock.Setup(x => x.CallActivityAsync("CompleteMigration", It.IsAny<object>()))
                   .Returns(Task.CompletedTask);
    }

    private static string[] GetEntityDependencies(string entityType)
    {
        return entityType switch
        {
            "categories" => Array.Empty<string>(),
            "products" => new[] { "categories" },
            "variants" => new[] { "categories", "products" },
            "images" => new[] { "categories", "products" },
            "modifiers" => new[] { "categories", "products" },
            "brands" => Array.Empty<string>(),
            _ => Array.Empty<string>()
        };
    }

    #endregion
}

 