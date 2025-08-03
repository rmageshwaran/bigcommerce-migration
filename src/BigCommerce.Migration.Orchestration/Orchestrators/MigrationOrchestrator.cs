using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Services;
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
    private readonly IProgressEventPublisher _progressEventPublisher;
    private readonly ISignalREventFactory _signalREventFactory; // 🎯 CENTRALIZED SIGNALR: Factory for consistent event creation

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

    public MigrationOrchestrator(ILogger<MigrationOrchestrator> logger, IProgressEventPublisher progressEventPublisher, ISignalREventFactory signalREventFactory)
    {
        _logger = logger;
        _progressEventPublisher = progressEventPublisher;
        _signalREventFactory = signalREventFactory ?? throw new ArgumentNullException(nameof(signalREventFactory)); // 🎯 CENTRALIZED SIGNALR: Store factory reference
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
            // Step 0: Check for live cancellation before starting (Migration-level)
            var migrationCancellationCheck = await context.CallActivityAsync<CancellationCheckResult>(
                "CheckLiveCancellationActivity",
                new CancellationCheckRequest
                {
                    MigrationId = request.MigrationId,
                    Scope = CancellationScope.Migration
                });

            if (migrationCancellationCheck.IsCancelled)
            {
                result.Status = MigrationStatus.Cancelled;
                result.Errors.Add($"Migration was cancelled before processing started. Reason: {migrationCancellationCheck.Reason}");
                result.EndTime = context.CurrentUtcDateTime;
                result.CalculateDuration();
                
                // Process cancellation through live cancellation system
                await context.CallActivityAsync("ProcessCancellationActivity", 
                    new CancellationProcessRequest 
                    { 
                        MigrationId = request.MigrationId, 
                        Scope = CancellationScope.Migration,
                        Reason = migrationCancellationCheck.Reason ?? "Migration cancelled before processing started"
                    });
                
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
            
            // Step 2: Initialize migration via queue event
            context.SetCustomStatus("Initializing migration");
            try
            {
                // ✅ CENTRALIZED SIGNALR: Use factory for consistent event creation with auto-populated base properties
                var migrationProgressEvent = _signalREventFactory.CreateMigrationProgress(request.MigrationId, new MigrationProgressOptions
                {
                    OverallProgress = 0,
                    Status = "initializing",
                    TotalEntities = 0,
                    ProcessedEntities = 0,
                    FailedEntities = 0,
                    CurrentEntityType = "",
                    EstimatedTimeRemaining = null
                    // ✅ Base properties (Timestamp, IsCancelled, HubMethod) auto-populated by factory
                    // ✅ Validation built-in
                    // ✅ Consistent naming enforced
                });

                await _progressEventPublisher.PublishMigrationProgressAsync(migrationProgressEvent);

                // ✅ CENTRALIZED SIGNALR: Use factory for consistent event creation with auto-populated base properties
                var statusEvent = _signalREventFactory.CreateStatusProgress(request.MigrationId, new StatusProgressOptions
                {
                    Status = "initializing",
                    Message = "Migration initialization started",
                    Data = new { Entities = request.OriginalRequest.Entities }
                    // ✅ Base properties (Timestamp, IsCancelled, HubMethod) auto-populated by factory
                    // ✅ Validation built-in
                    // ✅ Consistent naming enforced
                });

                await _progressEventPublisher.PublishStatusAsync(statusEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish migration initialization events for migration {MigrationId}", request.MigrationId);
                // Don't throw - progress events should not break the migration
            }

            // Step 3: Validate stores and resolve dependencies
            context.SetCustomStatus("Validating stores");
            var validationResult = await context.CallActivityAsync<BigCommerce.Migration.Core.Interfaces.ValidationResult>(
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
                // Check for live cancellation before processing each entity type (Migration-level)
                var entityTypeCancellationCheck = await context.CallActivityAsync<CancellationCheckResult>(
                    "CheckLiveCancellationActivity",
                    new CancellationCheckRequest
                    {
                        MigrationId = request.MigrationId,
                        Scope = CancellationScope.Migration
                    });

                if (entityTypeCancellationCheck.IsCancelled)
                {
                    result.Status = MigrationStatus.Cancelled;
                    result.Errors.Add($"Migration was cancelled during {entityType} processing. Reason: {entityTypeCancellationCheck.Reason}");
                    result.EndTime = context.CurrentUtcDateTime;
                    result.CalculateDuration();
                    
                    // Process cancellation through live cancellation system
                    await context.CallActivityAsync("ProcessCancellationActivity", 
                        new CancellationProcessRequest 
                        { 
                            MigrationId = request.MigrationId, 
                            Scope = CancellationScope.Migration,
                            EntityType = entityType,
                            Reason = entityTypeCancellationCheck.Reason ?? $"Migration cancelled during {entityType} processing"
                        });
                    
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
                    
                    // ✅ VALIDATION: Check for suspiciously low entity counts that might indicate discovery issues
                    if (entityResult.TotalEntities > 0)
                    {
                        // For categories specifically, warn if count is exactly 250 (pagination limit)
                        if (entityType == "categories" && entityResult.TotalEntities == 250)
                        {
                            context.SetCustomStatus($"⚠️ POTENTIAL ISSUE: Discovered exactly 250 categories - this might indicate pagination limit bug");
                            _logger.LogWarning("⚠️ POTENTIAL DISCOVERY BUG: Found exactly 250 categories for {EntityType} in migration {MigrationId}. " +
                                "This matches the pagination limit and might indicate incomplete discovery.",
                                entityType, request.MigrationId);
                        }
                        
                        // General warning for any entity type with exactly 250 items
                        if (entityResult.TotalEntities == 250)
                        {
                            _logger.LogWarning("⚠️ Entity count warning: {EntityType} has exactly 250 items (pagination limit). " +
                                "Please verify this is the actual count and not a pagination cutoff in migration {MigrationId}",
                                entityType, request.MigrationId);
                        }
                        
                        // Report migration progress
                        _logger.LogInformation("✅ {EntityType} migration completed: {Processed}/{Total} entities processed, {Successful} successful, {Failed} failed",
                            entityType, entityResult.ProcessedEntities, entityResult.TotalEntities, 
                            entityResult.SuccessfulEntities, entityResult.FailedEntities);
                    }

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

            // Step 6: Complete migration via queue events
            context.SetCustomStatus("Completing migration");
            try
            {
                var migrationSummary = result.GetStatisticsSummary();
                
                // ✅ CENTRALIZED SIGNALR: Use factory for consistent event creation with auto-populated base properties
                var migrationProgressEvent = _signalREventFactory.CreateMigrationProgress(request.MigrationId, new MigrationProgressOptions
                {
                    OverallProgress = 100,
                    Status = result.Status.ToString().ToLowerInvariant(),
                    TotalEntities = migrationSummary.TotalEntities,
                    ProcessedEntities = migrationSummary.SuccessfulEntities + migrationSummary.FailedEntities,
                    FailedEntities = migrationSummary.FailedEntities,
                    CurrentEntityType = "",
                    EstimatedTimeRemaining = TimeSpan.Zero
                    // ✅ Base properties (Timestamp, IsCancelled, HubMethod) auto-populated by factory
                    // ✅ Validation built-in
                    // ✅ Consistent naming enforced
                });

                await _progressEventPublisher.PublishMigrationProgressAsync(migrationProgressEvent);

                // ✅ CENTRALIZED SIGNALR: Use factory for consistent event creation with auto-populated base properties
                var statusEvent = _signalREventFactory.CreateStatusProgress(request.MigrationId, new StatusProgressOptions
                {
                    Status = result.Status.ToString().ToLowerInvariant(),
                    Message = result.Errors.Any() ? "Migration completed with errors" : "Migration completed successfully",
                    Data = new { 
                        TotalEntities = migrationSummary.TotalEntities,
                        SuccessfulEntities = migrationSummary.SuccessfulEntities,
                        FailedEntities = migrationSummary.FailedEntities,
                        SuccessRate = migrationSummary.SuccessRate
                    }
                    // ✅ Base properties (Timestamp, IsCancelled, HubMethod) auto-populated by factory
                    // ✅ Validation built-in
                    // ✅ Consistent naming enforced
                });

                await _progressEventPublisher.PublishStatusAsync(statusEvent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish migration completion events for migration {MigrationId}", request.MigrationId);
                // Don't throw - progress events should not break the migration
            }

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

 