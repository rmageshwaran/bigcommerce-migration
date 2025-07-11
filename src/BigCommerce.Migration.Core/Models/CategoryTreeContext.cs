namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Represents category tree context for multi-storefront migrations
/// Contains resolved category tree IDs for both source and destination channels
/// </summary>
public class CategoryTreeContext
{
    /// <summary>
    /// Source channel (storefront) identifier
    /// </summary>
    public string? SourceChannelId { get; set; }

    /// <summary>
    /// Destination channel (storefront) identifier
    /// </summary>
    public string? DestinationChannelId { get; set; }

    /// <summary>
    /// Resolved category tree ID for the source channel
    /// Retrieved from BigCommerce API: GET /stores/{store_id}/v3/catalog/trees?channel_id={sourceChannelId}
    /// </summary>
    public string? SourceCategoryTreeId { get; set; }

    /// <summary>
    /// Resolved category tree ID for the destination channel
    /// Retrieved from BigCommerce API: GET /stores/{store_id}/v3/catalog/trees?channel_id={destinationChannelId}
    /// </summary>
    public string? DestinationCategoryTreeId { get; set; }
} 