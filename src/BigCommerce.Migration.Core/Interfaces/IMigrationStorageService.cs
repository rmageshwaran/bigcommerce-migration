using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service interface for Azure Table Storage operations
/// Handles migration configuration, entity mapping, and API call tracking
/// </summary>
public interface IMigrationStorageService
{
    #region Migration Configuration Operations

    /// <summary>
    /// Creates a new migration configuration entry
    /// </summary>
    /// <param name="migrationEntry">Migration entry to create</param>
    /// <returns>Created migration entry</returns>
    Task<MigrationEntry> CreateMigrationAsync(MigrationEntry migrationEntry);

    /// <summary>
    /// Gets a migration configuration by ID
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <returns>Migration entry or null if not found</returns>
    Task<MigrationEntry?> GetMigrationAsync(string migrationId);

    /// <summary>
    /// Updates a migration configuration
    /// </summary>
    /// <param name="migrationEntry">Migration entry to update</param>
    /// <returns>Updated migration entry</returns>
    Task<MigrationEntry> UpdateMigrationAsync(MigrationEntry migrationEntry);

    /// <summary>
    /// Gets a list of migrations with optional filtering
    /// </summary>
    /// <param name="request">Query request with filters</param>
    /// <returns>Paginated list of migrations</returns>
    Task<MigrationListResult> GetMigrationsAsync(MigrationQueryRequest request);

    /// <summary>
    /// Deletes a migration configuration
    /// </summary>
    /// <param name="migrationId">Migration ID to delete</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteMigrationAsync(string migrationId);

    #endregion

    #region Entity Mapping Operations

    /// <summary>
    /// Creates an entity ID mapping
    /// </summary>
    /// <param name="mapping">Entity mapping to create</param>
    /// <returns>Created mapping</returns>
    Task<EntityMapping> CreateEntityMappingAsync(EntityMapping mapping);

    /// <summary>
    /// Gets an entity mapping by source ID
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Entity type (e.g., "product", "category")</param>
    /// <param name="sourceId">Source entity ID</param>
    /// <returns>Entity mapping or null if not found</returns>
    Task<EntityMapping?> GetEntityMappingAsync(string migrationId, string entityType, string sourceId);

    /// <summary>
    /// Gets all entity mappings for a migration
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="entityType">Optional entity type filter</param>
    /// <returns>List of entity mappings</returns>
    Task<List<EntityMapping>> GetEntityMappingsAsync(string migrationId, string? entityType = null);

    /// <summary>
    /// Creates multiple entity mappings in batch
    /// </summary>
    /// <param name="mappings">List of entity mappings to create</param>
    /// <returns>List of created mappings</returns>
    Task<List<EntityMapping>> CreateEntityMappingsBatchAsync(List<EntityMapping> mappings);

    /// <summary>
    /// Updates an entity mapping
    /// </summary>
    /// <param name="mapping">Entity mapping to update</param>
    /// <returns>Updated mapping</returns>
    Task<EntityMapping> UpdateEntityMappingAsync(EntityMapping mapping);

    /// <summary>
    /// Stores multiple entity mappings in batch (alias for CreateEntityMappingsBatchAsync)
    /// </summary>
    /// <param name="mappings">List of entity mappings to store</param>
    /// <returns>List of stored mappings</returns>
    Task<List<EntityMapping>> StoreEntityMappingsAsync(List<EntityMapping> mappings);

    #endregion

    #region API Call Tracking Operations

    /// <summary>
    /// Creates an API call tracking entry
    /// </summary>
    /// <param name="apiCall">API call tracking entry</param>
    /// <returns>Created API call entry</returns>
    Task<ApiCallTracking> CreateApiCallTrackingAsync(ApiCallTracking apiCall);

    /// <summary>
    /// Gets API call statistics for rate limiting
    /// </summary>
    /// <param name="storeId">Store ID</param>
    /// <param name="timeWindow">Time window for statistics</param>
    /// <returns>API call statistics</returns>
    Task<ApiCallStatistics> GetApiCallStatisticsAsync(string storeId, TimeSpan timeWindow);

    /// <summary>
    /// Gets API call history for a migration
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="request">Query request with filters</param>
    /// <returns>Paginated list of API calls</returns>
    Task<ApiCallListResult> GetApiCallHistoryAsync(string migrationId, ApiCallQueryRequest request);

    #endregion

    #region Cancellation Token Operations

    /// <summary>
    /// Creates a cancellation token for a migration
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <param name="reason">Cancellation reason</param>
    /// <returns>Created cancellation token</returns>
    Task<CancellationTokenEntry> CreateCancellationTokenAsync(string migrationId, string reason);

    /// <summary>
    /// Gets a cancellation token for a migration
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <returns>Cancellation token or null if not found</returns>
    Task<CancellationTokenEntry?> GetCancellationTokenAsync(string migrationId);

    /// <summary>
    /// Updates a cancellation token
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to update</param>
    /// <returns>Updated cancellation token</returns>
    Task<CancellationTokenEntry> UpdateCancellationTokenAsync(CancellationTokenEntry cancellationToken);

    /// <summary>
    /// Deletes a cancellation token
    /// </summary>
    /// <param name="migrationId">Migration ID</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteCancellationTokenAsync(string migrationId);

    #endregion
} 