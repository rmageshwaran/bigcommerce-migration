namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Represents complete channel context for multi-storefront migrations
/// Combines channel identifiers with resolved category tree IDs
/// Used throughout the migration process to maintain channel-specific context
/// </summary>
public class ChannelContext
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
    /// Only populated when migration includes categories
    /// </summary>
    public string? SourceCategoryTreeId { get; set; }

    /// <summary>
    /// Resolved category tree ID for the destination channel
    /// Only populated when migration includes categories
    /// </summary>
    public string? DestinationCategoryTreeId { get; set; }
} 