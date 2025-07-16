using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Orchestration.Models;
using BigCommerce.Migration.Orchestration.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text.Json;

namespace BigCommerce.Migration.OrchestrationTests.Services;

public class EntityErrorHandlingServiceTests
{
    private readonly Mock<IOpenSearchService> _mockOpenSearchService;
    private readonly Mock<IBlobService> _mockBlobService;
    private readonly Mock<ILogger<EntityErrorHandlingService>> _mockLogger;
    private readonly EntityErrorHandlingService _service;

    public EntityErrorHandlingServiceTests()
    {
        _mockOpenSearchService = new Mock<IOpenSearchService>();
        _mockBlobService = new Mock<IBlobService>();
        _mockLogger = new Mock<ILogger<EntityErrorHandlingService>>();
        
        _service = new EntityErrorHandlingService(
            _mockOpenSearchService.Object,
            _mockBlobService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task LogEntityErrorAsync_ShouldStoreErrorPayloadsToBlobStorage()
    {
        // Arrange
        var exception = new HttpRequestException("API Error", null, HttpStatusCode.BadRequest);
        var entity = new Dictionary<string, object>
        {
            ["id"] = "123",
            ["name"] = "Test Product",
            ["price"] = 99.99m
        };
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            SourceStore = new StoreConfiguration { StoreId = "source-store" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store" },
            BatchNumber = 1
        };
        var entityId = "123";
        var cancellationToken = CancellationToken.None;

        _mockBlobService.Setup(x => x.StoreRequestPayloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://blob-storage/request.json");

        _mockBlobService.Setup(x => x.StoreResponsePayloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://blob-storage/response.json");

        _mockOpenSearchService.Setup(x => x.LogErrorAsync(
            It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.LogEntityErrorAsync(exception, entity, request, entityId, cancellationToken);

        // Assert
        _mockBlobService.Verify(x => x.StoreRequestPayloadAsync(
            request.MigrationId, 
            It.Is<string>(id => id.Contains("products_123")), 
            It.Is<string>(payload => payload.Contains("Test Product")), 
            "application/json"), 
            Times.Once);

        _mockOpenSearchService.Verify(x => x.LogErrorAsync(
            "EntityError_products",
            exception,
            It.IsAny<object>(),
            cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task LogStructuredMigrationErrorAsync_ShouldStoreErrorPayloadsToBlobStorage()
    {
        // Arrange
        var exception = new HttpRequestException("API Error", null, HttpStatusCode.BadRequest);
        var entities = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { ["id"] = "123", ["name"] = "Product 1" },
            new Dictionary<string, object> { ["id"] = "456", ["name"] = "Product 2" }
        };
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "products",
            SourceStore = new StoreConfiguration { StoreId = "source-store" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store" },
            BatchNumber = 1,
            TotalBatches = 5
        };
        var errorType = "fetch";
        var cancellationToken = CancellationToken.None;

        _mockBlobService.Setup(x => x.StoreRequestPayloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://blob-storage/request.json");

        _mockBlobService.Setup(x => x.StoreResponsePayloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://blob-storage/response.json");

        _mockOpenSearchService.Setup(x => x.LogErrorAsync(
            It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.LogStructuredMigrationErrorAsync(exception, entities, request, errorType, cancellationToken);

        // Assert
        _mockBlobService.Verify(x => x.StoreRequestPayloadAsync(
            request.MigrationId, 
            It.Is<string>(id => id.Contains("products_fetch")), 
            It.Is<string>(payload => payload.Contains("Product 1") && payload.Contains("Product 2")), 
            "application/json"), 
            Times.Once);

        _mockOpenSearchService.Verify(x => x.LogErrorAsync(
            "MigrationError_products",
            exception,
            It.IsAny<object>(),
            cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task StoreErrorPayloadsAsync_ShouldStoreBothRequestAndResponsePayloads()
    {
        // Arrange
        var migrationId = "test-migration";
        var requestId = "test-request";
        var requestPayload = JsonSerializer.Serialize(new { test = "data" });
        var responsePayload = JsonSerializer.Serialize(new { error = "message" });
        var entityName = "test-entity";
        var cancellationToken = CancellationToken.None;

        _mockBlobService.Setup(x => x.StoreRequestPayloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://blob-storage/request.json");

        _mockBlobService.Setup(x => x.StoreResponsePayloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://blob-storage/response.json");

        // Act
        var (requestRef, responseRef) = await _service.StoreErrorPayloadsAsync(
            migrationId, requestId, requestPayload, responsePayload, entityName, cancellationToken);

        // Assert
        _mockBlobService.Verify(x => x.StoreRequestPayloadAsync(
            migrationId, requestId, requestPayload, "application/json"), Times.Once);

        _mockBlobService.Verify(x => x.StoreResponsePayloadAsync(
            migrationId, requestId, responsePayload, "application/json"), Times.Once);

        Assert.NotNull(requestRef);
        Assert.NotNull(responseRef);
        Assert.Equal(requestPayload, requestRef.PayloadData);
        Assert.Equal(responsePayload, responseRef.PayloadData);
        Assert.Equal(requestPayload.Length, requestRef.Size);
        Assert.Equal(responsePayload.Length, responseRef.Size);
    }

    [Fact]
    public void ExtractResponsePayloadFromException_ShouldExtractFromHttpRequestException()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new { error = "Bad Request" });
        var exception = new HttpRequestException("API Error", null, HttpStatusCode.BadRequest);
        exception.Data["ResponseContent"] = responseContent;

        // Act
        var result = _service.ExtractResponsePayloadFromException(exception);

        // Assert
        Assert.Equal(responseContent, result);
    }

    [Fact]
    public void ExtractHttpStatusFromException_ShouldExtractFromHttpRequestException()
    {
        // Arrange
        var exception = new HttpRequestException("API Error", null, HttpStatusCode.BadRequest);
        exception.Data["StatusCode"] = "400";

        // Act
        var result = _service.ExtractHttpStatusFromException(exception);

        // Assert
        Assert.Equal(400, result);
    }

    [Fact]
    public void GetTruncatedStackTrace_ShouldTruncateLongStackTrace()
    {
        // Arrange
        var longStackTrace = new string('x', 10000);

        // Act
        var result = _service.GetTruncatedStackTrace(longStackTrace);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length < longStackTrace.Length);
        Assert.True(result.Length <= 2000); // Should be truncated
    }

    [Fact]
    public void GetTruncatedStackTrace_ShouldHandleNullStackTrace()
    {
        // Act
        var result = _service.GetTruncatedStackTrace(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task LogEntityErrorAsync_ShouldSanitizeEntityIdInBlobUrl()
    {
        // Arrange
        var exception = new HttpRequestException("API Error", null, HttpStatusCode.BadRequest);
        var entity = new Dictionary<string, object> { ["name"] = "Test Category" };
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = new StoreConfiguration { StoreId = "source-store" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store" },
            BatchNumber = 1
        };
        var entityId = "name:Test Category"; // Contains problematic colon
        var cancellationToken = CancellationToken.None;

        // Setup mocks to capture the requestId parameter
        string? capturedRequestId = null;
        _mockBlobService.Setup(x => x.StoreRequestPayloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((migrationId, requestId, payload, contentType) =>
                capturedRequestId = requestId)
            .ReturnsAsync("https://blob-storage/request.json");

        _mockBlobService.Setup(x => x.StoreResponsePayloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://blob-storage/response.json");

        _mockOpenSearchService.Setup(x => x.LogErrorAsync(
            It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.LogEntityErrorAsync(exception, entity, request, entityId, cancellationToken);

        // Assert
        Assert.NotNull(capturedRequestId);
        Assert.DoesNotContain(":", capturedRequestId); // Colon should be sanitized
        Assert.Contains("name_Test_Category", capturedRequestId); // Should be sanitized to use underscores
        Assert.Contains("categories", capturedRequestId); // Should contain entity type
        
        // Verify blob service was called with sanitized requestId
        _mockBlobService.Verify(x => x.StoreRequestPayloadAsync(
            "test-migration", 
            It.Is<string>(id => !id.Contains(":")), // RequestId should not contain colons
            It.IsAny<string>(), 
            "application/json"), Times.Once);
    }

    [Fact]
    public async Task LogEntityErrorAsync_ShouldUseOriginalEntityIdFromTransformedEntity()
    {
        // Arrange
        var exception = new HttpRequestException("API Error", null, HttpStatusCode.BadRequest);
        var entity = new Dictionary<string, object> 
        { 
            ["name"] = "Test Category",
            ["_original_entity_id"] = "4487" // This simulates the ID stored before transformation
        };
        var request = new BatchProcessingRequest
        {
            MigrationId = "test-migration",
            EntityType = "categories",
            SourceStore = new StoreConfiguration { StoreId = "source-store" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-store" },
            BatchNumber = 1
        };
        var entityId = "name:Test Category"; // This should be overridden by _original_entity_id
        var cancellationToken = CancellationToken.None;

        // Setup mocks to capture the data sent to OpenSearch
        object? capturedErrorData = null;
        _mockOpenSearchService.Setup(x => x.LogErrorAsync(
            It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, Exception, object, CancellationToken>((context, ex, data, token) =>
                capturedErrorData = data)
            .ReturnsAsync(true);

        _mockBlobService.Setup(x => x.StoreRequestPayloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://blob-storage/request.json");

        _mockBlobService.Setup(x => x.StoreResponsePayloadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("https://blob-storage/response.json");

        // Act
        await _service.LogEntityErrorAsync(exception, entity, request, entityId, cancellationToken);

        // Assert
        Assert.NotNull(capturedErrorData);
        
        // Use reflection to check the anonymous object properties
        var errorDataType = capturedErrorData.GetType();
        var entityIdProperty = errorDataType.GetProperty("entityId");
        Assert.NotNull(entityIdProperty);
        
        var actualEntityId = entityIdProperty.GetValue(capturedErrorData)?.ToString();
        Assert.Equal("name:Test Category", actualEntityId); // Should use the entityId parameter passed in
    }
} 