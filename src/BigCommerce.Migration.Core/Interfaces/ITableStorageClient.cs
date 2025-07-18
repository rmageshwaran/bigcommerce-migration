namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Abstraction for Table Storage operations to enforce Dependency Inversion Principle
/// Provides testable interface without direct dependency on any storage implementation
/// </summary>
public interface ITableStorageClient
{
    /// <summary>
    /// Creates a table if it does not exist
    /// </summary>
    /// <param name="tableName">Name of the table to create</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation with success indicator</returns>
    Task<bool> CreateTableIfNotExistsAsync(string tableName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a table client for the specified table
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <returns>Table client abstraction</returns>
    ITableClient GetTableClient(string tableName);
}

/// <summary>
/// Abstraction for Table Client operations to enforce Dependency Inversion Principle
/// </summary>
public interface ITableClient
{
    /// <summary>
    /// Creates a table if it does not exist
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation with success indicator</returns>
    Task<bool> CreateIfNotExistsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds an entity to the table
    /// </summary>
    /// <param name="entity">Entity to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation with success indicator</returns>
    Task<bool> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class, ITableEntity;
    
    /// <summary>
    /// Updates an entity in the table
    /// </summary>
    /// <param name="entity">Entity to update</param>
    /// <param name="mode">Update mode (Replace or Merge)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation with success indicator</returns>
    Task<bool> UpdateEntityAsync<T>(T entity, string mode, CancellationToken cancellationToken = default) where T : class, ITableEntity;
    
    /// <summary>
    /// Upserts an entity in the table
    /// </summary>
    /// <param name="entity">Entity to upsert</param>
    /// <param name="mode">Update mode (Replace or Merge)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation with success indicator</returns>
    Task<bool> UpsertEntityAsync<T>(T entity, string mode, CancellationToken cancellationToken = default) where T : class, ITableEntity;
    
    /// <summary>
    /// Gets an entity from the table
    /// </summary>
    /// <param name="partitionKey">Partition key</param>
    /// <param name="rowKey">Row key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Entity or null if not found</returns>
    Task<T?> GetEntityAsync<T>(string partitionKey, string rowKey, CancellationToken cancellationToken = default) where T : class, ITableEntity;
    
    /// <summary>
    /// Queries entities from the table
    /// </summary>
    /// <param name="filter">OData filter expression</param>
    /// <param name="maxPerPage">Maximum number of entities per page</param>
    /// <param name="select">Properties to select</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of entities</returns>
    Task<IEnumerable<T>> QueryAsync<T>(string? filter = null, int? maxPerPage = null, IEnumerable<string>? select = null, CancellationToken cancellationToken = default) where T : class, ITableEntity;
    
    /// <summary>
    /// Deletes an entity from the table
    /// </summary>
    /// <param name="partitionKey">Partition key</param>
    /// <param name="rowKey">Row key</param>
    /// <param name="ifMatch">ETag for conditional deletion</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation with success indicator</returns>
    Task<bool> DeleteEntityAsync(string partitionKey, string rowKey, string? ifMatch = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Generic abstraction for table entities without Azure dependencies
/// </summary>
public interface ITableEntity
{
    /// <summary>
    /// Gets or sets the partition key
    /// </summary>
    string PartitionKey { get; set; }
    
    /// <summary>
    /// Gets or sets the row key
    /// </summary>
    string RowKey { get; set; }
    
    /// <summary>
    /// Gets or sets the timestamp
    /// </summary>
    DateTimeOffset? Timestamp { get; set; }
    
    /// <summary>
    /// Gets or sets the ETag
    /// </summary>
    string? ETag { get; set; }
} 