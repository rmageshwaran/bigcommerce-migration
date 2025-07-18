using FluentAssertions;
using System.Reflection;
using System.Linq;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Interfaces;

/// <summary>
/// TDD Tests for repository interfaces following Interface Segregation Principle (ISP)
/// These tests will fail initially and drive the creation of properly segregated repository interfaces
/// </summary>
public class RepositoryInterfaceTests
{
    [Fact]
    public void IMigrationRepository_Should_Only_Handle_Migration_Operations()
    {
        // RED: This test will fail until we create IMigrationRepository interface
        var migrationMethods = typeof(IMigrationRepository).GetMethods();
        
        // Should only have migration-related methods
        migrationMethods.All(m => 
            m.Name.Contains("Migration") || 
            m.Name.Contains("Create") ||
            m.Name.Contains("Get") ||
            m.Name.Contains("Update") ||
            m.Name.Contains("Delete") ||
            m.Name.Contains("Async"))
            .Should().BeTrue("All methods should be migration-related");
        
        // Should have exactly 5 migration methods
        migrationMethods.Length.Should().Be(5, 
            "Should have CreateAsync, GetAsync, UpdateAsync, GetMigrationsAsync, DeleteAsync");
        
        // Verify specific method signatures exist
        var methodNames = migrationMethods.Select(m => m.Name).ToList();
        methodNames.Should().Contain("CreateAsync");
        methodNames.Should().Contain("GetAsync");
        methodNames.Should().Contain("UpdateAsync");
        methodNames.Should().Contain("GetMigrationsAsync");
        methodNames.Should().Contain("DeleteAsync");
    }

    [Fact]
    public void IEntityMappingRepository_Should_Only_Handle_Entity_Mapping_Operations()
    {
        // RED: This test will fail until we create IEntityMappingRepository interface
        var mappingMethods = typeof(IEntityMappingRepository).GetMethods();
        
        // Should only have entity mapping-related methods
        mappingMethods.All(m => 
            m.Name.Contains("Mapping") || 
            m.Name.Contains("Entity") ||
            m.Name.Contains("Create") ||
            m.Name.Contains("Get") ||
            m.Name.Contains("Update") ||
            m.Name.Contains("Batch") ||
            m.Name.Contains("Async"))
            .Should().BeTrue("All methods should be entity mapping-related");
        
        // Should have exactly 5 entity mapping methods
        mappingMethods.Length.Should().Be(5, 
            "Should have CreateAsync, GetAsync, GetAllAsync, CreateBatchAsync, UpdateAsync");
        
        // Verify specific method signatures exist
        var methodNames = mappingMethods.Select(m => m.Name).ToList();
        methodNames.Should().Contain("CreateAsync");
        methodNames.Should().Contain("GetAsync");
        methodNames.Should().Contain("GetAllAsync");
        methodNames.Should().Contain("CreateBatchAsync");
        methodNames.Should().Contain("UpdateAsync");
    }

    [Fact]
    public void IApiCallTrackingRepository_Should_Only_Handle_Api_Call_Tracking_Operations()
    {
        // RED: This test will fail until we create IApiCallTrackingRepository interface
        var apiCallMethods = typeof(IApiCallTrackingRepository).GetMethods();
        
        // Should only have API call tracking-related methods
        apiCallMethods.All(m => 
            m.Name.Contains("ApiCall") || 
            m.Name.Contains("Statistics") ||
            m.Name.Contains("History") ||
            m.Name.Contains("Create") ||
            m.Name.Contains("Get") ||
            m.Name.Contains("Async"))
            .Should().BeTrue("All methods should be API call tracking-related");
        
        // Should have exactly 3 API call tracking methods
        apiCallMethods.Length.Should().Be(3, 
            "Should have CreateAsync, GetStatisticsAsync, GetHistoryAsync");
        
        // Verify specific method signatures exist
        var methodNames = apiCallMethods.Select(m => m.Name).ToList();
        methodNames.Should().Contain("CreateAsync");
        methodNames.Should().Contain("GetStatisticsAsync");
        methodNames.Should().Contain("GetHistoryAsync");
    }

    [Fact]
    public void ICancellationTokenRepository_Should_Only_Handle_Cancellation_Token_Operations()
    {
        // RED: This test will fail until we create ICancellationTokenRepository interface
        var cancellationMethods = typeof(ICancellationTokenRepository).GetMethods();
        
        // Should only have cancellation token-related methods
        cancellationMethods.All(m => 
            m.Name.Contains("Cancellation") || 
            m.Name.Contains("Token") ||
            m.Name.Contains("Create") ||
            m.Name.Contains("Get") ||
            m.Name.Contains("Update") ||
            m.Name.Contains("Delete") ||
            m.Name.Contains("Async"))
            .Should().BeTrue("All methods should be cancellation token-related");
        
        // Should have exactly 4 cancellation token methods
        cancellationMethods.Length.Should().Be(4, 
            "Should have CreateAsync, GetAsync, UpdateAsync, DeleteAsync");
        
        // Verify specific method signatures exist
        var methodNames = cancellationMethods.Select(m => m.Name).ToList();
        methodNames.Should().Contain("CreateAsync");
        methodNames.Should().Contain("GetAsync");
        methodNames.Should().Contain("UpdateAsync");
        methodNames.Should().Contain("DeleteAsync");
    }

    [Fact]
    public void Repository_Interfaces_Should_Follow_Single_Responsibility()
    {
        // RED: This test validates that each repository interface has a single, well-defined responsibility
        
        // Migration repository should only deal with migrations
        var migrationInterface = typeof(IMigrationRepository);
        migrationInterface.Name.Should().Contain("Migration", 
            "Migration repository interface name should reflect its responsibility");
        
        // Entity mapping repository should only deal with entity mappings
        var mappingInterface = typeof(IEntityMappingRepository);
        mappingInterface.Name.Should().Contain("EntityMapping", 
            "Entity mapping repository interface name should reflect its responsibility");
        
        // API call tracking repository should only deal with API call tracking
        var apiCallInterface = typeof(IApiCallTrackingRepository);
        apiCallInterface.Name.Should().Contain("ApiCallTracking", 
            "API call tracking repository interface name should reflect its responsibility");
        
        // Cancellation token repository should only deal with cancellation tokens
        var cancellationInterface = typeof(ICancellationTokenRepository);
        cancellationInterface.Name.Should().Contain("CancellationToken", 
            "Cancellation token repository interface name should reflect its responsibility");
    }

    [Fact]
    public void Repository_Interfaces_Should_Not_Have_Overlapping_Responsibilities()
    {
        // RED: This test ensures no business context overlap between repository interfaces
        // Method names can overlap (CreateAsync, GetAsync, etc.) but the parameters should be different
        
        // Check that each repository handles different entity types
        var migrationRepository = typeof(IMigrationRepository);
        var mappingRepository = typeof(IEntityMappingRepository);
        var apiCallRepository = typeof(IApiCallTrackingRepository);
        var cancellationRepository = typeof(ICancellationTokenRepository);
        
        // Migration repository should handle MigrationEntry
        var migrationCreateMethod = migrationRepository.GetMethod("CreateAsync");
        migrationCreateMethod?.GetParameters()[0].ParameterType.Should().Be(typeof(MigrationEntry),
            "Migration repository should handle MigrationEntry");
        
        // Entity mapping repository should handle EntityMapping
        var mappingCreateMethod = mappingRepository.GetMethod("CreateAsync");
        mappingCreateMethod?.GetParameters()[0].ParameterType.Should().Be(typeof(EntityMapping),
            "Entity mapping repository should handle EntityMapping");
        
        // API call repository should handle ApiCallTracking
        var apiCallCreateMethod = apiCallRepository.GetMethod("CreateAsync");
        apiCallCreateMethod?.GetParameters()[0].ParameterType.Should().Be(typeof(ApiCallTracking),
            "API call repository should handle ApiCallTracking");
        
        // Cancellation repository should handle different parameter types (migrationId, reason)
        var cancellationCreateMethod = cancellationRepository.GetMethod("CreateAsync");
        var cancellationParams = cancellationCreateMethod?.GetParameters();
        cancellationParams?[0].ParameterType.Should().Be(typeof(string), "Cancellation CreateAsync first param should be string (migrationId)");
        cancellationParams?[1].ParameterType.Should().Be(typeof(string), "Cancellation CreateAsync second param should be string (reason)");
        
        // Verify no repository handles the same entity types
        var migrationEntityType = typeof(MigrationEntry);
        var mappingEntityType = typeof(EntityMapping);
        var apiCallEntityType = typeof(ApiCallTracking);
        var cancellationEntityType = typeof(CancellationTokenEntry);
        
        // All should be different entity types
        migrationEntityType.Should().NotBe(mappingEntityType, "Migration and EntityMapping should be different types");
        migrationEntityType.Should().NotBe(apiCallEntityType, "Migration and ApiCallTracking should be different types");
        mappingEntityType.Should().NotBe(cancellationEntityType, "EntityMapping and CancellationTokenEntry should be different types");
    }

    [Fact]
    public void Repository_Interfaces_Should_Have_Correct_Return_Types()
    {
        // RED: This test validates that repository methods return appropriate types
        
        // Migration repository methods should return migration-related types
        var migrationCreateMethod = typeof(IMigrationRepository).GetMethod("CreateAsync");
        migrationCreateMethod?.ReturnType.Should().Be(typeof(Task<MigrationEntry>), 
            "CreateAsync should return Task<MigrationEntry>");
        
        var migrationGetMethod = typeof(IMigrationRepository).GetMethod("GetAsync");
        migrationGetMethod?.ReturnType.Should().Be(typeof(Task<MigrationEntry?>), 
            "GetAsync should return Task<MigrationEntry?>");
        
        // Entity mapping repository methods should return mapping-related types
        var mappingCreateMethod = typeof(IEntityMappingRepository).GetMethod("CreateAsync");
        mappingCreateMethod?.ReturnType.Should().Be(typeof(Task<EntityMapping>), 
            "EntityMapping CreateAsync should return Task<EntityMapping>");
        
        var mappingGetAllMethod = typeof(IEntityMappingRepository).GetMethod("GetAllAsync");
        mappingGetAllMethod?.ReturnType.Should().Be(typeof(Task<List<EntityMapping>>), 
            "GetAllAsync should return Task<List<EntityMapping>>");
        
        // API call tracking repository methods should return tracking-related types
        var apiCallCreateMethod = typeof(IApiCallTrackingRepository).GetMethod("CreateAsync");
        apiCallCreateMethod?.ReturnType.Should().Be(typeof(Task<ApiCallTracking>), 
            "ApiCallTracking CreateAsync should return Task<ApiCallTracking>");
        
        var apiCallStatsMethod = typeof(IApiCallTrackingRepository).GetMethod("GetStatisticsAsync");
        apiCallStatsMethod?.ReturnType.Should().Be(typeof(Task<ApiCallStatistics>), 
            "GetStatisticsAsync should return Task<ApiCallStatistics>");
        
        // Cancellation token repository methods should return cancellation-related types
        var cancellationCreateMethod = typeof(ICancellationTokenRepository).GetMethod("CreateAsync");
        cancellationCreateMethod?.ReturnType.Should().Be(typeof(Task<CancellationTokenEntry>), 
            "CancellationToken CreateAsync should return Task<CancellationTokenEntry>");
    }

    [Fact]
    public void Repository_Interfaces_Should_Have_Proper_Method_Parameters()
    {
        // RED: This test validates that repository methods have appropriate parameters
        
        // Migration repository CreateAsync should take MigrationEntry
        var migrationCreateMethod = typeof(IMigrationRepository).GetMethod("CreateAsync");
        var migrationCreateParams = migrationCreateMethod?.GetParameters();
        migrationCreateParams?.Length.Should().Be(1, "CreateAsync should have one parameter");
        migrationCreateParams?[0].ParameterType.Should().Be(typeof(MigrationEntry), 
            "CreateAsync parameter should be MigrationEntry");
        
        // Entity mapping repository GetAsync should take migrationId, entityType, sourceId
        var mappingGetMethod = typeof(IEntityMappingRepository).GetMethod("GetAsync");
        var mappingGetParams = mappingGetMethod?.GetParameters();
        mappingGetParams?.Length.Should().Be(3, "EntityMapping GetAsync should have three parameters");
        mappingGetParams?[0].ParameterType.Should().Be(typeof(string), "First parameter should be string (migrationId)");
        mappingGetParams?[1].ParameterType.Should().Be(typeof(string), "Second parameter should be string (entityType)");
        mappingGetParams?[2].ParameterType.Should().Be(typeof(string), "Third parameter should be string (sourceId)");
        
        // Cancellation token repository CreateAsync should take migrationId and reason
        var cancellationCreateMethod = typeof(ICancellationTokenRepository).GetMethod("CreateAsync");
        var cancellationCreateParams = cancellationCreateMethod?.GetParameters();
        cancellationCreateParams?.Length.Should().Be(2, "CancellationToken CreateAsync should have two parameters");
        cancellationCreateParams?[0].ParameterType.Should().Be(typeof(string), "First parameter should be string (migrationId)");
        cancellationCreateParams?[1].ParameterType.Should().Be(typeof(string), "Second parameter should be string (reason)");
    }

    [Fact]
    public void IMigrationStorageService_Should_Compose_All_Repository_Interfaces()
    {
        // RED: This test validates that IMigrationStorageService can use repository composition
        // This will help us validate the segregation is working correctly
        
        // IMigrationStorageService should be able to use all repositories
        var storageServiceInterface = typeof(IMigrationStorageService);
        storageServiceInterface.Should().NotBeNull("IMigrationStorageService should exist");
        
        // After segregation, IMigrationStorageService will use composition instead of implementing all methods directly
        // This test ensures we can inject all repository dependencies
        var migrationRepo = typeof(IMigrationRepository);
        var mappingRepo = typeof(IEntityMappingRepository);
        var apiCallRepo = typeof(IApiCallTrackingRepository);
        var cancellationRepo = typeof(ICancellationTokenRepository);
        
        // All repository interfaces should exist for composition
        migrationRepo.Should().NotBeNull("IMigrationRepository should exist for composition");
        mappingRepo.Should().NotBeNull("IEntityMappingRepository should exist for composition");
        apiCallRepo.Should().NotBeNull("IApiCallTrackingRepository should exist for composition");
        cancellationRepo.Should().NotBeNull("ICancellationTokenRepository should exist for composition");
    }
} 