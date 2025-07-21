using FluentAssertions;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// TDD Tests for PaginationApiService
/// Following Single Responsibility Principle - only pagination operations
/// </summary>
public class PaginationApiServiceTests
{
    private readonly Mock<ILogger<PaginationApiService>> _mockLogger;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly PaginationApiService _paginationApiService;
    private readonly StoreConfiguration _testStoreConfig;
    private readonly BigCommercePaginationRequest _testPaginationRequest;

    public PaginationApiServiceTests()
    {
        _mockLogger = new Mock<ILogger<PaginationApiService>>();
        _mockHttpClient = new Mock<HttpClient>();
        _paginationApiService = new PaginationApiService(_mockLogger.Object, _mockHttpClient.Object);
        
        _testStoreConfig = new StoreConfiguration
        {
            StoreId = "test-store",
            AccessToken = "test-token",
            BaseUrl = "https://api.bigcommerce.com",
            ChannelId = "1"
        };

        _testPaginationRequest = new BigCommercePaginationRequest
        {
            Page = 1,
            Limit = 50
        };
    }

    [Fact]
    public void PaginationApiService_Should_Implement_IPaginationApiClient()
    {
        // Arrange & Act
        var service = _paginationApiService;

        // Assert
        service.Should().BeAssignableTo<IPaginationApiClient>("PaginationApiService should implement IPaginationApiClient");
    }

    [Fact]
    public void PaginationApiService_Should_Have_Single_Responsibility()
    {
        // Arrange
        var serviceType = typeof(PaginationApiService);

        // Act & Assert - Check that service implements only the pagination interface
        serviceType.GetInterfaces().Should().Contain(typeof(IPaginationApiClient), 
            "PaginationApiService should implement IPaginationApiClient interface");
            
        // Verify it has the required pagination methods
        serviceType.GetMethod("GetPaginatedEntitiesAsync").Should().NotBeNull("Should have GetPaginatedEntitiesAsync method");
        serviceType.GetMethod("GetAllEntitiesPaginatedAsync").Should().NotBeNull("Should have GetAllEntitiesPaginatedAsync method");
    }

    [Fact]
    public async Task GetPaginatedEntitiesAsync_Should_Handle_Valid_Parameters()
    {
        // Arrange
        var entityType = "products";
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _paginationApiService.GetPaginatedEntitiesAsync(_testStoreConfig, entityType, _testPaginationRequest, cancellationToken);
        
        // Should not throw exception with valid parameters
        await act.Should().NotThrowAsync("Valid parameters should not cause exceptions");
    }

    [Fact]
    public async Task GetAllEntitiesPaginatedAsync_Should_Handle_Valid_Parameters()
    {
        // Arrange
        var entityType = "categories";
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => 
        {
            await foreach (var page in _paginationApiService.GetAllEntitiesPaginatedAsync(_testStoreConfig, entityType, _testPaginationRequest, cancellationToken))
            {
                // Just iterate through one page for testing
                break;
            }
        };
        
        // Should not throw exception with valid parameters
        await act.Should().NotThrowAsync("Valid parameters should not cause exceptions");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetPaginatedEntitiesAsync_Should_Validate_EntityType(string? invalidEntityType)
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _paginationApiService.GetPaginatedEntitiesAsync(_testStoreConfig, invalidEntityType!, _testPaginationRequest, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentException>("Invalid entity type should throw ArgumentException");
    }

    [Fact]
    public async Task GetPaginatedEntitiesAsync_Should_Validate_PaginationRequest_Not_Null()
    {
        // Arrange
        var entityType = "products";
        BigCommercePaginationRequest? nullRequest = null;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _paginationApiService.GetPaginatedEntitiesAsync(_testStoreConfig, entityType, nullRequest!, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentNullException>("Null pagination request should throw ArgumentNullException");
    }

    [Fact]
    public async Task GetPaginatedEntitiesAsync_Should_Validate_Store_Configuration()
    {
        // Arrange
        var invalidStoreConfig = new StoreConfiguration(); // Invalid configuration
        var entityType = "products";
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _paginationApiService.GetPaginatedEntitiesAsync(invalidStoreConfig, entityType, _testPaginationRequest, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentException>("Invalid store configuration should throw ArgumentException");
    }

    [Theory]
    [InlineData("products")]
    [InlineData("categories")]
    [InlineData("brands")]
    [InlineData("customers")]
    public async Task GetPaginatedEntitiesAsync_Should_Handle_Different_Entity_Types(string entityType)
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _paginationApiService.GetPaginatedEntitiesAsync(_testStoreConfig, entityType, _testPaginationRequest, cancellationToken);
        
        // Should handle different entity types without throwing
        await act.Should().NotThrowAsync($"Should handle {entityType} entity type");
    }

    [Fact]
    public async Task GetPaginatedEntitiesAsync_Should_Return_Proper_Response_Structure()
    {
        // Arrange
        var entityType = "products";
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await _paginationApiService.GetPaginatedEntitiesAsync(_testStoreConfig, entityType, _testPaginationRequest, cancellationToken);

        // Assert
        result.Should().NotBeNull("Response should not be null");
        result.Data.Should().NotBeNull("Response data should not be null");
        result.Meta.Should().NotBeNull("Response meta should not be null");
    }

    [Fact]
    public async Task PaginationApiService_Should_Respect_Cancellation_Token()
    {
        // Arrange
        var entityType = "products";
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        var act = async () => await _paginationApiService.GetPaginatedEntitiesAsync(_testStoreConfig, entityType, _testPaginationRequest, cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>("Cancelled token should throw OperationCanceledException");
    }

    [Fact]
    public async Task GetAllEntitiesPaginatedAsync_Should_Respect_Cancellation_Token()
    {
        // Arrange
        var entityType = "products";
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        var act = async () => 
        {
            await foreach (var page in _paginationApiService.GetAllEntitiesPaginatedAsync(_testStoreConfig, entityType, _testPaginationRequest, cts.Token))
            {
                // Should not reach here due to cancellation
            }
        };
        
        await act.Should().ThrowAsync<OperationCanceledException>("Cancelled token should throw OperationCanceledException");
    }
} 