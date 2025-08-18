using Azure.Data.Tables;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Centralized service for initializing Azure Tables with Azurite compatibility
/// </summary>
public interface IAzureTableInitializationService
{
    /// <summary>
    /// Gets a table client, creating the table if it doesn't exist
    /// </summary>
    /// <param name="tableName">Name of the table to initialize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Initialized table client</returns>
    Task<TableClient> GetTableClientAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures multiple tables exist, creating them if necessary
    /// </summary>
    /// <param name="tableNames">Names of tables to initialize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary of table name to table client</returns>
    Task<Dictionary<string, TableClient>> GetTableClientsAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a table exists and is accessible
    /// </summary>
    /// <param name="tableName">Name of the table to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if table exists and is accessible</returns>
    Task<bool> IsTableHealthyAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets health status of all managed tables
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary of table name to health status</returns>
    Task<Dictionary<string, bool>> GetAllTablesHealthAsync(CancellationToken cancellationToken = default);
}