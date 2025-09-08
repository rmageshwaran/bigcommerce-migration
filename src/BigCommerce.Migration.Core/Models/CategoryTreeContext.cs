namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Represents category tree context for multi-storefront migrations
/// Contains resolved category tree IDs for both source and destination channels
/// </summary>
public class CategoryTreeContext
{
    /// <summary>
    /// A dictionary that maps a source category tree ID to a destination category tree ID.
    /// This is built from the channel mappings provided in the migration request.
    /// </summary>
    public Dictionary<string, string> CategoryTreeIdMapping { get; set; } = new();
} 