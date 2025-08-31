using BigCommerce.Migration.Activities.Strategies.Transform;
using BigCommerce.Migration.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.Tests.Transform;

/// <summary>
/// xUnit unit tests for ProductMetafieldsTransformStrategy
/// Follows TDD approach with comprehensive coverage of transformation scenarios
/// Tests field validation, null cleanup, and error handling patterns
/// </summary>
public class ProductMetafieldsTransformStrategyTests
{
    private readonly Mock<ILogger<ProductMetafieldsTransformStrategy>> _mockLogger;
    private readonly ProductMetafieldsTransformStrategy _strategy;

    public ProductMetafieldsTransformStrategyTests()
    {
        _mockLogger = new Mock<ILogger<ProductMetafieldsTransformStrategy>>();
        _strategy = new ProductMetafieldsTransformStrategy(_mockLogger.Object);
    }

    /// <summary>
    /// Test: Valid metafield should be transformed correctly with all fields preserved
    /// </summary>
    [Fact]
    public async Task TransformEntityAsync_ValidMetafield_TransformsCorrectly()
    {
        // Arrange
        var sourceMetafield = CreateValidSourceMetafield();
        var migrationId = "test-migration-001";
        var sourceStore = CreateValidStoreConfiguration();
        var destinationStore = CreateValidStoreConfiguration();

        // Act
        var result = await _strategy.TransformEntityAsync(
            sourceMetafield, migrationId, sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("product-metafields", _strategy.EntityType);
        
        // Verify all required fields are preserved
        Assert.True(result.ContainsKey("resource_id"));
        Assert.True(result.ContainsKey("key"));
        Assert.True(result.ContainsKey("value"));
        Assert.True(result.ContainsKey("permission_set"));
        Assert.True(result.ContainsKey("namespace"));
        Assert.True(result.ContainsKey("description"));
        
        Assert.Equal(42, result["resource_id"]);
        Assert.Equal("Staff Name", result["key"]);
        Assert.Equal("Ronaldo", result["value"]);
        Assert.Equal("app_only", result["permission_set"]);
        Assert.Equal("Sales Department", result["namespace"]);
        Assert.Equal("Name of Staff Member", result["description"]);
    }

    /// <summary>
    /// Test: Null values should be removed from transformed entity
    /// </summary>
    [Fact]
    public async Task TransformEntityAsync_WithNullValues_RemovesNullFields()
    {
        // Arrange
        var sourceMetafield = CreateValidSourceMetafield();
        sourceMetafield["optional_field"] = null;
        sourceMetafield["another_null_field"] = null;
        sourceMetafield["empty_string"] = ""; // Should be kept - empty string is valid
        
        var migrationId = "test-migration-002";
        var sourceStore = CreateValidStoreConfiguration();
        var destinationStore = CreateValidStoreConfiguration();

        // Act
        var result = await _strategy.TransformEntityAsync(
            sourceMetafield, migrationId, sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        
        // Verify null fields were removed
        Assert.False(result.ContainsKey("optional_field"));
        Assert.False(result.ContainsKey("another_null_field"));
        
        // Verify empty string was preserved (valid value)
        Assert.True(result.ContainsKey("empty_string"));
        Assert.Equal("", result["empty_string"]);
        
        // Verify all required fields still exist
        Assert.True(result.ContainsKey("resource_id"));
        Assert.True(result.ContainsKey("key"));
        Assert.True(result.ContainsKey("value"));
        Assert.True(result.ContainsKey("permission_set"));
        Assert.True(result.ContainsKey("namespace"));
        Assert.True(result.ContainsKey("description"));
    }

    /// <summary>
    /// Test: Missing required fields should throw InvalidOperationException
    /// </summary>
    [Theory]
    [InlineData("permission_set")]
    [InlineData("namespace")]
    [InlineData("key")]
    [InlineData("value")]
    [InlineData("description")]
    [InlineData("resource_id")]
    public async Task TransformEntityAsync_MissingRequiredField_ThrowsInvalidOperationException(string missingField)
    {
        // Arrange
        var sourceMetafield = CreateValidSourceMetafield();
        sourceMetafield.Remove(missingField); // Remove the required field
        
        var migrationId = "test-migration-003";
        var sourceStore = CreateValidStoreConfiguration();
        var destinationStore = CreateValidStoreConfiguration();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _strategy.TransformEntityAsync(sourceMetafield, migrationId, sourceStore, destinationStore, null, CancellationToken.None));
        
        Assert.Contains(missingField, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing required fields", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: Null or whitespace required field values should throw InvalidOperationException
    /// </summary>
    [Theory]
    [InlineData("permission_set", null)]
    [InlineData("permission_set", "")]
    [InlineData("permission_set", "   ")]
    [InlineData("namespace", null)]
    [InlineData("namespace", "")]
    [InlineData("namespace", "   ")]
    [InlineData("key", null)]
    [InlineData("key", "")]
    [InlineData("key", "   ")]
    [InlineData("value", null)]
    [InlineData("value", "")]
    [InlineData("value", "   ")]
    [InlineData("description", null)]
    [InlineData("description", "")]
    [InlineData("description", "   ")]
    public async Task TransformEntityAsync_InvalidRequiredFieldValue_ThrowsInvalidOperationException(string fieldName, string? invalidValue)
    {
        // Arrange
        var sourceMetafield = CreateValidSourceMetafield();
        sourceMetafield[fieldName] = invalidValue; // Set invalid value
        
        var migrationId = "test-migration-004";
        var sourceStore = CreateValidStoreConfiguration();
        var destinationStore = CreateValidStoreConfiguration();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _strategy.TransformEntityAsync(sourceMetafield, migrationId, sourceStore, destinationStore, null, CancellationToken.None));
        
        Assert.Contains(fieldName, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing required fields", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test: Null entity should throw ArgumentNullException
    /// </summary>
    [Fact]
    public async Task TransformEntityAsync_NullEntity_ThrowsArgumentNullException()
    {
        // Arrange
        var migrationId = "test-migration-005";
        var sourceStore = CreateValidStoreConfiguration();
        var destinationStore = CreateValidStoreConfiguration();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _strategy.TransformEntityAsync(null!, migrationId, sourceStore, destinationStore, null, CancellationToken.None));
    }

    /// <summary>
    /// Test: Cancellation should be handled gracefully
    /// </summary>
    [Fact]
    public async Task TransformEntityAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var sourceMetafield = CreateValidSourceMetafield();
        var migrationId = "test-migration-006";
        var sourceStore = CreateValidStoreConfiguration();
        var destinationStore = CreateValidStoreConfiguration();
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel(); // Pre-cancelled token

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _strategy.TransformEntityAsync(sourceMetafield, migrationId, sourceStore, destinationStore, null, cancellationTokenSource.Token));
    }

    /// <summary>
    /// Test: Entity type should return correct value
    /// </summary>
    [Fact]
    public void EntityType_ReturnsCorrectValue()
    {
        // Act & Assert
        Assert.Equal("product-metafields", _strategy.EntityType);
    }

    /// <summary>
    /// Test: Complex metafield with all optional fields should be preserved correctly
    /// </summary>
    [Fact]
    public async Task TransformEntityAsync_ComplexMetafield_PreservesAllFields()
    {
        // Arrange
        var sourceMetafield = new Dictionary<string, object>
        {
            ["id"] = 123,
            ["resource_id"] = 42,
            ["key"] = "Staff Name",
            ["value"] = "Ronaldo",
            ["namespace"] = "Sales Department",
            ["description"] = "Name of Staff Member",
            ["permission_set"] = "app_only",
            ["resource_type"] = "product",
            ["date_created"] = "2022-06-16T18:39:00+00:00",
            ["date_modified"] = "2022-06-16T18:39:00+00:00",
            ["custom_field_1"] = "custom_value_1",
            ["custom_field_2"] = 999
        };
        
        var migrationId = "test-migration-007";
        var sourceStore = CreateValidStoreConfiguration();
        var destinationStore = CreateValidStoreConfiguration();

        // Act
        var result = await _strategy.TransformEntityAsync(
            sourceMetafield, migrationId, sourceStore, destinationStore, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sourceMetafield.Count, result.Count); // No fields lost
        
        // Verify all fields are preserved with same values
        foreach (var kvp in sourceMetafield)
        {
            Assert.True(result.ContainsKey(kvp.Key), $"Field {kvp.Key} should be preserved");
            Assert.Equal(kvp.Value, result[kvp.Key]);
        }
    }

    #region Helper Methods

    /// <summary>
    /// Creates a valid source metafield for testing
    /// </summary>
    private Dictionary<string, object> CreateValidSourceMetafield()
    {
        return new Dictionary<string, object>
        {
            ["id"] = 123,
            ["resource_id"] = 42,
            ["key"] = "Staff Name",
            ["value"] = "Ronaldo",
            ["permission_set"] = "app_only",
            ["namespace"] = "Sales Department", 
            ["description"] = "Name of Staff Member",
            ["resource_type"] = "product",
            ["date_created"] = "2022-06-16T18:39:00+00:00",
            ["date_modified"] = "2022-06-16T18:39:00+00:00"
        };
    }

    /// <summary>
    /// Creates a valid store configuration for testing
    /// </summary>
    private StoreConfiguration CreateValidStoreConfiguration()
    {
        return new StoreConfiguration
        {
            StoreId = "test-store-123",
            AccessToken = "test-api-token",
            BaseUrl = "https://api.bigcommerce.com",
            ChannelId = "1"
        };
    }

    #endregion
}
