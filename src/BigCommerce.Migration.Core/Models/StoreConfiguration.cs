namespace BigCommerce.Migration.Core.Models;

/// <summary>
/// Represents a BigCommerce store configuration with credentials and channel information
/// Used in migration requests to specify source/destination store details
/// </summary>
public class StoreConfiguration
{
    /// <summary>
    /// BigCommerce store identifier (e.g., "v6q95r5n91")
    /// </summary>
    public string? StoreId { get; set; }

    /// <summary>
    /// BigCommerce API access token for authentication
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Channel (storefront) identifier for multi-storefront support
    /// </summary>
    public string? ChannelId { get; set; }

    /// <summary>
    /// Optional base URL override (defaults to https://api.bigcommerce.com)
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Validates that all required fields are present and valid
    /// </summary>
    /// <returns>True if configuration is valid, false otherwise</returns>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(StoreId) &&
               !string.IsNullOrWhiteSpace(AccessToken) &&
               !string.IsNullOrWhiteSpace(ChannelId);
    }

    /// <summary>
    /// Gets the API base URL for this store (defaults to v3)
    /// </summary>
    /// <returns>Formatted API base URL</returns>
    public string GetApiBaseUrl()
    {
        return GetApiBaseUrl("v3");
    }

    /// <summary>
    /// Gets the API base URL for this store with specified version
    /// </summary>
    /// <param name="version">API version (v2 or v3)</param>
    /// <returns>Formatted API base URL</returns>
    public string GetApiBaseUrl(string version)
    {
        var baseUrl = BaseUrl ?? "https://api.bigcommerce.com";
        return $"{baseUrl}/stores/{StoreId}/{version}";
    }

    /// <summary>
    /// Gets authentication headers for BigCommerce API calls
    /// </summary>
    /// <returns>Dictionary of authentication headers</returns>
    public Dictionary<string, string> GetAuthHeaders()
    {
        return new Dictionary<string, string>
        {
            ["X-Auth-Token"] = AccessToken!,
            ["Accept"] = "application/json"
        };
    }

    /// <summary>
    /// Gets a masked version of the access token for logging
    /// </summary>
    /// <returns>Masked access token</returns>
    public string GetMaskedAccessToken()
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
            return "[EMPTY]";

        if (AccessToken.Length <= 8)
            return "****";

        // Find the last sequence of alphanumeric characters after any punctuation
        var lastAlphaNumericMatch = System.Text.RegularExpressions.Regex.Match(AccessToken, @"[a-zA-Z0-9]+$");
        if (lastAlphaNumericMatch.Success && lastAlphaNumericMatch.Index > 4)
        {
            // Token has punctuation, use the last alphanumeric sequence
            var lastPart = lastAlphaNumericMatch.Value;
            var endChars = lastPart.Length >= 4 ? 4 : lastPart.Length;
            return $"{AccessToken[..4]}****{lastPart[^endChars..]}";
        }

        // Token is mostly alphanumeric, use standard logic (last 3 chars)
        return $"{AccessToken[..4]}****{AccessToken[^3..]}";
    }

    /// <summary>
    /// Returns a string representation of the store configuration for logging
    /// </summary>
    /// <returns>String representation with masked token</returns>
    public override string ToString()
    {
        return $"Store: {StoreId}, Channel: {ChannelId}, Token: {GetMaskedAccessToken()}";
    }
} 