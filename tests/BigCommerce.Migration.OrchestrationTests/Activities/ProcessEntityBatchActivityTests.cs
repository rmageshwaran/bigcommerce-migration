using Microsoft.Extensions.Logging;
using Moq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Activities;
using BigCommerce.Migration.Orchestration.Models;
using Xunit;

namespace BigCommerce.Migration.OrchestrationTests.Activities;

/// <summary>
/// Tests for the ProcessEntityBatch activity function
/// These tests define the expected behavior - implementation will follow (TDD)
/// Following user requirements: Continue on failures, log all errors at sub-component level, 12 req/sec rate limiting
/// </summary>
public class ProcessEntityBatchActivityTests
{
    private readonly Mock<IBigCommerceApiClient> _apiClientMock;
    private readonly Mock<ILogger<ProcessEntityBatchActivity>> _loggerMock;
    private readonly Mock<IRateLimitService> _rateLimitServiceMock;
    private readonly Mock<IOpenSearchService> _openSearchServiceMock;
    private readonly Mock<IMigrationStorageService> _migrationStorageServiceMock;
    private readonly Mock<IBlobService> _blobServiceMock;
    private readonly ProcessEntityBatchActivity _activity;

    public ProcessEntityBatchActivityTests()
    {
        _apiClientMock = new Mock<IBigCommerceApiClient>();
        _loggerMock = new Mock<ILogger<ProcessEntityBatchActivity>>();
        _rateLimitServiceMock = new Mock<IRateLimitService>();
        _openSearchServiceMock = new Mock<IOpenSearchService>();
        _migrationStorageServiceMock = new Mock<IMigrationStorageService>();
        _blobServiceMock = new Mock<IBlobService>();
        
        _activity = new ProcessEntityBatchActivity(
            _apiClientMock.Object,
            _loggerMock.Object,
            _rateLimitServiceMock.Object,
            _openSearchServiceMock.Object,
            _migrationStorageServiceMock.Object,
            _blobServiceMock.Object);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_ValidProductBatch_ReturnsSuccessResult()
    {
        // Arrange
        var request = CreateValidBatchRequest("products", new[] { "100", "101", "102" });

        // Mock source products
        var sourceProducts = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Product 1" }, { "price", 10.99 } },
            new Dictionary<string, object> { { "id", 101 }, { "name", "Product 2" }, { "price", 20.99 } },
            new Dictionary<string, object> { { "id", 102 }, { "name", "Product 3" }, { "price", 30.99 } }
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.Is<StoreConfiguration>(s => s.StoreId == "source-store"),
                "products",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BigCommercePaginatedResponse<Dictionary<string, object>>
            {
                Data = sourceProducts,
                ApiVersion = BigCommerceApiVersion.V3
            });

        // Mock destination product creation
        _apiClientMock.Setup(x => x.CreateProductsAsync(
                It.Is<StoreConfiguration>(s => s.StoreId == "dest-store"),
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "id", 200 }, { "name", "Product 1" } },
                new Dictionary<string, object> { { "id", 201 }, { "name", "Product 2" } },
                new Dictionary<string, object> { { "id", 202 }, { "name", "Product 3" } }
            });

        // Mock rate limiting - always allow
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true, DelayMs = 0 });

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.BatchNumber);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(3, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.Empty(result.Errors);
        Assert.Equal(3, result.EntityMappings.Count);
        
        // Verify entity mappings
        Assert.Contains(result.EntityMappings, m => m.SourceId == "100" && m.DestinationId == "200");
        Assert.Contains(result.EntityMappings, m => m.SourceId == "101" && m.DestinationId == "201");
        Assert.Contains(result.EntityMappings, m => m.SourceId == "102" && m.DestinationId == "202");
        
        // Verify all mappings have correct metadata
        foreach (var mapping in result.EntityMappings)
        {
            Assert.Equal("test-migration-123", mapping.MigrationId);
            Assert.Equal("products", mapping.EntityType);
            Assert.Equal("source-store", mapping.SourceStoreId);
            Assert.Equal("dest-store", mapping.DestinationStoreId);
        }
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_ValidCategoryBatch_ReturnsSuccessResult()
    {
        // Arrange
        var request = CreateValidBatchRequest("categories", new[] { "10", "11" });
        request.CategoryTreeContext = new CategoryTreeContext
        {
            SourceCategoryTreeId = "1",
            DestinationCategoryTreeId = "2"
        };

        // Mock source categories
        var sourceCategories = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 10 }, { "name", "Category 1" }, { "parent_id", 0 } },
            new Dictionary<string, object> { { "id", 11 }, { "name", "Category 2" }, { "parent_id", 10 } }
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.Is<StoreConfiguration>(s => s.StoreId == "source-store"),
                "categories",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BigCommercePaginatedResponse<Dictionary<string, object>>
            {
                Data = sourceCategories,
                ApiVersion = BigCommerceApiVersion.V3
            });

        _apiClientMock.Setup(x => x.CreateCategoriesAsync(
                It.Is<StoreConfiguration>(s => s.StoreId == "dest-store"),
                "2",
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "id", 20 }, { "name", "Category 1" } },
                new Dictionary<string, object> { { "id", 21 }, { "name", "Category 2" } }
            });

        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true, DelayMs = 0 });

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalProcessed);
        Assert.Equal(2, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.Equal(2, result.EntityMappings.Count);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_PartialFailures_ContinuesProcessingAndLogsErrors()
    {
        // Arrange - Test user requirement: "Continue on failures, log all errors"
        var request = CreateValidBatchRequest("products", new[] { "100", "101", "102" });

        var sourceProducts = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Valid Product" } },
            new Dictionary<string, object> { { "id", 101 }, { "name", "Product with Invalid Price" }, { "price", -10.99 } },
            new Dictionary<string, object> { { "id", 102 }, { "name", "Another Valid Product" } }
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                "products",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BigCommercePaginatedResponse<Dictionary<string, object>>
            {
                Data = sourceProducts,
                ApiVersion = BigCommerceApiVersion.V3
            });

        // Mock destination creation - first and third succeed, second fails
        _apiClientMock.SetupSequence(x => x.CreateProductsAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "id", 200 }, { "name", "Valid Product" } }
            })
            .ThrowsAsync(new BigCommerceApiException("Validation failed: Price cannot be negative"))
            .ReturnsAsync(new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "id", 202 }, { "name", "Another Valid Product" } }
            });

        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true, DelayMs = 0 });

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request);

        // Assert - Should continue processing despite failures
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(2, result.SuccessfulEntities); // First and third succeeded
        Assert.Equal(1, result.FailedEntities);     // Second failed
        Assert.Single(result.Errors); // One error logged
        Assert.Contains("Validation failed: Price cannot be negative", result.Errors[0]);
        Assert.Equal(2, result.EntityMappings.Count); // Only successful mappings
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_RateLimitDelay_RespectsDelayAndContinues()
    {
        // Arrange - Test rate limiting requirement
        var request = CreateValidBatchRequest("products", new[] { "100" });

        var sourceProducts = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Product 1" } }
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                "products",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BigCommercePaginatedResponse<Dictionary<string, object>>
            {
                Data = sourceProducts,
                ApiVersion = BigCommerceApiVersion.V3
            });

        _apiClientMock.Setup(x => x.CreateProductsAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "id", 200 }, { "name", "Product 1" } }
            });

        // Mock rate limiting - first call requires delay
        _rateLimitServiceMock.SetupSequence(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = false, DelayMs = 1000 })
            .ReturnsAsync(new RateLimitResult { CanProceed = true, DelayMs = 0 });

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        
        // Verify rate limit service was called twice (once for delay, once for proceed)
        _rateLimitServiceMock.Verify(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_UnsupportedEntityType_ReturnsErrorResult()
    {
        // Arrange
        var request = CreateValidBatchRequest("unsupported", new[] { "1" });

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Unsupported entity type", result.Errors[0]);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_EmptyBatch_ReturnsEmptyResult()
    {
        // Arrange
        var request = CreateValidBatchRequest("products", Array.Empty<string>());

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.Empty(result.Errors);
        Assert.Empty(result.EntityMappings);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_InvalidRequest_ReturnsErrorResult()
    {
        // Arrange
        var request = new BatchProcessingRequest
        {
            MigrationId = "", // Invalid - empty migration ID
            EntityType = "products",
            BatchNumber = 1,
            TotalBatches = 1,
            EntityIds = new List<string> { "100" }
        };

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("MigrationId is required", result.Errors[0]);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_SourceStoreApiError_ReturnsErrorResult()
    {
        // Arrange
        var request = CreateValidBatchRequest("products", new[] { "100" });

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                "products",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BigCommerceApiException("Source store API error"));

        // Mock rate limiting - always allow
        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true, DelayMs = 0 });

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Source store API error", result.Errors[0]);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_ValidBatch_LogsProgressToOpenSearch()
    {
        // Arrange
        var request = CreateValidBatchRequest("products", new[] { "100" });

        var sourceProducts = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Product 1" } }
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                "products",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BigCommercePaginatedResponse<Dictionary<string, object>>
            {
                Data = sourceProducts,
                ApiVersion = BigCommerceApiVersion.V3
            });

        _apiClientMock.Setup(x => x.CreateProductsAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "id", 200 }, { "name", "Product 1" } }
            });

        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true, DelayMs = 0 });

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.SuccessfulEntities);
        
        // Verify OpenSearch logging was called for batch processing
        _openSearchServiceMock.Verify(x => x.LogEntityBatchProcessingAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_ValidBatch_StoresEntityMappings()
    {
        // Arrange
        var request = CreateValidBatchRequest("products", new[] { "100" });

        var sourceProducts = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Product 1" } }
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                "products",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BigCommercePaginatedResponse<Dictionary<string, object>>
            {
                Data = sourceProducts,
                ApiVersion = BigCommerceApiVersion.V3
            });

        _apiClientMock.Setup(x => x.CreateProductsAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "id", 200 }, { "name", "Product 1" } }
            });

        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true, DelayMs = 0 });

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.SuccessfulEntities);
        
        // Verify entity mappings were stored (once individually for hierarchy + once as fallback batch)
        _migrationStorageServiceMock.Verify(x => x.StoreEntityMappingsAsync(
            It.Is<List<EntityMapping>>(mappings => 
                mappings.Count == 1 && 
                mappings[0].SourceId == "100" && 
                mappings[0].DestinationId == "200")),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_WithCancellation_HandlesCancellationGracefully()
    {
        // Arrange - Test critical cancellation requirement
        var request = CreateValidBatchRequest("products", new[] { "100" });
        var cancellationToken = new CancellationToken(true); // Already cancelled

        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true, DelayMs = 0 });

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request, cancellationToken);

        // Assert - Should handle cancellation gracefully
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Equal(0, result.SuccessfulEntities);
        Assert.Equal(0, result.FailedEntities);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("Migration was cancelled", result.Errors[0]);
    }

    [Fact]
    public async Task ProcessEntityBatchAsync_WithCancellationDuringProcessing_HandlesGracefulShutdown()
    {
        // Arrange - Test cancellation during entity processing
        var request = CreateValidBatchRequest("products", new[] { "100", "101", "102" });
        var cancellationTokenSource = new CancellationTokenSource();

        var sourceProducts = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "id", 100 }, { "name", "Product 1" } },
            new Dictionary<string, object> { { "id", 101 }, { "name", "Product 2" } },
            new Dictionary<string, object> { { "id", 102 }, { "name", "Product 3" } }
        };

        _apiClientMock.Setup(x => x.GetPaginatedEntitiesAsync(
                It.IsAny<StoreConfiguration>(),
                "products",
                It.IsAny<BigCommercePaginationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BigCommercePaginatedResponse<Dictionary<string, object>>
            {
                Data = sourceProducts,
                ApiVersion = BigCommerceApiVersion.V3
            });

        // Mock product creation to succeed for first call, then cancel for subsequent calls
        var callCount = 0;
        _apiClientMock.Setup(x => x.CreateProductsAsync(
                It.IsAny<StoreConfiguration>(),
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call succeeds
                    return Task.FromResult(new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object> { { "id", 200 }, { "name", "Product 1" } }
                    });
                }
                else
                {
                    // Cancel after first entity
                    cancellationTokenSource.Cancel();
                    throw new OperationCanceledException();
                }
            });

        _rateLimitServiceMock.Setup(x => x.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult { CanProceed = true, DelayMs = 0 });

        // Act
        var result = await _activity.ProcessEntityBatchAsync(request, cancellationTokenSource.Token);

        // Assert - Should process first entity then stop gracefully
        Assert.NotNull(result);
        Assert.True(result.TotalProcessed >= 1); // At least one entity processed
        Assert.True(result.SuccessfulEntities >= 1); // At least one succeeded
        Assert.True(result.EntityMappings.Count >= 1); // At least one mapping created
    }

    private static BatchProcessingRequest CreateValidBatchRequest(string entityType, string[] entityIds)
    {
        return new BatchProcessingRequest
        {
            MigrationId = "test-migration-123",
            EntityType = entityType,
            BatchNumber = 1,
            TotalBatches = 1,
            EntityIds = entityIds.ToList(),
            SourceStore = new StoreConfiguration
            {
                StoreId = "source-store",
                AccessToken = "source-token",
                ChannelId = "1"
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "dest-store",
                AccessToken = "dest-token",
                ChannelId = "1"
            },
            CategoryTreeContext = new CategoryTreeContext()
        };
    }
} 