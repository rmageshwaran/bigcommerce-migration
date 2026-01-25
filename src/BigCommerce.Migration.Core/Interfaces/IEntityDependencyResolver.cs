namespace BigCommerce.Migration.Core.Interfaces;

/// <summary>
/// Interface for resolving entity dependencies and determining phased migration sequences.
/// Automatically identifies dependencies and dependents for comprehensive entity migration.
/// </summary>
public interface IEntityDependencyResolver
{
    /// <summary>
    /// Resolves the complete phased migration sequence for requested entities.
    /// For example, when "products" is requested, automatically includes all product phases:
    /// Phase 1: Products (250/page), Phase 2: Components (10/page), Phase 3: Variants, etc.
    /// </summary>
    /// <param name="requestedEntities">Original entities requested by the user</param>
    /// <returns>Complete sequence of entity types to migrate in dependency order</returns>
    List<string> ResolveEntitySequence(IEnumerable<string> requestedEntities);

    /// <summary>
    /// Determines if an entity type requires phased processing.
    /// Currently only "products" requires phased processing with 6 phases.
    /// </summary>
    /// <param name="entityType">Entity type to check</param>
    /// <returns>True if entity requires phased processing</returns>
    bool RequiresPhasedProcessing(string entityType);

    /// <summary>
    /// Gets the complete list of phases for an entity that requires phased processing.
    /// For "products": ["products", "product-components", "product-variants", "product-related", "product-metafields", "product-channels"]
    /// </summary>
    /// <param name="entityType">Entity type that requires phased processing</param>
    /// <returns>List of phase entity types in processing order</returns>
    List<string> GetEntityPhases(string entityType);

    /// <summary>
    /// Gets the configuration for a specific phase of an entity.
    /// Different phases may have different pagination requirements (250/page vs 10/page).
    /// </summary>
    /// <param name="phaseEntityType">Phase entity type (e.g., "product-components")</param>
    /// <returns>Phase configuration including pagination and processing settings</returns>
    EntityPhaseConfiguration GetPhaseConfiguration(string phaseEntityType);
}

/// <summary>
/// Configuration for a specific phase of entity processing
/// </summary>
public class EntityPhaseConfiguration
{
    /// <summary>
    /// Phase identifier (e.g., "product-components")
    /// </summary>
    public string PhaseType { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable phase name
    /// </summary>
    public string PhaseName { get; set; } = string.Empty;

    /// <summary>
    /// Phase number (1-6 for products)
    /// </summary>
    public int PhaseNumber { get; set; }

    /// <summary>
    /// Number of entities to fetch per page from BigCommerce API
    /// Phase 1: 250/page, Phase 2+: 10/page (due to includes)
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Include parameters for BigCommerce API
    /// Phase 1: bulk_pricing_rules,custom_fields,channels,videos
    /// Phase 2: options,modifiers,images,reviews
    /// </summary>
    public string Include { get; set; } = string.Empty;

    /// <summary>
    /// Whether this phase creates new entities or updates existing ones
    /// Phase 1: Creates products, Phase 2+: Creates/updates components
    /// </summary>
    public bool CreatesNewEntities { get; set; }

    /// <summary>
    /// Entity types that this phase will create/update
    /// Phase 2: ["options", "modifiers", "images", "reviews"]
    /// </summary>
    public List<string> TargetEntityTypes { get; set; } = new();

    /// <summary>
    /// Whether this phase requires the previous phase to complete first
    /// </summary>
    public bool RequiresPreviousPhaseCompletion { get; set; } = true;
}