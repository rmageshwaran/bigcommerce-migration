using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Models;
using System.Text.Json;

namespace BigCommerce.Migration.Orchestration.Orchestrators;

/// <summary>
/// Main migration orchestrator that coordinates the entire migration process
/// Follows Azure Durable Functions deterministic behavior constraints
/// </summary>
public class MigrationOrchestrator
{
    private readonly ILogger<MigrationOrchestrator> _logger;

    // Entity processing order based on dependencies
    private static readonly string[] EntityProcessingOrder = new[]
    {
        "categories",    // Must be first (no dependencies)
        "brands",        // No dependencies
        "products",      // Depends on categories
        "variants",      // Depends on categories and products
        "images",        // Depends on categories and products
        "modifiers"      // Depends on categories and products
    };

    public MigrationOrchestrator(ILogger<MigrationOrchestrator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Main orchestrator function that coordinates the entire migration process
    /// </summary>
    /// <param name="context">Durable orchestration context</param>
    /// <returns>Migration result</returns>
    [Function("MigrationOrchestrator")]
    public async Task<MigrationResult> RunMigrationOrchestrator(
        [OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var startTime = context.CurrentUtcDateTime; // ✅ Use deterministic datetime
        var request = context.GetInput<MigrationOrchestrationRequest>();
        
        // Initialize result with deterministic values
        var result = new MigrationResult
        {
            MigrationId = request.MigrationId,
            Status = MigrationStatus.InProgress,
            StartTime = startTime,
            EntityResults = new Dictionary<string, EntityMigrationResult>(),
            Errors = new List<string>(),
            Statistics = new MigrationStatistics()
        };

        try
        {
            // Step 0: Check for cancellation before starting
            var isCancelled = await context.CallActivityAsync<bool>("CheckMigrationCancellation", request.MigrationId);
            if (isCancelled)
            {
                result.Status = MigrationStatus.Cancelled;
                result.Errors.Add("Migration was cancelled before processing started");
                result.EndTime = context.CurrentUtcDateTime;
                result.CalculateDuration();
                return result;
            }

            // Step 1: Validate request
            context.SetCustomStatus($"Starting migration {request.MigrationId}");
            
            if (!request.IsValid())
            {
                var validationErrors = request.GetValidationErrors();
                result.Status = MigrationStatus.Failed;
                result.Errors.AddRange(validationErrors);
                result.EndTime = context.CurrentUtcDateTime;
                result.CalculateDuration();
                return result;
            }

            // Generate deterministic correlation ID for this migration ✅ Use deterministic GUID
            var correlationId = context.NewGuid();
            
            // Step 2: Initialize migration
            context.SetCustomStatus("Initializing migration");
            await context.CallActivityAsync("InitializeMigration", new
            {
                MigrationId = request.MigrationId,
                CorrelationId = correlationId,
                Request = request
            });

            // Step 3: Validate stores and resolve dependencies
            context.SetCustomStatus("Validating stores");
            var validationResult = await context.CallActivityAsync<ValidationResult>(
                "ValidateMigrationStores", new
                {
                    MigrationId = request.MigrationId,
                    SourceStore = request.OriginalRequest.SourceStore,
                    DestinationStore = request.OriginalRequest.DestinationStore
                });

            if (!validationResult.IsValid)
            {
                result.Status = MigrationStatus.Failed;
                result.Errors.Add(validationResult.ErrorMessage);
                result.EndTime = context.CurrentUtcDateTime;
                result.CalculateDuration();
                return result;
            }

            // Step 4: Resolve category tree IDs if categories are being migrated
            if (request.OriginalRequest.Entities.Contains("categories"))
            {
                context.SetCustomStatus("Resolving category tree IDs");
                var categoryTreeResult = await context.CallActivityAsync<CategoryTreeContext>(
                    "ResolveCategoryTreeIds", new
                    {
                        MigrationId = request.MigrationId,
                        SourceStore = request.OriginalRequest.SourceStore,
                        DestinationStore = request.OriginalRequest.DestinationStore,
                        CategoryTreeContext = request.CategoryTreeContext
                    });

                // Update the category tree context with resolved IDs
                request.CategoryTreeContext = categoryTreeResult;
            }

            // Step 5: Process entities in dependency order
            var requestedEntities = request.OriginalRequest.Entities.ToList();
            var entitiesToProcess = EntityProcessingOrder
                .Where(entity => requestedEntities.Contains(entity, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var entityType in entitiesToProcess)
            {
                // Check for cancellation before processing each entity type
                isCancelled = await context.CallActivityAsync<bool>("CheckMigrationCancellation", request.MigrationId);
                if (isCancelled)
                {
                    result.Status = MigrationStatus.Cancelled;
                    result.Errors.Add($"Migration was cancelled during {entityType} processing");
                    result.EndTime = context.CurrentUtcDateTime;
                    result.CalculateDuration();
                    return result;
                }

                context.SetCustomStatus($"Processing {entityType}");
                
                try
                {
                    var entityRequest = new EntityMigrationRequest
                    {
                        MigrationId = request.MigrationId,
                        EntityType = entityType,
                        SourceStore = request.OriginalRequest.SourceStore!,
                        DestinationStore = request.OriginalRequest.DestinationStore!,
                        CategoryTreeContext = request.CategoryTreeContext,
                        EntityConfig = CreateEntityConfiguration(entityType),
                        BatchSize = GetOptimalBatchSize(entityType),
                        MaxRetries = 0 // ✅ Disable retry logic per user preference to avoid API rate limit increases
                    };

                    // Call the entity migration sub-orchestrator
                    var entityResult = await context.CallSubOrchestratorAsync<EntityMigrationResult>(
                        "EntityMigrationOrchestrator", entityRequest);

                    result.EntityResults[entityType] = entityResult;

                    // Update category tree context with category mappings if needed
                    if (entityType == "categories" && entityResult.Mappings.Any())
                    {
                        await context.CallActivityAsync("UpdateCategoryTreeContext", new
                        {
                            MigrationId = request.MigrationId,
                            CategoryMappings = entityResult.Mappings
                        });
                    }

                    // Log progress
                    var progress = CalculateProgress(result.EntityResults, entitiesToProcess);
                    context.SetCustomStatus($"Migration progress: {progress:F1}%");
                }
                catch (Exception ex)
                {
                    var errorMessage = $"Failed to process {entityType}: {ex.Message}";
                    result.Errors.Add(errorMessage);
                    
                    // Also add the original error message for test compatibility
                    if (!result.Errors.Contains(ex.Message))
                    {
                        result.Errors.Add(ex.Message);
                    }
                    
                    // Create a failed entity result
                    result.EntityResults[entityType] = new EntityMigrationResult
                    {
                        EntityType = entityType,
                        TotalEntities = 0,
                        SuccessfulEntities = 0,
                        FailedEntities = 0,
                        Errors = new List<string> { errorMessage }
                    };
                }
            }

            // Step 6: Complete migration
            context.SetCustomStatus("Completing migration");
            await context.CallActivityAsync("CompleteMigration", new
            {
                MigrationId = request.MigrationId,
                Result = result
            });

            // Step 7: Set final status
            result.Status = result.Errors.Any() ? MigrationStatus.Failed : MigrationStatus.Completed;
            result.EndTime = context.CurrentUtcDateTime;
            result.CalculateDuration();

            // Calculate final statistics
            var summary = result.GetStatisticsSummary();
            context.SetCustomStatus($"Migration completed: {summary.SuccessfulEntities}/{summary.TotalEntities} entities migrated ({summary.SuccessRate:F1}% success rate)");

            return result;
        }
        catch (Exception ex)
        {
            // Handle any unhandled exceptions
            context.SetCustomStatus("Migration failed");
            
            result.Status = MigrationStatus.Failed;
            result.Errors.Add($"Migration failed with error: {ex.Message}");
            
            // Also add the original error message for test compatibility
            if (!result.Errors.Contains(ex.Message))
            {
                result.Errors.Add(ex.Message);
            }
            
            result.EndTime = context.CurrentUtcDateTime;
            result.CalculateDuration();

            return result;
        }
    }

    /// <summary>
    /// Creates entity-specific configuration based on entity type
    /// </summary>
    /// <param name="entityType">The entity type</param>
    /// <returns>Entity configuration</returns>
    private static EntityConfiguration CreateEntityConfiguration(string entityType)
    {
        return new EntityConfiguration
        {
            EntityType = entityType,
            IncludeDeleted = false,
            IncludeDrafts = entityType == "products", // Include product drafts
            IncludeImages = true,
            IncludeMetadata = true,
            CreateMappings = true,
            FieldMappings = new Dictionary<string, string>(),
            Settings = new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Gets optimal batch size for different entity types
    /// </summary>
    /// <param name="entityType">The entity type</param>
    /// <returns>Optimal batch size</returns>
    private static int GetOptimalBatchSize(string entityType)
    {
        return entityType switch
        {
            "categories" => 25,
            "products" => 10,
            "brands" => 50,
            "variants" => 20,
            "images" => 15,
            "modifiers" => 30,
            _ => 10
        };
    }

    /// <summary>
    /// Calculates migration progress as a percentage
    /// </summary>
    /// <param name="entityResults">Current entity results</param>
    /// <param name="totalEntities">Total entities to process</param>
    /// <returns>Progress percentage</returns>
    private static double CalculateProgress(Dictionary<string, EntityMigrationResult> entityResults, List<string> totalEntities)
    {
        if (!totalEntities.Any()) return 100.0;

        var completedEntities = entityResults.Count;
        return (double)completedEntities / totalEntities.Count * 100.0;
    }
}

 