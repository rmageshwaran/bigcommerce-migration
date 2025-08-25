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
            
            //_logger.LogInformation("🔄 [RESOLVE-DEPENDENCIES] Initial resolved sequence: [{Sequence}]", 
            //    string.Join(", ", resolvedSequence));

            // ✨ STEP 2: Then expand product-components into individual component types
            //var expandedSequence = new List<string>();
            //foreach (var entity in resolvedSequence)
            //{
            //    if (entity.Equals("product-components", StringComparison.OrdinalIgnoreCase))
            //    {
            //        _logger.LogInformation("🔗 [EXPAND-COMPONENTS] Expanding 'product-components' into individual component types: options, modifiers, reviews");
            //        expandedSequence.AddRange(new[] { "options", "modifiers", "reviews" });
            //    }
            //    else
            //    {
            //        expandedSequence.Add(entity);
            //    }
            //}
            
            //_logger.LogInformation("🔄 [EXPAND-COMPONENTS] Final expanded sequence: [{ExpandedSequence}]", 
            //    string.Join(", ", expandedSequence));

            //// Use the expanded sequence for the rest of the logic
            //resolvedSequence = expandedSequence;
            
            _logger.LogInformation("✅ [RESOLVE-DEPENDENCIES] Resolved {EntityCount} entities into {SequenceCount} processing phases: [{Sequence}]", 
                requestedEntities.Count, resolvedSequence.Count, string.Join(" → ", resolvedSequence));

            // Log phased processing details
            //foreach (var entityType in resolvedSequence)
            //{
            //    if (_entityDependencyResolver.RequiresPhasedProcessing(entityType))
            //    {
            //        var phases = _entityDependencyResolver.GetEntityPhases(entityType);
            //        _logger.LogInformation("🔄 [RESOLVE-DEPENDENCIES] {EntityType} requires phased processing: {PhaseCount} phases", 
            //            entityType, phases.Count);
            //    }
            //    else
            //    {
            //        var config = _entityDependencyResolver.GetPhaseConfiguration(entityType);
            //        _logger.LogDebug("📋 [RESOLVE-DEPENDENCIES] {EntityType} single-phase processing: {PageSize}/page", 
            //            entityType, config.PageSize);
            //    }
            //}

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
