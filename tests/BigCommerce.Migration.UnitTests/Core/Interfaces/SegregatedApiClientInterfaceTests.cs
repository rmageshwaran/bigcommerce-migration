using FluentAssertions;
using System.Reflection;
using System.Linq;
using BigCommerce.Migration.Core.Interfaces;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Interfaces;

/// <summary>
/// TDD Tests for segregated API client interfaces
/// Following Interface Segregation Principle (ISP)
/// </summary>
public class SegregatedApiClientInterfaceTests
{
    [Fact]
    public void ICategoryApiClient_Should_Only_Have_Category_Methods()
    {
        // RED: This test will fail until we create ICategoryApiClient interface
        var categoryMethods = typeof(ICategoryApiClient).GetMethods();
        
        // Should only have category-related methods
        categoryMethods.All(m => 
            m.Name.Contains("Category") || 
            m.Name.Contains("Tree") ||
            m.Name.StartsWith("Get") ||
            m.Name.StartsWith("Create"))
            .Should().BeTrue("All methods should be category-related");
        
        // Should have exactly 3 category methods
        categoryMethods.Length.Should().Be(3, 
            "Should have GetCategoryTreesAsync, GetCategoriesAsync, CreateCategoriesAsync");
    }

    [Fact]
    public void IProductApiClient_Should_Only_Have_Product_Methods()
    {
        // RED: This test will fail until we create IProductApiClient interface
        var productMethods = typeof(IProductApiClient).GetMethods();
        
        // Should only have product-related methods
        productMethods.All(m => 
            m.Name.Contains("Product") || 
            m.Name.Contains("Variant") || 
            m.Name.Contains("Image") || 
            m.Name.Contains("Modifier") ||
            m.Name.StartsWith("Get") ||
            m.Name.StartsWith("Create"))
            .Should().BeTrue("All methods should be product-related");
        
        // Should have 5 product-related methods
        productMethods.Length.Should().Be(5, 
            "Should have GetProductsAsync, CreateProductsAsync, GetProductVariantsAsync, GetProductImagesAsync, GetProductModifiersAsync");
    }

    [Fact]
    public void IPaginationApiClient_Should_Only_Have_Pagination_Methods()
    {
        // RED: This test will fail until we create IPaginationApiClient interface
        var paginationMethods = typeof(IPaginationApiClient).GetMethods();
        
        // Should only have pagination-related methods
        paginationMethods.All(m => 
            m.Name.Contains("Paginated") || 
            m.Name.Contains("Page") ||
            m.Name.Contains("All") ||
            m.Name.StartsWith("Get"))
            .Should().BeTrue("All methods should be pagination-related");
        
        // Should have 2 pagination methods
        paginationMethods.Length.Should().Be(2, 
            "Should have GetPaginatedEntitiesAsync, GetAllEntitiesPaginatedAsync");
    }

    [Fact]
    public void IApiHealthClient_Should_Only_Have_Health_Methods()
    {
        // RED: This test will fail until we create IApiHealthClient interface
        var healthMethods = typeof(IApiHealthClient).GetMethods();
        
        // Should only have health/utility methods
        healthMethods.All(m => 
            m.Name.Contains("Health") || 
            m.Name.Contains("Version") ||
            m.Name.Contains("Detect") ||
            m.Name.StartsWith("Is"))
            .Should().BeTrue("All methods should be health/utility-related");
        
        // Should have 2 health methods
        healthMethods.Length.Should().Be(2, 
            "Should have IsHealthyAsync, DetectApiVersionAsync");
    }

    [Fact]
    public void IBigCommerceApiClient_Should_Inherit_All_Segregated_Interfaces()
    {
        // RED: This test will fail until we update IBigCommerceApiClient to inherit segregated interfaces
        var compositeInterface = typeof(IBigCommerceApiClient);
        
        // Should inherit from all segregated interfaces
        typeof(ICategoryApiClient).IsAssignableFrom(compositeInterface)
            .Should().BeTrue("IBigCommerceApiClient should inherit ICategoryApiClient");
        typeof(IProductApiClient).IsAssignableFrom(compositeInterface)
            .Should().BeTrue("IBigCommerceApiClient should inherit IProductApiClient");
        typeof(IPaginationApiClient).IsAssignableFrom(compositeInterface)
            .Should().BeTrue("IBigCommerceApiClient should inherit IPaginationApiClient");
        typeof(IApiHealthClient).IsAssignableFrom(compositeInterface)
            .Should().BeTrue("IBigCommerceApiClient should inherit IApiHealthClient");
    }

    [Fact]
    public void Segregated_Interfaces_Should_Follow_Single_Responsibility()
    {
        // RED: This test validates that each interface has a single, well-defined responsibility
        
        // Category interface should only deal with categories
        var categoryInterface = typeof(ICategoryApiClient);
        categoryInterface.Name.Should().Contain("Category", 
            "Category interface name should reflect its responsibility");
        
        // Product interface should only deal with products
        var productInterface = typeof(IProductApiClient);
        productInterface.Name.Should().Contain("Product", 
            "Product interface name should reflect its responsibility");
        
        // Pagination interface should only deal with pagination
        var paginationInterface = typeof(IPaginationApiClient);
        paginationInterface.Name.Should().Contain("Pagination", 
            "Pagination interface name should reflect its responsibility");
        
        // Health interface should only deal with API health
        var healthInterface = typeof(IApiHealthClient);
        healthInterface.Name.Should().Contain("Health", 
            "Health interface name should reflect its responsibility");
    }

    [Fact]
    public void Segregated_Interfaces_Should_Not_Have_Overlapping_Responsibilities()
    {
        // RED: This test ensures no method overlap between interfaces
        var categoryMethods = typeof(ICategoryApiClient).GetMethods().Select(m => m.Name).ToHashSet();
        var productMethods = typeof(IProductApiClient).GetMethods().Select(m => m.Name).ToHashSet();
        var paginationMethods = typeof(IPaginationApiClient).GetMethods().Select(m => m.Name).ToHashSet();
        var healthMethods = typeof(IApiHealthClient).GetMethods().Select(m => m.Name).ToHashSet();
        
        // No overlap between category and product methods
        categoryMethods.Intersect(productMethods).Any()
            .Should().BeFalse("Category and Product interfaces should not share methods");
        
        // No overlap between category and pagination methods
        categoryMethods.Intersect(paginationMethods).Any()
            .Should().BeFalse("Category and Pagination interfaces should not share methods");
        
        // No overlap between product and health methods
        productMethods.Intersect(healthMethods).Any()
            .Should().BeFalse("Product and Health interfaces should not share methods");
    }
} 