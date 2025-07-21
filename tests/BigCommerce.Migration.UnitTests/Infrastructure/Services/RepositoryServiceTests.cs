using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Infrastructure.Services;
using System.Linq;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// TDD Tests for repository service implementations following Interface Segregation Principle (ISP)
/// These tests will fail initially and drive the creation of properly segregated repository services
/// </summary>
public class RepositoryServiceTests
{
    private readonly Mock<ILogger<MigrationRepository>> _migrationLoggerMock;
    private readonly Mock<ILogger<EntityMappingRepository>> _mappingLoggerMock;
    private readonly Mock<ILogger<ApiCallTrackingRepository>> _apiCallLoggerMock;
    private readonly Mock<ILogger<CancellationTokenRepository>> _cancellationLoggerMock;

    public RepositoryServiceTests()
    {
        _migrationLoggerMock = new Mock<ILogger<MigrationRepository>>();
        _mappingLoggerMock = new Mock<ILogger<EntityMappingRepository>>();
        _apiCallLoggerMock = new Mock<ILogger<ApiCallTrackingRepository>>();
        _cancellationLoggerMock = new Mock<ILogger<CancellationTokenRepository>>();
    }

    #region MigrationRepository Tests

    [Fact]
    public void MigrationRepository_Should_Implement_IMigrationRepository()
    {
        // RED: This test will fail until we create MigrationRepository class
        var repositoryType = typeof(MigrationRepository);
        var interfaceType = typeof(IMigrationRepository);

        repositoryType.Should().NotBeNull("MigrationRepository class should exist");
        repositoryType.GetInterfaces().Should().Contain(interfaceType, 
            "MigrationRepository should implement IMigrationRepository");
    }

    [Fact]
    public async Task MigrationRepository_CreateAsync_Should_Generate_Id_And_Timestamps()
    {
        // RED: This test will fail until we implement MigrationRepository.CreateAsync
        var repository = new MigrationRepository(_migrationLoggerMock.Object);
        var entry = new MigrationEntry
        {
            SourceStoreId = "source123",
            DestinationStoreId = "dest456",
            Status = MigrationStatus.Queued
        };

        var result = await repository.CreateAsync(entry);

        result.Should().NotBeNull("CreateAsync should return created entry");
        result.Id.Should().NotBeNullOrEmpty("Id should be generated");
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.SourceStoreId.Should().Be("source123");
        result.DestinationStoreId.Should().Be("dest456");
    }

    [Fact]
    public async Task MigrationRepository_GetAsync_Should_Return_Null_When_Not_Found()
    {
        // RED: This test will fail until we implement MigrationRepository.GetAsync
        var repository = new MigrationRepository(_migrationLoggerMock.Object);
        var nonExistentId = Guid.NewGuid().ToString();

        var result = await repository.GetAsync(nonExistentId);

        result.Should().BeNull("GetAsync should return null for non-existent migration");
    }

    #endregion

    #region EntityMappingRepository Tests

    [Fact]
    public void EntityMappingRepository_Should_Implement_IEntityMappingRepository()
    {
        // RED: This test will fail until we create EntityMappingRepository class
        var repositoryType = typeof(EntityMappingRepository);
        var interfaceType = typeof(IEntityMappingRepository);

        repositoryType.Should().NotBeNull("EntityMappingRepository class should exist");
        repositoryType.GetInterfaces().Should().Contain(interfaceType, 
            "EntityMappingRepository should implement IEntityMappingRepository");
    }

    [Fact]
    public async Task EntityMappingRepository_CreateAsync_Should_Generate_Id_And_Timestamps()
    {
        // RED: This test will fail until we implement EntityMappingRepository.CreateAsync
        var repository = new EntityMappingRepository(_mappingLoggerMock.Object);
        var mapping = new EntityMapping
        {
            MigrationId = Guid.NewGuid().ToString(),
            EntityType = "product",
            SourceId = "source123",
            DestinationId = "dest456"
        };

        var result = await repository.CreateAsync(mapping);

        result.Should().NotBeNull("CreateAsync should return created mapping");
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.EntityType.Should().Be("product");
        result.SourceId.Should().Be("source123");
    }

    [Fact]
    public async Task EntityMappingRepository_GetAsync_Should_Return_Null_When_Not_Found()
    {
        // RED: This test will fail until we implement EntityMappingRepository.GetAsync
        var repository = new EntityMappingRepository(_mappingLoggerMock.Object);
        var migrationId = Guid.NewGuid().ToString();

        var result = await repository.GetAsync(migrationId, "product", "nonexistent");

        result.Should().BeNull("GetAsync should return null for non-existent mapping");
    }

    #endregion

    #region ApiCallTrackingRepository Tests

    [Fact]
    public void ApiCallTrackingRepository_Should_Implement_IApiCallTrackingRepository()
    {
        // RED: This test will fail until we create ApiCallTrackingRepository class
        var repositoryType = typeof(ApiCallTrackingRepository);
        var interfaceType = typeof(IApiCallTrackingRepository);

        repositoryType.Should().NotBeNull("ApiCallTrackingRepository class should exist");
        repositoryType.GetInterfaces().Should().Contain(interfaceType, 
            "ApiCallTrackingRepository should implement IApiCallTrackingRepository");
    }

    [Fact]
    public async Task ApiCallTrackingRepository_CreateAsync_Should_Generate_Id_And_Timestamps()
    {
        // RED: This test will fail until we implement ApiCallTrackingRepository.CreateAsync
        var repository = new ApiCallTrackingRepository(_apiCallLoggerMock.Object);
        var apiCall = new ApiCallTracking
        {
            StoreId = "store123",
            Endpoint = "/v3/catalog/products",
            Method = "GET",
            StatusCode = 200,
            ResponseTimeMs = 250
        };

        var result = await repository.CreateAsync(apiCall);

        result.Should().NotBeNull("CreateAsync should return created API call");
        result.RequestTimestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.StoreId.Should().Be("store123");
        result.Endpoint.Should().Be("/v3/catalog/products");
    }

    [Fact]
    public async Task ApiCallTrackingRepository_GetStatisticsAsync_Should_Return_Default_When_No_Data()
    {
        // RED: This test will fail until we implement ApiCallTrackingRepository.GetStatisticsAsync
        var repository = new ApiCallTrackingRepository(_apiCallLoggerMock.Object);
        var storeId = "store123";
        var timeWindow = TimeSpan.FromHours(1);

        var result = await repository.GetStatisticsAsync(storeId, timeWindow);

        result.Should().NotBeNull("GetStatisticsAsync should return statistics object");
        result.TotalCalls.Should().Be(0, "No API calls should exist initially");
        result.SuccessfulCalls.Should().Be(0, "Successful calls should be 0 when no calls");
    }

    #endregion

    #region CancellationTokenRepository Tests

    [Fact]
    public void CancellationTokenRepository_Should_Implement_ICancellationTokenRepository()
    {
        // RED: This test will fail until we create CancellationTokenRepository class
        var repositoryType = typeof(CancellationTokenRepository);
        var interfaceType = typeof(ICancellationTokenRepository);

        repositoryType.Should().NotBeNull("CancellationTokenRepository class should exist");
        repositoryType.GetInterfaces().Should().Contain(interfaceType, 
            "CancellationTokenRepository should implement ICancellationTokenRepository");
    }

    [Fact]
    public async Task CancellationTokenRepository_CreateAsync_Should_Generate_Id_And_Timestamps()
    {
        // RED: This test will fail until we implement CancellationTokenRepository.CreateAsync
        var repository = new CancellationTokenRepository(_cancellationLoggerMock.Object);
        var migrationId = Guid.NewGuid().ToString();
        var reason = "User requested cancellation";

        var result = await repository.CreateAsync(migrationId, reason);

        result.Should().NotBeNull("CreateAsync should return created token");
        result.RequestedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.MigrationId.Should().Be(migrationId);
        result.Reason.Should().Be(reason);
        result.Status.Should().Be("Active", "Newly created token should be active");
    }

    [Fact]
    public async Task CancellationTokenRepository_GetAsync_Should_Return_Null_When_Not_Found()
    {
        // RED: This test will fail until we implement CancellationTokenRepository.GetAsync
        var repository = new CancellationTokenRepository(_cancellationLoggerMock.Object);
        var nonExistentMigrationId = Guid.NewGuid().ToString();

        var result = await repository.GetAsync(nonExistentMigrationId);

        result.Should().BeNull("GetAsync should return null for non-existent token");
    }

    #endregion

    #region Interface Segregation Validation Tests

    [Fact]
    public void Repository_Services_Should_Have_Single_Responsibility()
    {
        // RED: This test ensures each repository service has a single responsibility
        var migrationRepositoryMethods = typeof(MigrationRepository).GetMethods()
            .Where(m => m.IsPublic && !m.IsSpecialName).Select(m => m.Name).ToHashSet();
        var mappingRepositoryMethods = typeof(EntityMappingRepository).GetMethods()
            .Where(m => m.IsPublic && !m.IsSpecialName).Select(m => m.Name).ToHashSet();

        // Migration repository should only have migration-related methods
        migrationRepositoryMethods.Should().NotContain("CreateMapping", 
            "MigrationRepository should not handle entity mappings");
        migrationRepositoryMethods.Should().NotContain("TrackApiCall", 
            "MigrationRepository should not handle API call tracking");

        // Entity mapping repository should only have mapping-related methods  
        mappingRepositoryMethods.Should().NotContain("CreateMigration", 
            "EntityMappingRepository should not handle migrations");
        mappingRepositoryMethods.Should().NotContain("GetStatistics", 
            "EntityMappingRepository should not handle API statistics");
    }

    [Fact]
    public void Repository_Services_Should_Have_Proper_Dependencies()
    {
        // RED: This test ensures repository services have appropriate dependencies
        var migrationConstructor = typeof(MigrationRepository).GetConstructors().FirstOrDefault();
        var mappingConstructor = typeof(EntityMappingRepository).GetConstructors().FirstOrDefault();

        migrationConstructor.Should().NotBeNull("MigrationRepository should have a constructor");
        mappingConstructor.Should().NotBeNull("EntityMappingRepository should have a constructor");

        // Should accept ILogger for proper logging
        var migrationParams = migrationConstructor!.GetParameters();
        var mappingParams = mappingConstructor!.GetParameters();

        migrationParams.Should().Contain(p => p.ParameterType == typeof(ILogger<MigrationRepository>), 
            "MigrationRepository should accept ILogger");
        mappingParams.Should().Contain(p => p.ParameterType == typeof(ILogger<EntityMappingRepository>), 
            "EntityMappingRepository should accept ILogger");
    }

    #endregion
} 