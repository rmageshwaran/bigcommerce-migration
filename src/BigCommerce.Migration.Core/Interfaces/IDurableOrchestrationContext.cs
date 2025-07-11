namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for durable orchestration context
/// This matches the Azure Durable Functions SDK interface patterns
/// </summary>
public interface IDurableOrchestrationContext
{
    /// <summary>
    /// Gets the input for the orchestrator
    /// </summary>
    /// <typeparam name="T">Input type</typeparam>
    /// <returns>Deserialized input</returns>
    T GetInput<T>();

    /// <summary>
    /// Gets the current UTC date time (deterministic)
    /// </summary>
    DateTime CurrentUtcDateTime { get; }

    /// <summary>
    /// Generates a new GUID (deterministic)
    /// </summary>
    /// <returns>New GUID</returns>
    Guid NewGuid();

    /// <summary>
    /// Sets custom status for the orchestration
    /// </summary>
    /// <param name="status">Status message</param>
    void SetCustomStatus(string status);

    /// <summary>
    /// Calls an activity function
    /// </summary>
    /// <param name="name">Activity name</param>
    /// <param name="input">Activity input</param>
    /// <returns>Task representing the activity call</returns>
    Task CallActivityAsync(string name, object input);

    /// <summary>
    /// Calls an activity function with return value
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="name">Activity name</param>
    /// <param name="input">Activity input</param>
    /// <returns>Task with activity result</returns>
    Task<T> CallActivityAsync<T>(string name, object input);

    /// <summary>
    /// Calls a sub-orchestrator
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="name">Sub-orchestrator name</param>
    /// <param name="input">Sub-orchestrator input</param>
    /// <returns>Task with sub-orchestrator result</returns>
    Task<T> CallSubOrchestratorAsync<T>(string name, object input);

    /// <summary>
    /// Creates a durable timer (deterministic delay)
    /// </summary>
    /// <param name="fireAt">When to fire the timer</param>
    /// <returns>Timer task</returns>
    Task CreateTimer(DateTime fireAt);
}

/// <summary>
/// Validation result for migration operations
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether the validation passed
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Error message if validation failed
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Additional validation details
    /// </summary>
    public List<string> Details { get; set; } = new();

    /// <summary>
    /// Creates a successful validation result
    /// </summary>
    /// <returns>Successful validation result</returns>
    public static ValidationResult Success()
    {
        return new ValidationResult { IsValid = true };
    }

    /// <summary>
    /// Creates a failed validation result
    /// </summary>
    /// <param name="errorMessage">Error message</param>
    /// <returns>Failed validation result</returns>
    public static ValidationResult Failed(string errorMessage)
    {
        return new ValidationResult 
        { 
            IsValid = false, 
            ErrorMessage = errorMessage 
        };
    }
} 