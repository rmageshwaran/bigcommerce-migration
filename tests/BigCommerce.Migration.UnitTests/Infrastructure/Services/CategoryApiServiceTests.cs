using FluentAssertions;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// TDD Tests for CategoryApiService
/// Following Single Responsibility Principle - only category operations
/// </summary>
public class CategoryApiServiceTests
{
    private readonly Mock<ILogger<CategoryApiService>> _mockLogger;
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly CategoryApiService _categoryApiService;
    private readonly StoreConfiguration _testStoreConfig;

    public CategoryApiServiceTests()
    {
        _mockLogger = new Mock<ILogger<CategoryApiService>>();
        _mockHttpClient = new Mock<HttpClient>();
        _categoryApiService = new CategoryApiService(_mockLogger.Object, _mockHttpClient.Object);
        
        _testStoreConfig = new StoreConfiguration
        {
            StoreId = "test-store",
            AccessToken = "test-token",
            BaseUrl = "https://api.bigcommerce.com",
            ChannelId = "1"
        };
    }

    [Fact]
    public void CategoryApiService_Should_Implement_ICategoryApiClient()
    {
        // Arrange & Act
        var service = _categoryApiService;

        // Assert
        service.Should().BeAssignableTo<ICategoryApiClient>("CategoryApiService should implement ICategoryApiClient");
    }

    [Fact]
    public void CategoryApiService_Should_Have_Single_Responsibility()
    {
        // Arrange
        var serviceType = typeof(CategoryApiService);

        // Act & Assert - Check that service implements only the category interface
        serviceType.GetInterfaces().Should().Contain(typeof(ICategoryApiClient), 
            "CategoryApiService should implement ICategoryApiClient interface");
            
        // Verify it has the required category methods
        serviceType.GetMethod("GetCategoryTreesAsync").Should().NotBeNull("Should have GetCategoryTreesAsync method");
        serviceType.GetMethod("GetCategoriesAsync").Should().NotBeNull("Should have GetCategoriesAsync method");
        serviceType.GetMethod("CreateCategoriesAsync").Should().NotBeNull("Should have CreateCategoriesAsync method");
    }

    [Fact]
    public async Task GetCategoryTreesAsync_Should_Handle_Valid_Store_Configuration()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _categoryApiService.GetCategoryTreesAsync(_testStoreConfig, cancellationToken);
        
        // Should not throw exception with valid configuration
        await act.Should().NotThrowAsync("Valid store configuration should not cause exceptions");
    }

    [Fact]
    public async Task GetCategoriesAsync_Should_Handle_Valid_Parameters()
    {
        // Arrange
        var categoryTreeId = "1";
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _categoryApiService.GetCategoriesAsync(_testStoreConfig, categoryTreeId, cancellationToken);
        
        // Should not throw exception with valid parameters
        await act.Should().NotThrowAsync("Valid parameters should not cause exceptions");
    }

    [Fact]
    public async Task CreateCategoriesAsync_Should_Handle_Valid_Categories()
    {
        // Arrange
        var categoryTreeId = "1";
        var categories = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "name", "Test Category" } }
        };
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _categoryApiService.CreateCategoriesAsync(_testStoreConfig, categoryTreeId, categories, cancellationToken);
        
        // Should not throw exception with valid categories
        await act.Should().NotThrowAsync("Valid categories should not cause exceptions");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetCategoriesAsync_Should_Validate_CategoryTreeId(string? invalidTreeId)
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _categoryApiService.GetCategoriesAsync(_testStoreConfig, invalidTreeId!, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentException>("Invalid category tree ID should throw ArgumentException");
    }

    [Fact]
    public async Task CreateCategoriesAsync_Should_Validate_Categories_Not_Null()
    {
        // Arrange
        var categoryTreeId = "1";
        List<Dictionary<string, object>>? nullCategories = null;
        var cancellationToken = CancellationToken.None;

        // Act & Assert
        var act = async () => await _categoryApiService.CreateCategoriesAsync(_testStoreConfig, categoryTreeId, nullCategories!, cancellationToken);
        
        await act.Should().ThrowAsync<ArgumentNullException>("Null categories should throw ArgumentNullException");
    }

    [Fact]
    public async Task CategoryApiService_Should_Respect_Cancellation_Token()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        var act = async () => await _categoryApiService.GetCategoryTreesAsync(_testStoreConfig, cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>("Cancelled token should throw OperationCanceledException");
    }
} 