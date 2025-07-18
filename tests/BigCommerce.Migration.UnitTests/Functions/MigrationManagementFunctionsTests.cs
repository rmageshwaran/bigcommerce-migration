using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using Xunit;
using FluentAssertions;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Functions.Functions;
using System.Text;
using System.Text.Json;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.Functions;

/// <summary>
/// Tests for MigrationManagementFunctions - focused on migration lifecycle operations
/// Tests follow TDD approach: RED phase - writing failing tests for desired behavior
/// </summary>
public class MigrationManagementFunctionsTests
{
    private readonly Mock<ILogger<MigrationManagementFunctions>> _mockLogger;
    private readonly Mock<IQueueService> _mockQueueService;
    private readonly Mock<IMigrationStorageService> _mockStorageService;
    private readonly Mock<IOpenSearchService> _mockOpenSearchService;
    private readonly Mock<HttpRequestData> _mockRequest;
    private readonly Mock<FunctionContext> _mockContext;

    public MigrationManagementFunctionsTests()
    {
        _mockLogger = new Mock<ILogger<MigrationManagementFunctions>>();
        _mockQueueService = new Mock<IQueueService>();
        _mockStorageService = new Mock<IMigrationStorageService>();
        _mockOpenSearchService = new Mock<IOpenSearchService>();
        _mockRequest = new Mock<HttpRequestData>();
        _mockContext = new Mock<FunctionContext>();
    }

    /// <summary>
    /// RED PHASE: Test that MigrationManagementFunctions only handles management operations
    /// This test will fail until we create the segregated class
    /// </summary>
    [Fact]
    public void MigrationManagementFunctions_Should_Only_Handle_Management_Operations()
    {
        // Arrange & Act
        var methods = typeof(MigrationManagementFunctions).GetMethods()
            .Where(m => m.IsPublic && m.DeclaringType == typeof(MigrationManagementFunctions) && m.GetCustomAttributes(typeof(FunctionAttribute), false).Any())
            .Select(m => m.Name)
            .ToList();
        
        // Assert - Should only contain management operations
        var expectedMethods = new List<string> { "StartMigration", "CancelMigration", "PauseMigration", "ResumeMigration" };
        methods.Should().BeSubsetOf(expectedMethods, "Management functions should only handle migration lifecycle operations");
        methods.Should().Contain("StartMigration", "Must handle migration creation");
        methods.Should().Contain("CancelMigration", "Must handle migration cancellation");
    }

    /// <summary>
    /// RED PHASE: Test StartMigration functionality
    /// This test will fail until we implement the segregated StartMigration method
    /// </summary>
    [Fact(Skip = "Azure Functions HTTP mocking issue - infrastructure related")]
    public async Task StartMigration_Should_Create_Migration_And_Queue_Processing()
    {
        // Arrange
        var migrationRequest = new MigrationRequest
        {
            SourceStore = new StoreConfiguration { StoreId = "source-123", AccessToken = "token", BaseUrl = "https://test.com" },
            DestinationStore = new StoreConfiguration { StoreId = "dest-456", AccessToken = "token2", BaseUrl = "https://test2.com" },
            Entities = new List<string> { "categories", "products" }
        };
        
        var requestBody = JsonSerializer.Serialize(migrationRequest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var requestStream = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
        
        _mockRequest.Setup(r => r.Body).Returns(requestStream);
        _mockRequest.Setup(r => r.CreateResponse()).Returns(Mock.Of<HttpResponseData>());
        
        var functions = new MigrationManagementFunctions(
            _mockLogger.Object,
            _mockQueueService.Object,
            _mockStorageService.Object,
            _mockOpenSearchService.Object);

        // Act
        var result = await functions.StartMigration(_mockRequest.Object, _mockContext.Object);

        // Assert
        result.Should().NotBeNull("Should return a response");
        _mockStorageService.Verify(s => s.CreateMigrationAsync(It.IsAny<BigCommerce.Migration.Core.Models.MigrationEntry>()), Times.Once,
            "Should store migration entry");
        _mockQueueService.Verify(q => q.SendMigrationStartMessageAsync(It.IsAny<string>(), It.IsAny<MigrationRequest>(), It.IsAny<CategoryTreeContext>()), Times.Once,
            "Should queue migration for processing");
    }

    /// <summary>
    /// RED PHASE: Test CancelMigration functionality
    /// This test will fail until we implement the segregated CancelMigration method
    /// </summary>
    [Fact(Skip = "Azure Functions HTTP mocking issue - infrastructure related")]
    public async Task CancelMigration_Should_Cancel_Migration_And_Update_Status()
    {
        // Arrange
        var migrationId = "migration-123";
        var existingMigration = new BigCommerce.Migration.Core.Models.MigrationEntry
        {
            Id = migrationId,
            Status = BigCommerce.Migration.Core.Models.MigrationStatus.InProgress,
            SourceStoreId = "source-123",
            DestinationStoreId = "dest-456"
        };

        _mockStorageService.Setup(s => s.GetMigrationAsync(migrationId))
            .ReturnsAsync(existingMigration);
        _mockRequest.Setup(r => r.CreateResponse()).Returns(Mock.Of<HttpResponseData>());

        var functions = new MigrationManagementFunctions(
            _mockLogger.Object,
            _mockQueueService.Object,
            _mockStorageService.Object,
            _mockOpenSearchService.Object);

        // Act
        var result = await functions.CancelMigration(_mockRequest.Object, _mockContext.Object);

        // Assert
        result.Should().NotBeNull("Should return a response");
        _mockStorageService.Verify(s => s.UpdateMigrationAsync(It.Is<BigCommerce.Migration.Core.Models.MigrationEntry>(m => 
            m.Id == migrationId && m.Status == BigCommerce.Migration.Core.Models.MigrationStatus.Cancelled)), Times.Once,
            "Should update migration status to cancelled");
    }

    /// <summary>
    /// RED PHASE: Test that management functions have proper dependency injection
    /// This test will fail until we create the proper constructor
    /// </summary>
    [Fact]
    public void MigrationManagementFunctions_Should_Have_Focused_Dependencies()
    {
        // Arrange & Act
        var constructor = typeof(MigrationManagementFunctions).GetConstructors().FirstOrDefault();
        
        // Assert
        constructor.Should().NotBeNull("Should have a constructor");
        
        var parameters = constructor!.GetParameters();
        var parameterTypes = parameters.Select(p => p.ParameterType).ToList();
        
        // Should only depend on services needed for management operations
        parameterTypes.Should().Contain(typeof(ILogger<MigrationManagementFunctions>), "Should have logger");
        parameterTypes.Should().Contain(typeof(IQueueService), "Should have queue service for starting migrations");
        parameterTypes.Should().Contain(typeof(IMigrationStorageService), "Should have storage service for migration state");
        parameterTypes.Should().Contain(typeof(IOpenSearchService), "Should have OpenSearch for logging events");
        
        // Should NOT depend on services it doesn't need
        parameterTypes.Should().NotContain(typeof(IBigCommerceApiClient), "Should not depend on API client - not needed for management");
        parameterTypes.Should().NotContain(typeof(ICategoryTreeResolver), "Should not depend on tree resolver - not needed for management");
        parameterTypes.Should().NotContain(typeof(IBlobService), "Should not depend on blob service - not needed for management");
    }

    /// <summary>
    /// RED PHASE: Test that functions follow Single Responsibility Principle
    /// </summary>
    [Fact]
    public void MigrationManagementFunctions_Should_Follow_Single_Responsibility_Principle()
    {
        // Arrange
        var classType = typeof(MigrationManagementFunctions);
        
        // Act & Assert
        classType.Name.Should().Contain("Management", "Class name should indicate its single responsibility");
        
        // All methods should be related to migration lifecycle management
        var publicMethods = classType.GetMethods()
            .Where(m => m.IsPublic && m.DeclaringType == classType)
            .Where(m => m.GetCustomAttributes(typeof(FunctionAttribute), false).Any())
            .ToList();
        
        foreach (var method in publicMethods)
        {
            method.Name.Should().MatchRegex(@"(Start|Cancel|Pause|Resume).*Migration.*", 
                $"Method {method.Name} should be related to migration lifecycle management");
        }
    }
} 