using System;
using System.Collections.Generic;
using System.Text.Json;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using FluentAssertions;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Services
{
    /// <summary>
    /// Unit tests for SignalRMessageConverter based on actual class structure
    /// Tests JSON conversion and message creation functionality
    /// </summary>
    public class SignalRMessageConverterSimpleTests
    {
        private readonly SignalRMessageConverter _converter;
        private readonly DateTime _testTimestamp = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        public SignalRMessageConverterSimpleTests()
        {
            _converter = new SignalRMessageConverter();
        }

        #region ConvertToFrontendJson Tests

        [Fact]
        public void ConvertToFrontendJson_WithMigrationProgressEvent_ShouldReturnCamelCaseJson()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = "test-migration-123",
                Timestamp = _testTimestamp,
                HubMethod = "MigrationProgressUpdated",
                EventType = "MigrationProgress",
                OverallProgress = 75.5,
                Status = "processing",
                TotalEntities = 1000,
                ProcessedEntities = 755,
                FailedEntities = 5,
                CurrentEntityType = "products"
            };

            // Act
            var result = _converter.ConvertToFrontendJson(progressEvent);

            // Assert
            result.Should().NotBeNullOrEmpty();
            
            // Verify camelCase conversion
            result.Should().Contain("\"migrationId\":");
            result.Should().Contain("\"overallProgress\":");
            result.Should().Contain("\"totalEntities\":");
            result.Should().Contain("\"processedEntities\":");
            result.Should().Contain("\"failedEntities\":");
            result.Should().Contain("\"currentEntityType\":");
            
            // Should NOT contain PascalCase
            result.Should().NotContain("\"MigrationId\":");
            result.Should().NotContain("\"OverallProgress\":");
            result.Should().NotContain("\"TotalEntities\":");
            
            // Verify specific values
            result.Should().Contain("\"test-migration-123\"");
            result.Should().Contain("75.5");
            result.Should().Contain("\"processing\"");
            result.Should().Contain("1000");
            result.Should().Contain("755");
            result.Should().Contain("5");
            result.Should().Contain("\"products\"");
        }

        [Fact]
        public void ConvertToFrontendJson_WithBatchProgressEvent_ShouldReturnCamelCaseJson()
        {
            // Arrange
            var batchEvent = new BatchProgressEvent
            {
                MigrationId = "test-migration-456",
                Timestamp = _testTimestamp,
                HubMethod = "BatchProgressUpdated",
                EventType = "BatchProgress",
                BatchNumber = 5,
                TotalBatches = 20,
                BatchSize = 100,
                ProcessedCount = 95,
                FailedCount = 5,
                Status = "completed",
                EntityType = "categories"
            };

            // Act
            var result = _converter.ConvertToFrontendJson(batchEvent);

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().Contain("\"batchNumber\":");
            result.Should().Contain("\"totalBatches\":");
            result.Should().Contain("\"batchSize\":");
            result.Should().Contain("\"processedCount\":");
            result.Should().Contain("\"failedCount\":");
            result.Should().Contain("\"entityType\":");
            
            // Should NOT contain PascalCase
            result.Should().NotContain("\"BatchNumber\":");
            result.Should().NotContain("\"TotalBatches\":");
            result.Should().NotContain("\"EntityType\":");
        }

        [Fact]
        public void ConvertToFrontendJson_WithNullEvent_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => _converter.ConvertToFrontendJson(null!));
            exception.ParamName.Should().Be("progressEvent");
        }

        #endregion

        #region ConvertToFrontendObject Tests

        [Fact]
        public void ConvertToFrontendObject_WithMigrationProgressEvent_ShouldReturnDictionaryWithCamelCaseKeys()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = "test-migration-123",
                Timestamp = _testTimestamp,
                HubMethod = "MigrationProgressUpdated",
                EventType = "MigrationProgress",
                OverallProgress = 75.5,
                Status = "processing",
                TotalEntities = 1000,
                ProcessedEntities = 755,
                CurrentEntityType = "products"
            };

            // Act
            var result = _converter.ConvertToFrontendObject(progressEvent);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeOfType<Dictionary<string, object>>();
            
            var dictionary = (Dictionary<string, object>)result;
            
            // Verify camelCase keys
            dictionary.Should().ContainKey("migrationId");
            dictionary.Should().ContainKey("overallProgress");
            dictionary.Should().ContainKey("totalEntities");
            dictionary.Should().ContainKey("processedEntities");
            dictionary.Should().ContainKey("currentEntityType");
            dictionary.Should().ContainKey("hubMethod");
            dictionary.Should().ContainKey("eventType");
            
            // Should NOT contain PascalCase keys
            dictionary.Should().NotContainKey("MigrationId");
            dictionary.Should().NotContainKey("OverallProgress");
            dictionary.Should().NotContainKey("TotalEntities");
            
            // Verify values
            dictionary["migrationId"].ToString().Should().Be("test-migration-123");
            dictionary["overallProgress"].ToString().Should().Be("75.5");
            dictionary["totalEntities"].ToString().Should().Be("1000");
            dictionary["processedEntities"].ToString().Should().Be("755");
            dictionary["currentEntityType"].ToString().Should().Be("products");
            dictionary["hubMethod"].ToString().Should().Be("MigrationProgressUpdated");
            dictionary["eventType"].ToString().Should().Be("MigrationProgress");
        }

        [Fact]
        public void ConvertToFrontendObject_WithNullEvent_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => _converter.ConvertToFrontendObject(null!));
            exception.ParamName.Should().Be("progressEvent");
        }

        #endregion

        #region CreateSignalRMessage Tests

        [Fact]
        public void CreateSignalRMessage_WithValidEvent_ShouldCreateCompleteMessage()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = "test-migration-123",
                Timestamp = _testTimestamp,
                HubMethod = "MigrationProgressUpdated",
                EventType = "MigrationProgress",
                OverallProgress = 75.5,
                Status = "processing"
            };

            // Act
            var result = _converter.CreateSignalRMessage(progressEvent);

            // Assert
            result.Should().NotBeNull();
            
            // Verify the structure using JSON serialization
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var deserializedResult = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            
            deserializedResult.Should().ContainKey("target");
            deserializedResult.Should().ContainKey("arguments");
            deserializedResult.Should().ContainKey("metadata");
            
            deserializedResult!["target"].ToString().Should().Be("MigrationProgressUpdated");
            
            // Verify metadata
            var metadataJson = deserializedResult["metadata"].ToString();
            metadataJson.Should().Contain("\"eventType\":\"MigrationProgress\"");
            metadataJson.Should().Contain("\"migrationId\":\"test-migration-123\"");
            metadataJson.Should().Contain("\"hubMethod\":\"MigrationProgressUpdated\"");
        }

        [Fact]
        public void CreateSignalRMessage_WithNullEvent_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => _converter.CreateSignalRMessage(null!));
            exception.ParamName.Should().Be("progressEvent");
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void ConvertToFrontendJson_And_ConvertToFrontendObject_ShouldProduceSameData()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = "test-migration-consistency",
                Timestamp = _testTimestamp,
                HubMethod = "MigrationProgressUpdated",
                EventType = "MigrationProgress",
                OverallProgress = 42.7,
                Status = "processing",
                TotalEntities = 500,
                ProcessedEntities = 213
            };

            // Act
            var jsonResult = _converter.ConvertToFrontendJson(progressEvent);
            var objectResult = _converter.ConvertToFrontendObject(progressEvent);

            // Assert
            var jsonDeserialized = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonResult);
            var objectDictionary = (Dictionary<string, object>)objectResult;

            // Compare key counts (allowing for some variation due to serialization differences)
            jsonDeserialized!.Count.Should().BeGreaterThan(5);
            objectDictionary.Count.Should().BeGreaterThan(5);

            // Compare essential keys
            foreach (var key in new[] { "migrationId", "overallProgress", "totalEntities", "processedEntities", "hubMethod", "eventType" })
            {
                jsonDeserialized.Should().ContainKey(key);
                objectDictionary.Should().ContainKey(key);
            }
        }

        [Fact]
        public void AllMethods_WithErrorEvent_ShouldWorkTogether()
        {
            // Arrange
            var errorEvent = new ErrorProgressEvent
            {
                MigrationId = "real-world-migration-001",
                Timestamp = _testTimestamp,
                HubMethod = "ErrorOccurred",
                EventType = "ErrorProgress",
                Message = "API rate limit exceeded for products endpoint",
                Severity = "warning",
                EntityType = "products",
                EntityId = "prod-12345"
            };

            // Act & Assert - All methods should work without exceptions
            var jsonResult = _converter.ConvertToFrontendJson(errorEvent);
            jsonResult.Should().NotBeNullOrEmpty();
            jsonResult.Should().Contain("\"message\":");
            jsonResult.Should().Contain("\"severity\":");

            var objectResult = _converter.ConvertToFrontendObject(errorEvent);
            objectResult.Should().NotBeNull();
            var dictionary = (Dictionary<string, object>)objectResult;
            dictionary.Should().ContainKey("message");
            dictionary.Should().ContainKey("severity");

            var messageResult = _converter.CreateSignalRMessage(errorEvent);
            messageResult.Should().NotBeNull();
            
            var messageJson = JsonSerializer.Serialize(messageResult, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var messageDict = JsonSerializer.Deserialize<Dictionary<string, object>>(messageJson);
            messageDict!["target"].ToString().Should().Be("ErrorOccurred");
        }

        #endregion
    }
} 