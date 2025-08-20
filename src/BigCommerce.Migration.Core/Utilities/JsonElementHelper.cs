using System.Globalization;
using System.Text.Json;

namespace BigCommerce.Migration.Core.Utilities;

/// <summary>
/// Helper class for safely extracting values from objects that might be JsonElements
/// This is particularly useful when dealing with Dictionary&lt;string, object&gt; values
/// that have been serialized/deserialized through Durable Functions or Azure Table Storage
/// </summary>
public static class JsonElementHelper
{
    /// <summary>
    /// Safely extracts an integer value from an object that might be a JsonElement
    /// </summary>
    /// <param name="value">The value to convert to integer</param>
    /// <returns>The integer value, or 0 if conversion fails</returns>
    public static int GetIntegerValue(object? value)
    {
        if (value == null) return 0;
        
        if (value is int intValue)
            return intValue;
            
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.TryGetInt32(out var intResult) ? intResult : 0;
                
            if (jsonElement.ValueKind == JsonValueKind.String)
                return int.TryParse(jsonElement.GetString(), out var stringResult) ? stringResult : 0;
        }
        
        if (value is string stringValue)
            return int.TryParse(stringValue, out var stringParsed) ? stringParsed : 0;
            
        // Try direct conversion for other numeric types (long, decimal, etc.)
        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Safely extracts a boolean value from an object that might be a JsonElement
    /// </summary>
    /// <param name="value">The value to convert to boolean</param>
    /// <returns>The boolean value, or false if conversion fails</returns>
    public static bool GetBooleanValue(object? value)
    {
        if (value == null) return false;
        
        if (value is bool boolValue)
            return boolValue;
            
        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.True ||
                   (jsonElement.ValueKind == JsonValueKind.String && 
                    bool.TryParse(jsonElement.GetString(), out var parsed) && parsed);
        }
        
        if (value is string stringValue)
            return bool.TryParse(stringValue, out var stringParsed) && stringParsed;
            
        return false;
    }

    /// <summary>
    /// Safely extracts a string value from an object that might be a JsonElement
    /// </summary>
    /// <param name="value">The value to convert to string</param>
    /// <returns>The string value, or empty string if conversion fails</returns>
    public static string GetStringValue(object? value)
    {
        if (value == null) return string.Empty;
        
        if (value is string stringValue)
            return stringValue;
            
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
                return jsonElement.GetString() ?? string.Empty;
                
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.ToString();
                
            if (jsonElement.ValueKind == JsonValueKind.True)
                return "true";
                
            if (jsonElement.ValueKind == JsonValueKind.False)
                return "false";
        }
        
        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Safely extracts a long value from an object that might be a JsonElement
    /// </summary>
    /// <param name="value">The value to convert to long</param>
    /// <returns>The long value, or 0 if conversion fails</returns>
    public static long GetLongValue(object? value)
    {
        if (value == null) return 0L;
        
        if (value is long longValue)
            return longValue;
            
        if (value is int intValue)
            return intValue;
            
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.TryGetInt64(out var longResult) ? longResult : 0L;
                
            if (jsonElement.ValueKind == JsonValueKind.String)
                return long.TryParse(jsonElement.GetString(), out var stringResult) ? stringResult : 0L;
        }
        
        if (value is string stringValue)
            return long.TryParse(stringValue, out var stringParsed) ? stringParsed : 0L;
            
        // Try direct conversion for other numeric types
        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0L;
        }
    }

    /// <summary>
    /// Safely extracts a decimal value from an object that might be a JsonElement
    /// </summary>
    /// <param name="value">The value to convert to decimal</param>
    /// <returns>The decimal value, or 0 if conversion fails</returns>
    public static decimal GetDecimalValue(object? value)
    {
        if (value == null) return 0m;
        
        if (value is decimal decimalValue)
            return decimalValue;
            
        if (value is int intValue)
            return intValue;
            
        if (value is long longValue)
            return longValue;
            
        if (value is double doubleValue)
            return (decimal)doubleValue;
            
        if (value is float floatValue)
            return (decimal)floatValue;
            
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.TryGetDecimal(out var decimalResult) ? decimalResult : 0m;
                
            if (jsonElement.ValueKind == JsonValueKind.String)
                return decimal.TryParse(jsonElement.GetString(), out var stringResult) ? stringResult : 0m;
        }
        
        if (value is string stringValue)
            return decimal.TryParse(stringValue, out var stringParsed) ? stringParsed : 0m;
            
        // Try direct conversion for other numeric types
        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0m;
        }
    }
}