namespace BigCommerce.Migration.Orchestration.Models;

/// <summary>
/// Strongly typed request for retrieving entity mappings from storage
/// </summary>
public class RetrieveEntityMappingsRequest
{
    public string MigrationId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
}