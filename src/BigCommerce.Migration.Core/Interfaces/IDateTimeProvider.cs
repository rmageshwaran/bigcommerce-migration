namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for providing current date and time functionality
/// Follows Dependency Inversion Principle - enables testable and mockable date/time operations
/// Used by services that need current timestamp for calculations and logging
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>
    /// Gets the current UTC date and time
    /// </summary>
    /// <returns>Current UTC DateTime</returns>
    DateTime UtcNow { get; }

    /// <summary>
    /// Gets the current local date and time
    /// </summary>
    /// <returns>Current local DateTime</returns>
    DateTime Now { get; }
} 