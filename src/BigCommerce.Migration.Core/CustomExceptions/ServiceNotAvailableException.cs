using System;

namespace BigCommerce.Migration.Core.CustomExceptions;

/// <summary>
/// Exception thrown when a required service is not available or accessible.
/// This exception is typically used when external services, APIs, or dependencies
/// are unavailable, down, or unreachable.
/// </summary>
public class ServiceNotAvailableException : Exception
{
    /// <summary>
    /// Gets the name of the service that is not available.
    /// </summary>
    public string? ServiceName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceNotAvailableException"/> class.
    /// </summary>
    public ServiceNotAvailableException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceNotAvailableException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ServiceNotAvailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceNotAvailableException"/> class
    /// with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ServiceNotAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceNotAvailableException"/> class
    /// with a specified service name, optional message, and optional inner exception.
    /// </summary>
    /// <param name="serviceName">The name of the service that is not available.</param>
    /// <param name="message">Optional custom error message. If null, a default message will be generated.</param>
    /// <param name="innerException">Optional inner exception that caused this exception.</param>
    public ServiceNotAvailableException(string serviceName, string? message = null, Exception? innerException = null)
        : base(message ?? $"The service '{serviceName}' is not available.", innerException)
    {
        ServiceName = serviceName;
    }
}
