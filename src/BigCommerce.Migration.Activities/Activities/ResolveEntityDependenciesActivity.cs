using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity function for resolving entity dependencies and determining phased migration sequences.
/// Uses EntityDependencyResolver to automatically identify dependencies and trigger phased processing.
/// </summary>
public class ResolveEntityDependenciesActivity
{
    private readonly ILogger<ResolveEntityDependenciesActivity> _logger;
    private readonly IEntityDependencyResolver _entityDependencyResolver;

    public ResolveEntityDependenciesActivity(
        ILogger<ResolveEntityDependenciesActivity> logger,
        IEntityDependencyResolver entityDependencyResolver)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entityDependencyResolver = entityDependencyResolver ?? throw new ArgumentNullException(nameof(entityDependencyResolver));
    }

    /// <summary>
    /// Resolves the complete entity dependency sequence for the requested entities.
    /// When "products" is requested, automatically includes all 6 product phases.
    /// </summary>
    /// <param name="request">List of requested entity types</param>
    /// <returns>Complete sequence of entity types to migrate in dependency order</returns>
    [Function("ResolveEntityDependencies")]
    public async Task<List<string>> ResolveEntityDependencies([ActivityTrigger] List<string> requestedEntities)
    {
        _logger.LogInformation("🧠 [RESOLVE-DEPENDENCIES] Resolving entity dependencies for: [{Entities}]", 
            string.Join(", ", requestedEntities));

        try
        {
            // ✨ STEP 1: First resolve dependencies normally to get the complete sequence
            var resolvedSequence = _entityDependencyResolver.ResolveEntitySequence(requestedEntities);

            _logger.LogInformation("✅ [RESOLVE-DEPENDENCIES] Resolved {EntityCount} entities into {SequenceCount} processing phases: [{Sequence}]", 
                requestedEntities.Count, resolvedSequence.Count, string.Join(" → ", resolvedSequence));

            return await Task.FromResult(resolvedSequence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [RESOLVE-DEPENDENCIES] Failed to resolve entity dependencies for: [{Entities}]", 
                string.Join(", ", requestedEntities));
            throw;
        }
    }
}
