namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Service responsible for formatting error messages for different contexts
/// Single Responsibility: Error message formatting only
/// </summary>
public interface IErrorMessageFormatter
{
    /// <summary>
    /// Creates a simple, user-friendly error message for UI display
    /// </summary>
    /// <param name="entityType">Type of entity that failed</param>
    /// <param name="entityId">ID of the entity that failed</param>
    /// <param name="detailedErrorMessage">Detailed error message from API</param>
    /// <returns>Simple error message suitable for UI display</returns>
    string CreateSimpleErrorMessage(string entityType, string entityId, string? detailedErrorMessage);
    
    /// <summary>
    /// Creates a detailed error message for debugging and logging
    /// </summary>
    /// <param name="entityType">Type of entity that failed</param>
    /// <param name="entityId">ID of the entity that failed</param>
    /// <param name="detailedErrorMessage">Detailed error message from API</param>
    /// <returns>Detailed error message for debugging</returns>
    string CreateDetailedErrorMessage(string entityType, string entityId, string? detailedErrorMessage);
} 