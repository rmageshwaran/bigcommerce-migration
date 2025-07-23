#pragma warning disable CS8602, CS8604
using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Functions.Functions;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.UnitTests.Functions
{
    /// <summary>
    /// Unit tests for SignalRProgressFunctions
    /// Tests focus on core logic and constructor validation
    /// Azure Functions SignalR runtime behaviors are tested through integration tests
    /// </summary>
    public class SignalRProgressFunctionsTests
    {
        private readonly Mock<ILogger<SignalRProgressFunctions>> _mockLogger;
        private readonly SignalRProgressFunctions _signalRProgressFunctions;

        public SignalRProgressFunctionsTests()
        {
            _mockLogger = new Mock<ILogger<SignalRProgressFunctions>>();
            _signalRProgressFunctions = new SignalRProgressFunctions(_mockLogger.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SignalRProgressFunctions(null!));
        }

        [Fact]
        public void Constructor_WithValidLogger_ShouldCreateInstance()
        {
            // Act
            var functions = new SignalRProgressFunctions(_mockLogger.Object);

            // Assert
            Assert.NotNull(functions);
        }

        #endregion

        #region ProcessProgressEvents Tests - JSON Handling

        [Fact]
        public void ProcessProgressEvents_WithValidMigrationProgressEvent_ShouldProcessSuccessfully()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = "test-migration-123",
                OverallProgress = 75.5,
                Status = "running",
                TotalEntities = 1000,
                ProcessedEntities = 755
            };

            var queueMessage = JsonSerializer.Serialize(progressEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(queueMessage);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("MigrationProgressUpdated", result.Target);
            Assert.NotNull(result.Arguments);
            Assert.Single(result.Arguments);
        }

        [Fact]
        public void ProcessProgressEvents_WithValidBatchProgressEvent_ShouldProcessSuccessfully()
        {
            // Arrange
            var batchEvent = new BatchProgressEvent
            {
                MigrationId = "test-migration-456",
                EntityType = "products",
                BatchNumber = 3,
                TotalBatches = 10,
                Status = "completed"
            };

            var queueMessage = JsonSerializer.Serialize(batchEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(queueMessage);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("BatchProgressUpdated", result.Target);
            Assert.NotNull(result.Arguments);
            Assert.Single(result.Arguments);
        }

        [Fact]
        public void ProcessProgressEvents_WithValidEntityProgressEvent_ShouldProcessSuccessfully()
        {
            // Arrange
            var entityEvent = new EntityProgressEvent
            {
                MigrationId = "test-migration-789",
                EntityType = "categories",
                TotalCount = 100,
                ProcessedCount = 75,
                Status = "processing"
            };

            var queueMessage = JsonSerializer.Serialize(entityEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(queueMessage);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("EntityProgressUpdated", result.Target);
            Assert.NotNull(result.Arguments);
            Assert.Single(result.Arguments);
        }

        [Fact]
        public void ProcessProgressEvents_WithValidErrorProgressEvent_ShouldProcessSuccessfully()
        {
            // Arrange
            var errorEvent = new ErrorProgressEvent
            {
                MigrationId = "test-migration-error",
                Severity = "error",
                Message = "Failed to process entity",
                EntityType = "products",
                IsContinuable = true
            };

            var queueMessage = JsonSerializer.Serialize(errorEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(queueMessage);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("ErrorOccurred", result.Target);
            Assert.NotNull(result.Arguments);
            Assert.Single(result.Arguments);
        }

        [Fact]
        public void ProcessProgressEvents_WithValidStatusProgressEvent_ShouldProcessSuccessfully()
        {
            // Arrange
            var statusEvent = new StatusProgressEvent
            {
                MigrationId = "test-migration-status",
                Status = "completed",
                Message = "Migration completed successfully"
            };

            var queueMessage = JsonSerializer.Serialize(statusEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(queueMessage);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("MigrationStatusChanged", result.Target);
            Assert.NotNull(result.Arguments);
            Assert.Single(result.Arguments);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public void ProcessProgressEvents_WithInvalidJson_ShouldReturnErrorMessage()
        {
            // Arrange
            var invalidJson = "{ invalid json }";

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(invalidJson);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("error", result.Target);
            Assert.NotNull(result.Arguments);
            Assert.Single(result.Arguments);
            Assert.Equal("Invalid JSON", result.Arguments[0]);
        }

        [Fact]
        public void ProcessProgressEvents_WithNullDeserialization_ShouldReturnErrorMessage()
        {
            // Arrange - JSON that deserializes to null
            var nullJson = "null";

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(nullJson);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("error", result.Target);
            Assert.Single(result.Arguments);
            Assert.Equal("Invalid message", result.Arguments[0]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-json")]
        [InlineData("{")]
        [InlineData("}")]
        public void ProcessProgressEvents_WithMalformedJson_ShouldReturnErrorMessage(string malformedJson)
        {
            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(malformedJson);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("error", result.Target);
            Assert.Single(result.Arguments);
            Assert.Equal("Invalid JSON", result.Arguments[0]);
        }

        [Fact]
        public void ProcessProgressEvents_WithInvalidJson_ShouldLogError()
        {
            // Arrange
            var invalidJson = "{ invalid json }";

            // Act
            _signalRProgressFunctions.ProcessProgressEvents(invalidJson);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to parse progress event JSON")),
                    It.IsAny<JsonException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void ProcessProgressEvents_WithNullDeserialization_ShouldLogWarning()
        {
            // Arrange
            var nullJson = "null";

            // Act
            _signalRProgressFunctions.ProcessProgressEvents(nullJson);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to deserialize progress event")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region JSON Serialization Tests

        [Fact]
        public void ProcessProgressEvents_ShouldHandleCamelCaseJsonCorrectly()
        {
            // Arrange - Manually create camelCase JSON to verify proper deserialization
            var camelCaseJson = @"{
                ""migrationId"": ""test-camel-case"",
                ""eventType"": ""progress"",
                ""hubMethod"": ""MigrationProgressUpdated"",
                ""timestamp"": ""2025-01-01T00:00:00Z"",
                ""overallProgress"": 85.5,
                ""status"": ""running"",
                ""totalEntities"": 1200,
                ""processedEntities"": 1026
            }";

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(camelCaseJson);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("MigrationProgressUpdated", result.Target);
            Assert.Single(result.Arguments);
        }

        [Fact]
        public void ProcessProgressEvents_WithLargeValidJson_ShouldProcessCorrectly()
        {
            // Arrange - Create a large but valid progress event
            var largeEvent = new MigrationProgressEvent
            {
                MigrationId = "large-migration-" + new string('x', 1000), // Large migration ID
                Status = "running",
                OverallProgress = 50.0
            };

            var largeJson = JsonSerializer.Serialize(largeEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(largeJson);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("MigrationProgressUpdated", result.Target);
            Assert.Single(result.Arguments);
        }

        #endregion

        #region Logging Tests

        [Fact]
        public void ProcessProgressEvents_WithValidEvent_ShouldLogDebugMessages()
        {
            // Arrange
            var progressEvent = new MigrationProgressEvent
            {
                MigrationId = "test-migration",
                EventType = "progress",
                HubMethod = "MigrationProgressUpdated"
            };

            var queueMessage = JsonSerializer.Serialize(progressEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            _signalRProgressFunctions.ProcessProgressEvents(queueMessage);

            // Assert - Should log processing and broadcasting messages
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing progress event from queue")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Broadcasting progress event via SignalR")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region SOLID Principles Tests

        [Fact]
        public void SignalRProgressFunctions_ShouldFollowSingleResponsibilityPrinciple()
        {
            // Assert - The class should only handle queue-to-SignalR broadcasting
            var type = typeof(SignalRProgressFunctions);
            var methods = type.GetMethods();

            // Should have specific methods for queue processing, negotiation, and connection handling
            Assert.Contains(methods, m => m.Name == "ProcessProgressEvents");
            Assert.Contains(methods, m => m.Name == "GetSignalRInfo");
            Assert.Contains(methods, m => m.Name == "OnConnected");
            Assert.Contains(methods, m => m.Name == "OnDisconnected");
        }

        [Fact]
        public void SignalRProgressFunctions_ShouldDependOnAbstractions()
        {
            // Assert - Constructor should depend on abstractions (ILogger interface)
            var constructor = typeof(SignalRProgressFunctions).GetConstructors()[0];
            var parameters = constructor.GetParameters();

            Assert.Single(parameters);
            Assert.Equal(typeof(ILogger<SignalRProgressFunctions>), parameters[0].ParameterType);
        }

        #endregion

        #region Edge Cases Tests

        [Fact]
        public void ProcessProgressEvents_WithEmptyString_ShouldReturnErrorMessage()
        {
            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(string.Empty);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("error", result.Target);
            Assert.Equal("Invalid JSON", result.Arguments[0]);
        }

        [Fact]
        public void ProcessProgressEvents_WithWhitespaceOnly_ShouldReturnErrorMessage()
        {
            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents("   ");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("error", result.Target);
            Assert.Equal("Invalid JSON", result.Arguments[0]);
        }

        #endregion
    }
} 