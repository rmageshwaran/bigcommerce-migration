using BigCommerce.Migration.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BigCommerce.Migration.Infrastructure.Services;

/// <summary>
/// Service for resolving entity dependencies and determining phased migration sequences.
/// Implements intelligent entity resolution for comprehensive product ecosystem migration.
/// </summary>
public class EntityDependencyResolver : IEntityDependencyResolver
{
    private readonly ILogger<EntityDependencyResolver> _logger;

    /// <summary>
    /// Predefined phase configurations for different entity types
    /// </summary>
    private readonly Dictionary<string, EntityPhaseConfiguration> _phaseConfigurations;

    /// <summary>
    /// Initializes a new instance of the EntityDependencyResolver
    /// </summary>
    /// <param name="logger">Logger for tracking dependency resolution operations</param>
    public EntityDependencyResolver(ILogger<EntityDependencyResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _phaseConfigurations = InitializePhaseConfigurations();
    }

    /// <summary>
    /// Resolves the complete phased migration sequence for requested entities.
    /// When "products" is requested, automatically includes all 6 product phases.
    /// </summary>
    public List<string> ResolveEntitySequence(IEnumerable<string> requestedEntities)
    {
        var requestedList = requestedEntities.ToList();
        _logger.LogInformation("🧠 [ENTITY-DEPENDENCY-RESOLVER] Resolving entity sequence for: [{Entities}]", 
            string.Join(", ", requestedList));

        var resolvedSequence = new List<string>();

        // First, add non-phased entities in dependency order
        var basicDependencyOrder = new[] { "brands", "categories" };
        foreach (var basicEntity in basicDependencyOrder)
        {
            if (requestedList.Contains(basicEntity, StringComparer.OrdinalIgnoreCase))
            {
                resolvedSequence.Add(basicEntity);
                _logger.LogDebug("🔗 [ENTITY-DEPENDENCY-RESOLVER] Added basic entity: {Entity}", basicEntity);
            }
        }

        // Then, process entities that require phased processing
        foreach (var requestedEntity in requestedList)
        {
            if (RequiresPhasedProcessing(requestedEntity))
            {
                var phases = GetEntityPhases(requestedEntity);
                resolvedSequence.AddRange(phases);
                
                _logger.LogInformation("🔄 [ENTITY-DEPENDENCY-RESOLVER] Added {PhaseCount} phases for {Entity}: [{Phases}]", 
                    phases.Count, requestedEntity, string.Join(", ", phases));
            }
            else if (!resolvedSequence.Contains(requestedEntity, StringComparer.OrdinalIgnoreCase))
            {
                // Add single-phase entities that aren't already included
                resolvedSequence.Add(requestedEntity);
                _logger.LogDebug("🔗 [ENTITY-DEPENDENCY-RESOLVER] Added single-phase entity: {Entity}", requestedEntity);
            }
        }

        _logger.LogInformation("✅ [ENTITY-DEPENDENCY-RESOLVER] Resolved sequence: [{Sequence}]", 
            string.Join(" → ", resolvedSequence));

        return resolvedSequence;
    }

    /// <summary>
    /// Determines if an entity type requires phased processing.
    /// Currently only "products" requires phased processing.
    /// </summary>
    public bool RequiresPhasedProcessing(string entityType)
    {
        return entityType.Equals("products", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the complete list of phases for "products" entity type.
    /// Returns all 6 phases in the correct processing order.
    /// </summary>
    public List<string> GetEntityPhases(string entityType)
    {
        if (!RequiresPhasedProcessing(entityType))
        {
            return new List<string> { entityType };
        }

        if (entityType.Equals("products", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>
            {
                "products",              // Phase 1: Core products (250/page)
                "product-components",    // Phase 2: Options, modifiers, reviews (10/page)
                "product-related",       // Phase 3: Related products updates (provides timing gap for option mappings)
                "product-images",        // Phase 4: Product images migration (individual product updates)
                "product-channel-assign", // Phase 5: Product channel assignments (bulk API updates)
                "variants",              // Phase 6: Product variants ✅ with option mappings ready
                "product-metafields"     // Phase 7: Product metafields ✅ NEW - after all dependencies complete
            };
        }

        return new List<string> { entityType };
    }

    /// <summary>
    /// Gets the configuration for a specific phase of entity processing.
    /// </summary>
    public EntityPhaseConfiguration GetPhaseConfiguration(string phaseEntityType)
    {
        if (_phaseConfigurations.TryGetValue(phaseEntityType.ToLowerInvariant(), out var config))
        {
            return config;
        }

        _logger.LogWarning("⚠️ [ENTITY-DEPENDENCY-RESOLVER] No configuration found for phase: {PhaseType}", phaseEntityType);
        
        // Return default configuration
        return new EntityPhaseConfiguration
        {
            PhaseType = phaseEntityType,
            PhaseName = phaseEntityType,
            PhaseNumber = 1,
            PageSize = 250,
            Include = string.Empty,
            CreatesNewEntities = true,
            TargetEntityTypes = new List<string> { phaseEntityType },
            RequiresPreviousPhaseCompletion = true
        };
    }

    /// <summary>
    /// Initializes the predefined phase configurations for all supported phases.
    /// </summary>
    private Dictionary<string, EntityPhaseConfiguration> InitializePhaseConfigurations()
    {
        return new Dictionary<string, EntityPhaseConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["products"] = new EntityPhaseConfiguration
            {
                PhaseType = "products",
                PhaseName = "Core Products",
                PhaseNumber = 1,
                PageSize = 250,
                Include = "bulk_pricing_rules,custom_fields,channels,videos",
                CreatesNewEntities = true,
                TargetEntityTypes = new List<string> { "products" },
                RequiresPreviousPhaseCompletion = false // First phase
            },
            
            ["product-components"] = new EntityPhaseConfiguration
            {
                PhaseType = "product-components",
                PhaseName = "Product Components",
                PhaseNumber = 2,
                PageSize = 10, // Reduced due to comprehensive includes
                Include = "options,modifiers,reviews",
                CreatesNewEntities = true,
                TargetEntityTypes = new List<string> { "options", "modifiers", "reviews" },
                RequiresPreviousPhaseCompletion = true
            },
            
                    ["variants"] = new EntityPhaseConfiguration
        {
            PhaseType = "variants",
                PhaseName = "Product Variants",
                PhaseNumber = 6, // Phase 6 (after product-channel-assign)
                PageSize = 250, // ✅ FIXED: Match appsettings.json for optimal API efficiency
                Include = string.Empty,
                CreatesNewEntities = true,
                TargetEntityTypes = new List<string> { "variants" },
                RequiresPreviousPhaseCompletion = true
            },
            
            // ✨ Individual component type configurations
            ["options"] = new EntityPhaseConfiguration
            {
                PhaseType = "options",
                PhaseName = "Product Options",
                PhaseNumber = 2,
                PageSize = 10, // Reduced due to comprehensive includes
                Include = "options", // Include all to get full product data
                CreatesNewEntities = true,
                TargetEntityTypes = new List<string> { "options" },
                RequiresPreviousPhaseCompletion = true
            },
            
            ["modifiers"] = new EntityPhaseConfiguration
            {
                PhaseType = "modifiers",
                PhaseName = "Product Modifiers",
                PhaseNumber = 2,
                PageSize = 10,
                Include = "modifiers",
                CreatesNewEntities = true,
                TargetEntityTypes = new List<string> { "modifiers" },
                RequiresPreviousPhaseCompletion = true
            },
            
            ["reviews"] = new EntityPhaseConfiguration
            {
                PhaseType = "reviews",
                PhaseName = "Product Reviews",
                PhaseNumber = 2,
                PageSize = 10,
                Include = "reviews",
                CreatesNewEntities = true,
                TargetEntityTypes = new List<string> { "reviews" },
                RequiresPreviousPhaseCompletion = true
            },
            
            ["product-related"] = new EntityPhaseConfiguration
            {
                PhaseType = "product-related",
                PhaseName = "Related Products",
                PhaseNumber = 3, // Phase 3 (after product-components)
                PageSize = 50,
                Include = "related_products",
                CreatesNewEntities = false, // Updates existing products
                TargetEntityTypes = new List<string> { "products" }, // Updates products with related product IDs
                RequiresPreviousPhaseCompletion = true
            },
            
            ["product-images"] = new EntityPhaseConfiguration
            {
                PhaseType = "product-images",
                PhaseName = "Product Images",
                PhaseNumber = 4, // Phase 4 (after product-related, before product-channel-assign)
                PageSize = 20, // Products per discovery batch
                Include = "", // No include needed - uses EntityMappings discovery
                CreatesNewEntities = false, // Updates existing products with images
                TargetEntityTypes = new List<string> { "products" }, // Updates products with image data
                RequiresPreviousPhaseCompletion = true
            },
            
            ["product-channel-assign"] = new EntityPhaseConfiguration
            {
                PhaseType = "product-channel-assign",
                PhaseName = "Product Channel Assignments",
                PhaseNumber = 5, // Phase 5 (after product-images, before variants)
                PageSize = 250, // Products per discovery batch (similar to variants)
                Include = "", // No include needed - uses EntityMappings discovery
                CreatesNewEntities = false, // Updates existing product channel assignments
                TargetEntityTypes = new List<string> { "products" }, // Updates products with channel assignments
                RequiresPreviousPhaseCompletion = true
            },
            
            ["product-metafields"] = new EntityPhaseConfiguration
            {
                PhaseType = "product-metafields",
                PhaseName = "Product Metafields",
                PhaseNumber = 7, // Phase 7 - after variants (Phase 6)
                PageSize = 250, // Use max API limit for efficiency
                Include = "", // No includes needed - endpoint doesn't support includes
                CreatesNewEntities = true,
                TargetEntityTypes = new List<string> { "product-metafields" },
                RequiresPreviousPhaseCompletion = true
            },
            
            ["product-channels"] = new EntityPhaseConfiguration
            {
                PhaseType = "product-channels",
                PhaseName = "Channel Assignments",
                PhaseNumber = 8, // Moved to phase 8 (after product-metafields)
                PageSize = 50,
                Include = "channels",
                CreatesNewEntities = false, // Updates existing channel assignments
                TargetEntityTypes = new List<string> { "channels" },
                RequiresPreviousPhaseCompletion = true
            }
        };
    }
}
