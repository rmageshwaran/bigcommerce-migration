using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Orchestration.Services;
using BigCommerce.Migration.Orchestration.Models;

namespace BigCommerce.Migration.Orchestration.Activities;

/// <summary>
/// Activity to retrieve all entity mappings for a specific migration and entity type
/// This ensures Phase 2 has access to all stored mappings from Phase 1, eliminating timing issues
/// </summary>
public class RetrieveEntityMappingsActivity
{
    private readonly ILogger<RetrieveEntityMappingsActivity> _logger;
    private readonly IEntityMappingService _entityMappingService;

    public RetrieveEntityMappingsActivity(
        ILogger<RetrieveEntityMappingsActivity> logger,
        IEntityMappingService entityMappingService)
    {
        _logger = logger;
        _entityMappingService = entityMappingService;
    }

    [Function("RetrieveEntityMappingsActivity")]
    public async Task<List<EntityMapping>> RunAsync(
        [ActivityTrigger] RetrieveEntityMappingsRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use strongly typed request - no parsing needed!
            var migrationId = request.MigrationId;
            var entityType = request.EntityType;

            _logger.LogInformation("🔍 [RETRIEVE-MAPPINGS] Retrieving entity mappings for MigrationId: {MigrationId}, EntityType: {EntityType}", 
                migrationId, entityType);

            // 🔄 RETRY LOGIC: Handle storage timing/consistency issues
            List<EntityMapping> mappings = null;
            int maxRetries = 5;
            int retryDelayMs = 1000; // Start with 1 second

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                // Retrieve all entity mappings for the migration and entity type
                mappings = await _entityMappingService.GetEntityMappingsAsync(migrationId, entityType, cancellationToken);

                if (mappings?.Count > 0)
                {
                    _logger.LogInformation("✅ [RETRIEVE-MAPPINGS] Successfully retrieved {Count} entity mappings for {EntityType} in migration {MigrationId} (attempt {Attempt})", 
                        mappings.Count, entityType, migrationId, attempt);
                    break; // Success!
                }

                if (attempt < maxRetries)
                {
                    _logger.LogWarning("⚠️ [RETRIEVE-MAPPINGS] Attempt {Attempt}: Found {Count} mappings. Retrying in {Delay}ms due to potential storage timing issue...", 
                        attempt, mappings?.Count ?? 0, retryDelayMs);
                    await Task.Delay(retryDelayMs, cancellationToken);
                    retryDelayMs *= 2; // Exponential backoff: 1s, 2s, 4s, 8s
                }
                else
                {
                    _logger.LogError("❌ [RETRIEVE-MAPPINGS] Final attempt {Attempt}: Still found {Count} mappings after {MaxRetries} retries", 
                        attempt, mappings?.Count ?? 0, maxRetries);
                }
            }

            mappings ??= new List<EntityMapping>(); // Ensure not null

            // Log first few mappings for debugging
            if (mappings.Any())
            {
                foreach (var mapping in mappings.Take(3))
                {
                    _logger.LogInformation("🔍 [RETRIEVE-MAPPINGS] Sample mapping: {SourceId} → {DestinationId}", 
                        mapping.SourceId, mapping.DestinationId);
                }
                
                if (mappings.Count > 3)
                {
                    _logger.LogInformation("🔍 [RETRIEVE-MAPPINGS] ... and {RemainingCount} more mappings", 
                        mappings.Count - 3);
                }
            }

            return mappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [RETRIEVE-MAPPINGS] Failed to retrieve entity mappings: {ErrorMessage}", ex.Message);
            throw;
        }
    }
}