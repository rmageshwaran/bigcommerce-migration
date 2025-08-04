using BigCommerce.Migration.Core.Interfaces;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Production implementation of IDateTimeProvider using system DateTime
/// Follows Single Responsibility Principle - only handles date/time operations
/// Provides real system time for production use
/// </summary>
public class DateTimeProvider : IDateTimeProvider
{
    /// <summary>
    /// Gets the current UTC date and time from system clock
    /// </summary>
    /// <returns>Current UTC DateTime</returns>
    public DateTime UtcNow => DateTime.UtcNow;

    /// <summary>
    /// Gets the current local date and time from system clock
    /// </summary>
    /// <returns>Current local DateTime</returns>
    public DateTime Now => DateTime.Now;
} 