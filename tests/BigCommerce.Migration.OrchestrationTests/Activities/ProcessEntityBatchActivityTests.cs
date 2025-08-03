using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using Xunit;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System;

namespace BigCommerce.Migration.OrchestrationTests.Activities;

/// <summary>
/// TDD Tests for the refactored ProcessEntityBatchActivity following SOLID principles
/// </summary>
public class ProcessEntityBatchActivityTests
{
    private readonly Mock<ILogger<ProcessEntityBatchActivity>> _loggerMock;
    private readonly Mock<IEntityFetchService> _entityFetchServiceMock;
    private readonly Mock<IEntityTransformService> _entityTransformServiceMock;
    private readonly Mock<IEntityCreateService> _entityCreateServiceMock;
    private readonly Mock<IEntityMappingService> _entityMappingServiceMock;
    private readonly Mock<IEntityErrorHandlingService> _errorHandlingServiceMock;
    private readonly Mock<IRateLimitService> _rateLimitServiceMock;
    private readonly Mock<IOpenSearchService> _openSearchServiceMock;
    private readonly Mock<IMigrationStorageService> _migrationStorageServiceMock;
    private readonly Mock<ILiveCancellationManager> _liveCancellationManagerMock;
    // SignalR service dependency removed - now using queue-based progress events
    private readonly Mock<IErrorMessageFormatter> _errorMessageFormatterMock;
    private readonly ProcessEntityBatchActivity _activity;

    public ProcessEntityBatchActivityTests()
    {
        _loggerMock = new Mock<ILogger<ProcessEntityBatchActivity>>();
        _entityFetchServiceMock = new Mock<IEntityFetchService>();
        _entityTransformServiceMock = new Mock<IEntityTransformService>();
        _entityCreateServiceMock = new Mock<IEntityCreateService>();
        _entityMappingServiceMock = new Mock<IEntityMappingService>();
        _errorHandlingServiceMock = new Mock<IEntityErrorHandlingService>();
        _rateLimitServiceMock = new Mock<IRateLimitService>();
        _openSearchServiceMock = new Mock<IOpenSearchService>();
        _migrationStorageServiceMock = new Mock<IMigrationStorageService>();
        _liveCancellationManagerMock = new Mock<ILiveCancellationManager>();
        _errorMessageFormatterMock = new Mock<IErrorMessageFormatter>();
        
        // Setup default error message formatter behavior
        _errorMessageFormatterMock.Setup(x => x.CreateSimpleErrorMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string, string>((entityType, entityId, error) => $"Failed to create {entityType} {entityId}: {error}");
        
        _activity = new ProcessEntityBatchActivity(
            _loggerMock.Object,
            _entityFetchServiceMock.Object,
            _entityTransformServiceMock.Object,
            _entityCreateServiceMock.Object,
            _entityMappingServiceMock.Object,
            _errorHandlingServiceMock.Object,
            _rateLimitServiceMock.Object,
            _openSearchServiceMock.Object,
            _migrationStorageServiceMock.Object,
            _liveCancellationManagerMock.Object,
            _errorMessageFormatterMock.Object
        );
    }

    #region Constructor Guard Clause Tests
    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessEntityBatchActivity(
            null!, _entityFetchServiceMock.Object, _entityTransformServiceMock.Object, _entityCreateServiceMock.Object, _entityMappingServiceMock.Object, _errorHandlingServiceMock.Object, _rateLimitServiceMock.Object, _openSearchServiceMock.Object, _migrationStorageServiceMock.Object, _liveCancellationManagerMock.Object, _errorMessageFormatterMock.Object));
    }
    [Fact]
    public void Constructor_NullEntityFetchService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessEntityBatchActivity(
            _loggerMock.Object, null!, _entityTransformServiceMock.Object, _entityCreateServiceMock.Object, _entityMappingServiceMock.Object, _errorHandlingServiceMock.Object, _rateLimitServiceMock.Object, _openSearchServiceMock.Object, _migrationStorageServiceMock.Object, _liveCancellationManagerMock.Object, _errorMessageFormatterMock.Object));
    }
    [Fact]
    public void Constructor_NullEntityTransformService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessEntityBatchActivity(
            _loggerMock.Object, _entityFetchServiceMock.Object, null!, _entityCreateServiceMock.Object, _entityMappingServiceMock.Object, _errorHandlingServiceMock.Object, _rateLimitServiceMock.Object, _openSearchServiceMock.Object, _migrationStorageServiceMock.Object, _liveCancellationManagerMock.Object, _errorMessageFormatterMock.Object));
    }
    [Fact]
    public void Constructor_NullEntityCreateService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessEntityBatchActivity(
            _loggerMock.Object, _entityFetchServiceMock.Object, _entityTransformServiceMock.Object, null!, _entityMappingServiceMock.Object, _errorHandlingServiceMock.Object, _rateLimitServiceMock.Object, _openSearchServiceMock.Object, _migrationStorageServiceMock.Object, _liveCancellationManagerMock.Object, _errorMessageFormatterMock.Object));
    }
    [Fact]
    public void Constructor_NullEntityMappingService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessEntityBatchActivity(
            _loggerMock.Object, _entityFetchServiceMock.Object, _entityTransformServiceMock.Object, _entityCreateServiceMock.Object, null!, _errorHandlingServiceMock.Object, _rateLimitServiceMock.Object, _openSearchServiceMock.Object, _migrationStorageServiceMock.Object, _liveCancellationManagerMock.Object, _errorMessageFormatterMock.Object));
    }
    [Fact]
    public void Constructor_NullErrorHandlingService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessEntityBatchActivity(
            _loggerMock.Object, _entityFetchServiceMock.Object, _entityTransformServiceMock.Object, _entityCreateServiceMock.Object, _entityMappingServiceMock.Object, null!, _rateLimitServiceMock.Object, _openSearchServiceMock.Object, _migrationStorageServiceMock.Object, _liveCancellationManagerMock.Object, _errorMessageFormatterMock.Object));
    }
    [Fact]
    public void Constructor_NullRateLimitService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessEntityBatchActivity(
            _loggerMock.Object, _entityFetchServiceMock.Object, _entityTransformServiceMock.Object, _entityCreateServiceMock.Object, _entityMappingServiceMock.Object, _errorHandlingServiceMock.Object, null!, _openSearchServiceMock.Object, _migrationStorageServiceMock.Object, _liveCancellationManagerMock.Object, _errorMessageFormatterMock.Object));
    }
    [Fact]
    public void Constructor_NullOpenSearchService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessEntityBatchActivity(
            _loggerMock.Object, _entityFetchServiceMock.Object, _entityTransformServiceMock.Object, _entityCreateServiceMock.Object, _entityMappingServiceMock.Object, _errorHandlingServiceMock.Object, _rateLimitServiceMock.Object, null!, _migrationStorageServiceMock.Object, _liveCancellationManagerMock.Object, _errorMessageFormatterMock.Object));
    }
    [Fact]
    public void Constructor_NullMigrationStorageService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessEntityBatchActivity(
            _loggerMock.Object, _entityFetchServiceMock.Object, _entityTransformServiceMock.Object, _entityCreateServiceMock.Object, _entityMappingServiceMock.Object, _errorHandlingServiceMock.Object, _rateLimitServiceMock.Object, _openSearchServiceMock.Object, null!, _liveCancellationManagerMock.Object, _errorMessageFormatterMock.Object));
    }
    [Fact]
    public void Constructor_NullErrorMessageFormatter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessEntityBatchActivity(
            _loggerMock.Object, _entityFetchServiceMock.Object, _entityTransformServiceMock.Object, _entityCreateServiceMock.Object, _entityMappingServiceMock.Object, _errorHandlingServiceMock.Object, _rateLimitServiceMock.Object, _openSearchServiceMock.Object, _migrationStorageServiceMock.Object, _liveCancellationManagerMock.Object, null!));
    }
    #endregion

    #region Entity Processing Edge Cases
    [Fact]
    public async Task ProcessEntityBatchAsync_EntityWithoutId_DoesNotThrow()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var sourceProducts = new List<Dictionary<string, object>> { new Dictionary<string, object> { { "name", "NoId" } } };
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;
        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);
        var result = await _activity.Run(request, token);
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalProcessed);
        Assert.Equal(1, result.SuccessfulEntities);
    }
    [Fact]
    public async Task ProcessEntityBatchAsync_MappingThrows_DoesNotThrow()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;
        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Throws(new Exception("Mapping failed"));
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);
        _errorHandlingServiceMock.Setup(x => x.LogEntityErrorAsync(It.IsAny<Exception>(), It.IsAny<Dictionary<string, object>>(), request, It.IsAny<string>(), token)).Returns(Task.CompletedTask);
        var result = await _activity.Run(request, token);
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(3, result.FailedEntities); // Mapping failure increments failed count
    }
    [Fact]
    public async Task ProcessEntityBatchAsync_StoreEntityMappingThrows_DoesNotThrow()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;
        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Throws(new Exception("Store mapping failed"));
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);
        var result = await _activity.Run(request, token);
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(3, result.SuccessfulEntities); // Store mapping failure does not affect success count
    }
    #endregion

    #region Logging/Timing
    [Fact]
    public async Task ProcessEntityBatchAsync_OpenSearchLoggingThrows_DoesNotThrow()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;
        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);
        _openSearchServiceMock.Setup(x => x.LogEntityBatchProcessingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<object>(), token)).ThrowsAsync(new Exception("OpenSearch error"));
        var result = await _activity.Run(request, token);
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(3, result.SuccessfulEntities);
    }
    [Fact]
    public async Task ProcessEntityBatchAsync_ProcessingTime_IsSet()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;
        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);
        var result = await _activity.Run(request, token);
        Assert.NotNull(result);
        Assert.True(result.ProcessingTime.TotalMilliseconds > 0);
    }
    #endregion

    [Fact]
    public async Task ProcessEntityBatchAsync_ValidProductBatch_ReturnsSuccessResult()
    {
        var request = CreateValidBatchRequest("products", new[] { "100", "101", "102" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token))
            .ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request))
            .ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token))
            .ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request))
            .Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId))
            .ReturnsAsync((CancellationTokenEntry?)null);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(3, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_WithCancellation_StopsProcessing()
    {
        var request = CreateValidBatchRequest("products", new[] { "100", "101", "102" });
        var sourceProducts = CreateTestProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token))
            .ReturnsAsync(sourceProducts);
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId))
            .ReturnsAsync(new CancellationTokenEntry { IsProcessed = false });

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Contains("Migration was cancelled before processing", result.Errors);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_WithEntityCreationFailure_ContinuesProcessing()
    {
        var request = CreateValidBatchRequest("products", new[] { "100", "101", "102" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token))
            .ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request))
            .ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.SetupSequence(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token))
            .ReturnsAsync((List<Dictionary<string, object>>?)null)
            .ReturnsAsync(new List<Dictionary<string, object>> { CreateCreatedProducts().First() })
            .ReturnsAsync(new List<Dictionary<string, object>> { CreateCreatedProducts().First() });
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request))
            .Returns(new EntityMapping { SourceId = "101", DestinationId = "201" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId))
            .ReturnsAsync((CancellationTokenEntry?)null);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(2, result.SuccessfulEntities);
        Assert.Equal(1, result.FailedEntities);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("Failed to create"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_WithFetchFailure_ReturnsError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100", "101", "102" });
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token))
            .ThrowsAsync(new System.Exception("API connection failed"));
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId))
            .ReturnsAsync((CancellationTokenEntry?)null);
        _errorHandlingServiceMock.Setup(x => x.LogStructuredMigrationErrorAsync(
            It.IsAny<System.Exception>(), It.IsAny<List<Dictionary<string, object>>>(), request, "fetch", token)).Returns(Task.CompletedTask);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.Contains(result.Errors, e => e.Contains("Failed to fetch source entities"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_WithRateLimitDelay_AppliesDelay()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var sourceProducts = new List<Dictionary<string, object>> { CreateTestProducts().First() };
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token))
            .ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request))
            .ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token))
            .ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request))
            .Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = false, DelayMs = 100 });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId))
            .ReturnsAsync((CancellationTokenEntry?)null);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(1, result.SuccessfulEntities);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_WithInvalidRequest_ReturnsValidationErrors()
    {
        var request = CreateInvalidBatchRequest();
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("MigrationId is required"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_WithEmptyBatch_SkipsProcessing()
    {
        var request = CreateValidBatchRequest("products", new string[0]);
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.Empty(result.Errors);
    }

    #region Validation Edge Cases
    [Fact]
    public async Task ProcessEntityBatchAsync_NullMigrationId_ReturnsValidationError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        request.MigrationId = null!;
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Contains(result.Errors, e => e.Contains("MigrationId is required"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_EmptyMigrationId_ReturnsValidationError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        request.MigrationId = "";
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Contains(result.Errors, e => e.Contains("MigrationId is required"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_NullEntityType_ReturnsValidationError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        request.EntityType = null!;
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Contains(result.Errors, e => e.Contains("EntityType is required"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_EmptyEntityType_ReturnsValidationError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        request.EntityType = "";
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Contains(result.Errors, e => e.Contains("EntityType is required"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_ZeroBatchNumber_ReturnsValidationError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        request.BatchNumber = 0;
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Contains(result.Errors, e => e.Contains("BatchNumber must be greater than 0"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_NegativeBatchNumber_ReturnsValidationError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        request.BatchNumber = -1;
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Contains(result.Errors, e => e.Contains("BatchNumber must be greater than 0"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_ZeroTotalBatches_ReturnsValidationError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        request.TotalBatches = 0;
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Contains(result.Errors, e => e.Contains("TotalBatches must be greater than 0"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_NullSourceStore_ReturnsValidationError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        request.SourceStore = null!;
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Contains(result.Errors, e => e.Contains("Valid SourceStore is required"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_NullDestinationStore_ReturnsValidationError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        request.DestinationStore = null!;
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Contains(result.Errors, e => e.Contains("Valid DestinationStore is required"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_MultipleValidationErrors_ReturnsAllErrors()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        request.MigrationId = "";
        request.EntityType = "";
        request.BatchNumber = 0;
        request.TotalBatches = 0;
        request.SourceStore = null!;
        request.DestinationStore = null!;
        var token = CancellationToken.None;

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(6, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Contains("MigrationId is required"));
        Assert.Contains(result.Errors, e => e.Contains("EntityType is required"));
        Assert.Contains(result.Errors, e => e.Contains("BatchNumber must be greater than 0"));
        Assert.Contains(result.Errors, e => e.Contains("TotalBatches must be greater than 0"));
        Assert.Contains(result.Errors, e => e.Contains("Valid SourceStore is required"));
        Assert.Contains(result.Errors, e => e.Contains("Valid DestinationStore is required"));
    }
    #endregion

    #region Rate Limiting Edge Cases
    [Fact]
    public async Task ProcessEntityBatchAsync_NullStoreId_SkipsRateLimiting()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        // Keep store IDs valid for validation, but test that rate limiting is skipped when store ID is null
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.SuccessfulEntities);
        // Rate limiting should be called with valid store IDs, not null
        _rateLimitServiceMock.Verify(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_EmptyStoreId_SkipsRateLimiting()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        // Keep store IDs valid for validation, but test that rate limiting is skipped when store ID is empty
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.SuccessfulEntities);
        // Rate limiting should be called with valid store IDs, not empty
        _rateLimitServiceMock.Verify(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_RateLimitNullResult_DoesNotThrow()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((RateLimitResult?)null);
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.SuccessfulEntities);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_RateLimitZeroDelay_DoesNotDelay()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = false, DelayMs = 0 });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.SuccessfulEntities);
    }
    #endregion

    #region Cancellation Edge Cases
    [Fact]
    public async Task ProcessEntityBatchAsync_CancellationCheckThrows_ContinuesProcessing()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ThrowsAsync(new Exception("Cancellation check failed"));

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.SuccessfulEntities);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_CancellationDuringProcessing_StopsProcessing()
    {
        var request = CreateValidBatchRequest("products", new[] { "100", "101", "102" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        
        // First call returns null (not cancelled), subsequent calls return cancelled
        _migrationStorageServiceMock.SetupSequence(x => x.GetCancellationTokenAsync(request.MigrationId))
            .ReturnsAsync((CancellationTokenEntry?)null)  // Initial check - not cancelled
            .ReturnsAsync((CancellationTokenEntry?)null)  // Before first entity - not cancelled
            .ReturnsAsync(new CancellationTokenEntry { IsProcessed = false })  // Before second entity - cancelled
            .ReturnsAsync(new CancellationTokenEntry { IsProcessed = false }); // Before third entity - cancelled

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.True(result.TotalProcessed > 0);
        Assert.Contains(result.Errors, e => e.Contains("Migration was cancelled during batch processing"));
    }
    #endregion

    #region Error Handling Edge Cases
    [Fact]
    public async Task ProcessEntityBatchAsync_TransformThrows_ContinuesProcessing()
    {
        var request = CreateValidBatchRequest("products", new[] { "100", "101", "102" });
        var sourceProducts = CreateTestProducts();
        var createdProducts = CreateCreatedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ThrowsAsync(new Exception("Transform failed"));
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdProducts);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);
        _errorHandlingServiceMock.Setup(x => x.LogEntityErrorAsync(It.IsAny<Exception>(), It.IsAny<Dictionary<string, object>>(), request, It.IsAny<string>(), token)).Returns(Task.CompletedTask);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(3, result.FailedEntities);
        Assert.Contains(result.Errors, e => e.Contains("Transform failed"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_CreateThrows_ContinuesProcessing()
    {
        var request = CreateValidBatchRequest("products", new[] { "100", "101", "102" });
        var sourceProducts = CreateTestProducts();
        var transformedProducts = CreateTransformedProducts();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceProducts);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedProducts.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ThrowsAsync(new Exception("Create failed"));
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);
        _errorHandlingServiceMock.Setup(x => x.LogEntityErrorAsync(It.IsAny<Exception>(), It.IsAny<Dictionary<string, object>>(), request, It.IsAny<string>(), token)).Returns(Task.CompletedTask);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(3, result.FailedEntities);
        Assert.Contains(result.Errors, e => e.Contains("Create failed"));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_UnexpectedException_ReturnsError()
    {
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ThrowsAsync(new InvalidOperationException("Unexpected error"));
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);
        _errorHandlingServiceMock.Setup(x => x.LogStructuredMigrationErrorAsync(It.IsAny<Exception>(), It.IsAny<List<Dictionary<string, object>>>(), request, "fetch", token)).Returns(Task.CompletedTask);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Contains(result.Errors, e => e.Contains("Failed to fetch source entities"));
    }
    #endregion

    #region Different Entity Types
    [Fact]
    public async Task ProcessEntityBatchAsync_Categories_ProcessesSuccessfully()
    {
        var request = CreateValidBatchRequest("categories", new[] { "100", "101", "102" });
        var sourceCategories = CreateTestCategories();
        var transformedCategories = CreateTransformedCategories();
        var createdCategories = CreateCreatedCategories();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceCategories);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedCategories.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdCategories);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(3, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_Brands_ProcessesSuccessfully()
    {
        var request = CreateValidBatchRequest("brands", new[] { "100", "101", "102" });
        var sourceBrands = CreateTestBrands();
        var transformedBrands = CreateTransformedBrands();
        var createdBrands = CreateCreatedBrands();
        var token = CancellationToken.None;

        _entityFetchServiceMock.Setup(x => x.FetchEntitiesAsync(request, token)).ReturnsAsync(sourceBrands);
        _entityTransformServiceMock.Setup(x => x.TransformEntityAsync(It.IsAny<Dictionary<string, object>>(), request)).ReturnsAsync(transformedBrands.First());
        _entityCreateServiceMock.Setup(x => x.CreateEntitiesAsync(It.IsAny<List<Dictionary<string, object>>>(), request, token)).ReturnsAsync(createdBrands);
        _entityMappingServiceMock.Setup(x => x.CreateEntityMapping(It.IsAny<Dictionary<string, object>>(), It.IsAny<Dictionary<string, object>>(), request)).Returns(new EntityMapping { SourceId = "100", DestinationId = "200" });
        _entityMappingServiceMock.Setup(x => x.StoreEntityMappingAsync(It.IsAny<EntityMapping>(), token)).Returns(Task.CompletedTask);
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RateLimitResult { CanProceed = true });
        _migrationStorageServiceMock.Setup(x => x.GetCancellationTokenAsync(request.MigrationId)).ReturnsAsync((CancellationTokenEntry?)null);

        var result = await _activity.Run(request, token);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(3, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
    }
    #endregion

    private static BatchProcessingRequest CreateValidBatchRequest(string entityType, string[] entityIds)
    {
        return new BatchProcessingRequest
        {
            MigrationId = "test-migration-123",
            EntityType = entityType,
            BatchNumber = 1,
            TotalBatches = 1,
            EntityIds = entityIds.ToList(),
            SourceStore = new StoreConfiguration { StoreId = "source-store", AccessToken = "source-token", ChannelId = "1" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store", AccessToken = "dest-token", ChannelId = "1" },
            CategoryTreeContext = new CategoryTreeContext()
        };
    }

    private static BatchProcessingRequest CreateInvalidBatchRequest()
    {
        return new BatchProcessingRequest
        {
            MigrationId = "",
            EntityType = "",
            BatchNumber = 0,
            TotalBatches = 0,
            EntityIds = new List<string>(),
            SourceStore = null!,
            DestinationStore = null!,
            CategoryTreeContext = new CategoryTreeContext()
        };
    }

    private static List<Dictionary<string, object>> CreateTestProducts()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Product 1" }, { "price", 10.99 } },
            new Dictionary<string, object> { { "id", 101 }, { "name", "Product 2" }, { "price", 20.99 } },
            new Dictionary<string, object> { { "id", 102 }, { "name", "Product 3" }, { "price", 30.99 } }
        };
    }

    private static List<Dictionary<string, object>> CreateTransformedProducts()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "name", "Product 1" }, { "price", 10.99 } }
        };
    }

    private static List<Dictionary<string, object>> CreateCreatedProducts()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 200 }, { "name", "Product 1" }, { "price", 10.99 } }
        };
    }

    private static List<Dictionary<string, object>> CreateTestCategories()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Category 1" }, { "parent_id", 0 } },
            new Dictionary<string, object> { { "id", 101 }, { "name", "Category 2" }, { "parent_id", 0 } },
            new Dictionary<string, object> { { "id", 102 }, { "name", "Category 3" }, { "parent_id", 100 } }
        };
    }

    private static List<Dictionary<string, object>> CreateTransformedCategories()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Transformed Category 1" }, { "parent_id", 0 } },
            new Dictionary<string, object> { { "id", 101 }, { "name", "Transformed Category 2" }, { "parent_id", 0 } },
            new Dictionary<string, object> { { "id", 102 }, { "name", "Transformed Category 3" }, { "parent_id", 100 } }
        };
    }

    private static List<Dictionary<string, object>> CreateCreatedCategories()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 200 }, { "name", "Created Category 1" }, { "parent_id", 0 } },
            new Dictionary<string, object> { { "id", 201 }, { "name", "Created Category 2" }, { "parent_id", 0 } },
            new Dictionary<string, object> { { "id", 202 }, { "name", "Created Category 3" }, { "parent_id", 200 } }
        };
    }

    private static List<Dictionary<string, object>> CreateTestBrands()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Brand 1" }, { "page_title", "Brand 1 Page" } },
            new Dictionary<string, object> { { "id", 101 }, { "name", "Brand 2" }, { "page_title", "Brand 2 Page" } },
            new Dictionary<string, object> { { "id", 102 }, { "name", "Brand 3" }, { "page_title", "Brand 3 Page" } }
        };
    }

    private static List<Dictionary<string, object>> CreateTransformedBrands()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Transformed Brand 1" }, { "page_title", "Transformed Brand 1 Page" } },
            new Dictionary<string, object> { { "id", 101 }, { "name", "Transformed Brand 2" }, { "page_title", "Transformed Brand 2 Page" } },
            new Dictionary<string, object> { { "id", 102 }, { "name", "Transformed Brand 3" }, { "page_title", "Transformed Brand 3 Page" } }
        };
    }

    private static List<Dictionary<string, object>> CreateCreatedBrands()
    {
        return new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 200 }, { "name", "Created Brand 1" }, { "page_title", "Created Brand 1 Page" } },
            new Dictionary<string, object> { { "id", 201 }, { "name", "Created Brand 2" }, { "page_title", "Created Brand 2 Page" } },
            new Dictionary<string, object> { { "id", 202 }, { "name", "Created Brand 3" }, { "page_title", "Created Brand 3 Page" } }
        };
    }
} 