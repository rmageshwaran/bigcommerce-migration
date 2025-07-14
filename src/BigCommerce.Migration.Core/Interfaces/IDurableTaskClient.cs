using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Wrapper interface for DurableTaskClient to enable mocking in tests
/// </summary>
public interface IDurableTaskClient
{
    /// <summary>
    /// Schedules a new orchestration instance
    /// </summary>
    Task<string> ScheduleNewOrchestrationInstanceAsync(
        string orchestratorName,
        object? input = null,
        StartOrchestrationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of an orchestration instance
    /// </summary>
    Task<OrchestrationMetadata?> GetInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates an orchestration instance
    /// </summary>
    Task TerminateInstanceAsync(
        string instanceId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Raises an event to an orchestration instance
    /// </summary>
    Task RaiseEventAsync(
        string instanceId,
        string eventName,
        object? eventData = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for an orchestration instance to complete
    /// </summary>
    Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of IDurableTaskClient that wraps the actual DurableTaskClient
/// </summary>
public class DurableTaskClientWrapper : IDurableTaskClient
{
    private readonly DurableTaskClient _client;

    /// <summary>
    /// Initializes a new instance of the DurableTaskClientWrapper class
    /// </summary>
    /// <param name="client">The DurableTaskClient to wrap</param>
    public DurableTaskClientWrapper(DurableTaskClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Schedules a new orchestration instance
    /// </summary>
    /// <param name="orchestratorName">Name of the orchestrator</param>
    /// <param name="input">Input data for the orchestration</param>
    /// <param name="options">Start orchestration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The instance ID of the scheduled orchestration</returns>
    public async Task<string> ScheduleNewOrchestrationInstanceAsync(
        string orchestratorName,
        object? input = null,
        StartOrchestrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await _client.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName, input, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the status of an orchestration instance
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The orchestration metadata or null if not found</returns>
    public async Task<OrchestrationMetadata?> GetInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await _client.GetInstanceAsync(instanceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Terminates an orchestration instance
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <param name="reason">The termination reason</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the termination operation</returns>
    public async Task TerminateInstanceAsync(
        string instanceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await _client.TerminateInstanceAsync(instanceId, reason, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Raises an event to an orchestration instance
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <param name="eventName">The event name</param>
    /// <param name="eventData">The event data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task representing the event raising operation</returns>
    public async Task RaiseEventAsync(
        string instanceId,
        string eventName,
        object? eventData = null,
        CancellationToken cancellationToken = default)
    {
        await _client.RaiseEventAsync(instanceId, eventName, eventData, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for an orchestration instance to complete
    /// </summary>
    /// <param name="instanceId">The instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The orchestration metadata when completed</returns>
    public async Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        return await _client.WaitForInstanceCompletionAsync(instanceId, cancellationToken).ConfigureAwait(false);
    }
} 