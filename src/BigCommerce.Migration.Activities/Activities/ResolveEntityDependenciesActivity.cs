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
            var resolvedSequence = _entityDependencyResolver.ResolveEntitySequence(requestedEntities);
            
            _logger.LogInformation("✅ [RESOLVE-DEPENDENCIES] Resolved {EntityCount} entities into {SequenceCount} processing phases: [{Sequence}]", 
                requestedEntities.Count, resolvedSequence.Count, string.Join(" → ", resolvedSequence));

            // Log phased processing details
            foreach (var entityType in resolvedSequence)
            {
                if (_entityDependencyResolver.RequiresPhasedProcessing(entityType))
                {
                    var phases = _entityDependencyResolver.GetEntityPhases(entityType);
                    _logger.LogInformation("🔄 [RESOLVE-DEPENDENCIES] {EntityType} requires phased processing: {PhaseCount} phases", 
                        entityType, phases.Count);
                }
                else
                {
                    var config = _entityDependencyResolver.GetPhaseConfiguration(entityType);
                    _logger.LogDebug("📋 [RESOLVE-DEPENDENCIES] {EntityType} single-phase processing: {PageSize}/page", 
                        entityType, config.PageSize);
                }
            }

            return await Task.FromResult(resolvedSequence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [RESOLVE-DEPENDENCIES] Failed to resolve entity dependencies for: [{Entities}]", 
                string.Join(", ", requestedEntities));
            throw;
        }
    }

    /// <summary>
    /// Gets the configuration for a specific phase of entity processing.
    /// Used for understanding pagination and processing requirements for each phase.
    /// </summary>
    /// <param name="phaseEntityType">Phase entity type (e.g., "product-components")</param>
    /// <returns>Phase configuration with pagination and processing settings</returns>
    [Function("GetEntityPhaseConfiguration")]
    public async Task<EntityPhaseConfiguration> GetEntityPhaseConfiguration([ActivityTrigger] string phaseEntityType)
    {
        _logger.LogDebug("🔧 [PHASE-CONFIG] Getting configuration for phase: {PhaseType}", phaseEntityType);

        try
        {
            var config = _entityDependencyResolver.GetPhaseConfiguration(phaseEntityType);
            
            _logger.LogDebug("✅ [PHASE-CONFIG] Retrieved configuration for {PhaseType}: {PageSize}/page, Phase {PhaseNumber}", 
                phaseEntityType, config.PageSize, config.PhaseNumber);

            return await Task.FromResult(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE-CONFIG] Failed to get configuration for phase: {PhaseType}", phaseEntityType);
            throw;
        }
    }
}