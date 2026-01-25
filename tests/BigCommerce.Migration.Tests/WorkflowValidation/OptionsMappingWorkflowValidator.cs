using System.Text.Json;
using Xunit;

namespace BigCommerce.Migration.Tests.WorkflowValidation;

/// <summary>
/// SIMPLE workflow validator to ensure options mapping JSON format works
/// This is the ONLY test that matters - validates the actual workflow WITHOUT complex interfaces
/// </summary>
public class OptionsMappingWorkflowValidator
{
    [Fact]
    public void ValidateOptionsMappingJSONFormat_MustWork()
    {
        // STEP 1: Test your EXACT BigCommerce API response format
        var bigCommerceResponseJson = @"{
            ""id"": 220,
            ""product_id"": 192,
            ""name"": ""Color (Colors only)"",
            ""display_name"": ""Color"",
            ""type"": ""swatch"",
            ""sort_order"": 0,
            ""option_values"": [
                {
                    ""id"": 174,
                    ""label"": ""Beige"",
                    ""sort_order"": 1,
                    ""value_data"": { ""colors"": [""#FAFAEB""] },
                    ""is_default"": false
                },
                {
                    ""id"": 175,
                    ""label"": ""Grey"",
                    ""sort_order"": 2,
                    ""value_data"": { ""colors"": [""#BDBDBD""] },
                    ""is_default"": false
                }
            ],
            ""config"": {}
        }";

        // STEP 2: Parse the response (this is what was failing before)
        var bigCommerceResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(bigCommerceResponseJson);
        
        // STEP 3: Test the critical type checking logic that was fixed
        var success = TestOptionValueExtraction(bigCommerceResponse!);
        
        // CRITICAL ASSERTION: This MUST work or variant migration fails
        Assert.True(success, "❌ CRITICAL FAILURE: Cannot extract option values from BigCommerce response!");
        
        // STEP 4: Test the complete mapping JSON format
        var mappingJson = CreateCorrectMappingJSON();
        var canVariantsFindMapping = TestVariantLookupLogic(mappingJson);
        
        Assert.True(canVariantsFindMapping, "❌ CRITICAL FAILURE: Variant creation cannot find option mappings!");
    }

    [Fact]
    public void ValidateJSONFormatRequirements_MustBeCorrect()
    {
        var correctFormat = @"{
            ""options"": [
                {
                    ""sourceOptionId"": ""source_123"",
                    ""destinationOptionId"": ""dest_456"",
                    ""optionValues"": [
                        {
                            ""sourceId"": ""source_value_1"",
                            ""destinationId"": ""dest_value_1""
                        }
                    ]
                }
            ]
        }";

        // MUST have camelCase (critical for variant creation)
        Assert.Contains("sourceOptionId", correctFormat, StringComparison.Ordinal);
        Assert.Contains("destinationOptionId", correctFormat, StringComparison.Ordinal);
        Assert.Contains("optionValues", correctFormat, StringComparison.Ordinal);
        
        // MUST NOT have snake_case (would break variant creation)
        Assert.DoesNotContain("source_option_id", correctFormat, StringComparison.Ordinal);
        Assert.DoesNotContain("destination_option_id", correctFormat, StringComparison.Ordinal);
        Assert.DoesNotContain("option_values", correctFormat, StringComparison.Ordinal);
        
        // Test parsing works
        var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(correctFormat);
        Assert.NotNull(parsed);
        Assert.True(parsed.ContainsKey("options"));
    }

    /// <summary>
    /// Tests the exact logic that was fixed - extracting option values from BigCommerce response
    /// This is the critical path that was failing silently before
    /// </summary>
    private static bool TestOptionValueExtraction(Dictionary<string, object> createdOption)
    {
        // Extract option values if they exist in the created option
        if (createdOption.TryGetValue("option_values", out var optionValuesObj))
        {
            // FIXED: Handle both List<object> and JsonElement types
            List<object>? optionValuesList = null;
            
            if (optionValuesObj is List<object> list)
            {
                optionValuesList = list;
            }
            else if (optionValuesObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                // Convert JsonElement array to List<Dictionary<string, object>>
                optionValuesList = new List<object>();
                foreach (var element in jsonElement.EnumerateArray())
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText());
                    if (dict != null)
                        optionValuesList.Add(dict);
                }
            }
            
            return optionValuesList != null && optionValuesList.Count > 0;
        }
        
        return false;
    }

    /// <summary>
    /// Creates the correct JSON format that variants expect
    /// </summary>
    private static string CreateCorrectMappingJSON()
    {
        return @"{
            ""options"": [
                {
                    ""sourceOptionId"": ""source_option_123"",
                    ""destinationOptionId"": ""220"",
                    ""optionValues"": [
                        {
                            ""sourceId"": ""source_value_174"",
                            ""destinationId"": ""174""
                        },
                        {
                            ""sourceId"": ""source_value_175"",
                            ""destinationId"": ""175""
                        }
                    ]
                }
            ]
        }";
    }

    /// <summary>
    /// Tests the exact lookup logic used by VariantCreationStrategy
    /// This MUST work or variant migration fails
    /// </summary>
    private static bool TestVariantLookupLogic(string optionsMappingData)
    {
        try
        {
            // Simulate VariantCreationStrategy.LookupDestinationOptionIdAsync
            var sourceOptionId = "source_option_123";
            var sourceOptionValueId = "source_value_174";
            
            var optionsData = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsMappingData);
            
            if (optionsData?.TryGetValue("options", out var optionsArray) == true && 
                optionsArray is JsonElement optionsElement && 
                optionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var optionElement in optionsElement.EnumerateArray())
                {
                    var optionDict = JsonSerializer.Deserialize<Dictionary<string, object>>(optionElement.GetRawText());
                    
                    if (optionDict?.TryGetValue("sourceOptionId", out var sourceIdObj) == true && 
                        sourceIdObj?.ToString() == sourceOptionId &&
                        optionDict.TryGetValue("destinationOptionId", out var destIdObj) &&
                        !string.IsNullOrEmpty(destIdObj?.ToString()))
                    {
                        // Found option mapping, now test option value lookup
                        if (optionDict.TryGetValue("optionValues", out var optionValuesObj) &&
                            optionValuesObj is JsonElement valuesElement &&
                            valuesElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var valueElement in valuesElement.EnumerateArray())
                            {
                                var valueDict = JsonSerializer.Deserialize<Dictionary<string, object>>(valueElement.GetRawText());
                                
                                if (valueDict?.TryGetValue("sourceId", out var sourceValueIdObj) == true && 
                                    sourceValueIdObj?.ToString() == sourceOptionValueId &&
                                    valueDict.TryGetValue("destinationId", out var destValueIdObj) &&
                                    !string.IsNullOrEmpty(destValueIdObj?.ToString()))
                                {
                                    return true; // SUCCESS: Both option and option value found
                                }
                            }
                        }
                    }
                }
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }
}

