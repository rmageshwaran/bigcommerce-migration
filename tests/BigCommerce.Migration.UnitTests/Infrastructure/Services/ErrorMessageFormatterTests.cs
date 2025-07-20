using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services;

/// <summary>
/// Tests for ErrorMessageFormatter following TDD principles
/// Tests cover performance, edge cases, and SOLID compliance
/// </summary>
public class ErrorMessageFormatterTests
{
    private readonly Mock<ILogger<ErrorMessageFormatter>> _loggerMock;
    private readonly IErrorMessageFormatter _formatter;

    public ErrorMessageFormatterTests()
    {
        _loggerMock = new Mock<ILogger<ErrorMessageFormatter>>();
        _formatter = new ErrorMessageFormatter(_loggerMock.Object);
    }

    [Fact]
    public void CreateSimpleErrorMessage_WithNullDetailedMessage_ShouldReturnDefaultMessage()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "123";

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, null);

        // Assert
        result.Should().Be("Failed to create categories 123: No entity returned");
    }

    [Fact]
    public void CreateSimpleErrorMessage_WithEmptyDetailedMessage_ShouldReturnDefaultMessage()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "123";

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, "");

        // Assert
        result.Should().Be("Failed to create categories 123: No entity returned");
    }

    [Fact]
    public void CreateSimpleErrorMessage_WithBigCommerceApiError_ShouldExtractSpecificError()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "4486";
        var detailedError = @"API Error creating categories 4486: API request failed with status UnprocessableEntity for https://api.bigcommerce.com/stores/in2msaitrc/v3/catalog/trees/categories: {""data"":[],""errors"":{""status"":422,""title"":""Please verify if all required fields are present in the request body and are filled with values correctly."",""type"":""https://developer.bigcommerce.com/api-docs/getting-started/api-status-codes"",""errors"":{""0.parent_id"":""A parent category with the id: 4487 was not found"",""0.tree_id"":""Tree Id is required and can't be empty""}},""meta"":{""total"":1,""success"":0,""failed"":1}}";

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, detailedError);

        // Assert
        result.Should().Be("Failed to create categories 4486: A parent category with the id: 4487 was not found");
    }

    [Fact]
    public void CreateSimpleErrorMessage_WithDuplicateCategoryError_ShouldExtractSpecificError()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "4487";
        var detailedError = @"API Error creating categories 4487: API request failed with status UnprocessableEntity for https://api.bigcommerce.com/stores/in2msaitrc/v3/catalog/trees/categories: {""data"":[],""errors"":{""status"":422,""title"":""Please verify if all required fields are present in the request body and are filled with values correctly."",""type"":""https://developer.bigcommerce.com/api-docs/getting-started/api-status-codes"",""errors"":{""0.name"":""A duplicate category with the name: 'Mens' was found in the same parent""}},""meta"":{""total"":1,""success"":0,""failed"":1}}";

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, detailedError);

        // Assert
        result.Should().Be("Failed to create categories 4487: A duplicate category with the name: 'Mens' was found in the same parent");
    }

    [Fact]
    public void CreateSimpleErrorMessage_WithInvalidJson_ShouldReturnStatusCode()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "123";
        var detailedError = @"API Error creating categories 123: API request failed with status UnprocessableEntity for https://api.bigcommerce.com/stores/in2msaitrc/v3/catalog/trees/categories: invalid json";

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, detailedError);

        // Assert
        result.Should().Be("Failed to create categories 123: API returned UnprocessableEntity");
    }

    [Fact]
    public void CreateSimpleErrorMessage_WithLongMessage_ShouldTruncate()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "123";
        var longMessage = new string('x', 200); // 200 characters

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, longMessage);

        // Assert
        result.Should().HaveLength(103); // "Failed to create categories 123: " + 100 chars + "..."
        result.Should().EndWith("...");
    }

    [Fact]
    public void CreateSimpleErrorMessage_WithNonApiError_ShouldReturnTruncatedMessage()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "123";
        var simpleError = "Some other error occurred";

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, simpleError);

        // Assert
        result.Should().Be("Failed to create categories 123: Some other error occurred");
    }

    [Fact]
    public void CreateDetailedErrorMessage_ShouldReturnFullMessage()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "123";
        var detailedError = "API request failed with status 422";

        // Act
        var result = _formatter.CreateDetailedErrorMessage(entityType, entityId, detailedError);

        // Assert
        result.Should().Be("API Error creating categories 123: API request failed with status 422");
    }

    [Fact]
    public void CreateDetailedErrorMessage_WithNullMessage_ShouldReturnDefaultMessage()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "123";

        // Act
        var result = _formatter.CreateDetailedErrorMessage(entityType, entityId, null);

        // Assert
        result.Should().Be("Failed to create categories 123: No entity returned");
    }

    [Theory]
    [InlineData("categories", "123")]
    [InlineData("products", "456")]
    [InlineData("brands", "789")]
    [InlineData("variants", "999")]
    [InlineData("images", "111")]
    [InlineData("modifiers", "222")]
    public void CreateSimpleErrorMessage_WithDifferentEntityTypes_ShouldFormatCorrectly(string entityType, string entityId)
    {
        // Arrange
        var detailedError = "Some error occurred";

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, detailedError);

        // Assert
        result.Should().Be($"Failed to create {entityType} {entityId}: Some error occurred");
    }

    [Fact]
    public void CreateSimpleErrorMessage_WithCaching_ShouldReturnCachedResult()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "123";
        var detailedError = "API request failed with status 422";

        // Act - Call twice with same parameters
        var result1 = _formatter.CreateSimpleErrorMessage(entityType, entityId, detailedError);
        var result2 = _formatter.CreateSimpleErrorMessage(entityType, entityId, detailedError);

        // Assert - Both should be identical (cached)
        result1.Should().Be(result2);
        result1.Should().Be("Failed to create categories 123: API returned 422");
    }

    [Fact]
    public void CreateSimpleErrorMessage_WithJsonErrorsObject_ShouldExtractTitle()
    {
        // Arrange
        var entityType = "categories";
        var entityId = "123";
        var detailedError = @"API Error creating categories 123: API request failed with status UnprocessableEntity for https://api.bigcommerce.com/stores/in2msaitrc/v3/catalog/trees/categories: {""data"":[],""errors"":{""status"":422,""title"":""Please verify if all required fields are present in the request body and are filled with values correctly."",""type"":""https://developer.bigcommerce.com/api-docs/getting-started/api-status-codes""},""meta"":{""total"":1,""success"":0,""failed"":1}}";

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, detailedError);

        // Assert
        result.Should().Be("Failed to create categories 123: Please verify if all required fields are present in the request body and are filled with values correctly.");
    }

    [Theory]
    [InlineData("products", "456", "HTTP request failed with status 400")]
    [InlineData("brands", "789", "Request failed with status 409")]
    [InlineData("variants", "999", "Status code: 422")]
    public void CreateSimpleErrorMessage_WithDifferentErrorFormats_ShouldExtractStatusCode(string entityType, string entityId, string errorFormat)
    {
        // Arrange
        var detailedError = $"{entityType} creation failed: {errorFormat}";

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, detailedError);

        // Assert
        result.Should().Contain("API returned");
        result.Should().Contain(entityType);
        result.Should().Contain(entityId);
    }

    [Theory]
    [InlineData("products", "456", @"{""error"":{""message"":""Product name is required""}}")]
    [InlineData("brands", "789", @"{""message"":""Brand already exists""}}")]
    [InlineData("variants", "999", @"{""validationErrors"":[""SKU is required""]}")]
    public void CreateSimpleErrorMessage_WithDifferentJsonStructures_ShouldExtractError(string entityType, string entityId, string jsonError)
    {
        // Arrange
        var detailedError = $"API Error creating {entityType} {entityId}: API request failed with status 422: {jsonError}";

        // Act
        var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, detailedError);

        // Assert
        result.Should().Contain(entityType);
        result.Should().Contain(entityId);
        result.Should().NotContain("API Error creating"); // Should extract the specific error
    }

    [Fact]
    public void CreateSimpleErrorMessage_WithGenericErrorPatterns_ShouldWorkForAllEntities()
    {
        // Arrange
        var entities = new[] { "categories", "products", "brands", "variants", "images", "modifiers" };
        var errorPatterns = new[]
        {
            "API Error creating",
            "HTTP request failed",
            "Request failed",
            "Error creating"
        };

        foreach (var entityType in entities)
        {
            foreach (var pattern in errorPatterns)
            {
                var entityId = "123";
                var detailedError = $"{pattern} {entityType} {entityId}: Some specific error message";

                // Act
                var result = _formatter.CreateSimpleErrorMessage(entityType, entityId, detailedError);

                // Assert
                result.Should().Contain(entityType);
                result.Should().Contain(entityId);
                result.Should().NotContain(pattern); // Should extract the actual error
            }
        }
    }
} 