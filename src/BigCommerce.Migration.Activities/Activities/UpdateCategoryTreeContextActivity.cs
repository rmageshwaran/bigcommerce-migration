using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Activities.Activities;

/// <summary>
/// Activity function for updating category tree context with category mappings
/// </summary>
public class UpdateCategoryTreeContextActivity
{
    private readonly IMigrationStorageService _storageService;
    private readonly ILogger<UpdateCategoryTreeContextActivity> _logger;

    public UpdateCategoryTreeContextActivity(
        IMigrationStorageService storageService,
        ILogger<UpdateCategoryTreeContextActivity> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Updates category tree context with category mappings
    /// </summary>
    /// <param name="request">Category tree context update request</param>
    [Function("UpdateCategoryTreeContext")]
    public async Task UpdateCategoryTreeContextAsync(
        [ActivityTrigger] UpdateCategoryTreeContextRequest request)
    {
        try
        {
            _logger.LogInformation("Updating category tree context for migration {MigrationId} with {Count} category mappings", 
                request.MigrationId, request.CategoryMappings.Count);

            // Store category mappings for later use by other entity types
            foreach (var mapping in request.CategoryMappings)
            {
                var entityMapping = new EntityMapping
                {
                    MigrationId = request.MigrationId,
                    EntityType = "categories",
                    SourceId = mapping.Key,
                    DestinationId = mapping.Value
                };
                await _storageService.CreateEntityMappingAsync(entityMapping);
            }

            _logger.LogInformation("Category tree context updated successfully for migration {MigrationId}", 
                request.MigrationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update category tree context for migration {MigrationId}", 
                request.MigrationId);
            throw;
        }
    }
}

/// <summary>
/// Request model for updating category tree context
/// </summary>
public class UpdateCategoryTreeContextRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public Dictionary<string, string> CategoryMappings { get; set; } = new();
} 