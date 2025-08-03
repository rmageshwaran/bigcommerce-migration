#pragma warning disable CS8602, CS8604
using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BigCommerce.Migration.Functions.Functions;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;

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
        private readonly Mock<ISignalRMessageConverter> _mockSignalRMessageConverter;
        private readonly SignalRProgressFunctions _signalRProgressFunctions;

        public SignalRProgressFunctionsTests()
        {
            _mockLogger = new Mock<ILogger<SignalRProgressFunctions>>();
            _mockSignalRMessageConverter = new Mock<ISignalRMessageConverter>();
            _signalRProgressFunctions = new SignalRProgressFunctions(_mockLogger.Object, _mockSignalRMessageConverter.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SignalRProgressFunctions(null!, _mockSignalRMessageConverter.Object));
        }

        [Fact]
        public void Constructor_WithNullSignalRMessageConverter_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SignalRProgressFunctions(_mockLogger.Object, null!));
        }

        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateInstance()
        {
            // Act
            var functions = new SignalRProgressFunctions(_mockLogger.Object, _mockSignalRMessageConverter.Object);

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

        [Fact]
        public void ProcessProgressEvents_WithValidCancellationProgressEvent_ShouldProcessSuccessfully()
        {
            // Arrange
            var cancellationEvent = new CancellationProgressEvent
            {
                MigrationId = "test-migration-cancel",
                Scope = CancellationScope.Migration,
                Status = "propagating",
                Reason = "User requested cancellation",
                EntityType = "products",
                BatchId = "batch-123",
                RequestedBy = "user@test.com"
            };

            var queueMessage = JsonSerializer.Serialize(cancellationEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(queueMessage);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("CancellationProgressUpdated", result.Target);
            Assert.NotNull(result.Arguments);
            Assert.Single(result.Arguments);
        }

        [Fact]
        public void ProcessProgressEvents_WithCancellationProgressEvent_ShouldDeserializeWithoutConflicts()
        {
            // Arrange - Test the exact scenario that was failing in production
            var cancellationJson = @"{
                ""eventType"": ""cancellation-progress"",
                ""scope"": 0,
                ""status"": ""propagating"",
                ""reason"": ""User requested cancellation"",
                ""entityType"": ""brands"",
                ""batchId"": null,
                ""storeId"": null,
                ""estimatedTimeToComplete"": null,
                ""propagatedAt"": ""2025-08-03T10:39:17.2338097Z"",
                ""requestedBy"": ""user@test.com"",
                ""totalInstances"": 2,
                ""acknowledgedInstances"": 1,
                ""additionalContext"": {},
                ""migrationId"": ""test-migration-123"",
                ""timestamp"": ""2025-08-03T10:39:17.2343353Z"",
                ""hubMethod"": ""CancellationProgressUpdated"",
                ""connectionId"": null,
                ""groupName"": null
            }";

            // Act - This should NOT throw or return error message
            var result = _signalRProgressFunctions.ProcessProgressEvents(cancellationJson);

            // Assert - Should process successfully, not return error
            Assert.NotNull(result);
            Assert.Equal("CancellationProgressUpdated", result.Target);
            Assert.NotEqual("error", result.Target); // Should NOT be an error message
            Assert.NotNull(result.Arguments);
            Assert.Single(result.Arguments);
        }

        [Fact]
        public void ProcessProgressEvents_WithCancellationProgressEvent_ShouldNotReturnInvalidMessage()
        {
            // Arrange - Test that this specific event type doesn't trigger "Invalid message" error
            var cancellationEvent = new CancellationProgressEvent
            {
                MigrationId = "test-migration-456",
                Scope = CancellationScope.EntityType,
                Status = "completed",
                Reason = "Entity type cancellation",
                EntityType = "categories"
            };

            var queueMessage = JsonSerializer.Serialize(cancellationEvent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Act
            var result = _signalRProgressFunctions.ProcessProgressEvents(queueMessage);

            // Assert - Should NOT return "Invalid message" error that was happening in production
            Assert.NotNull(result);
            Assert.NotEqual("error", result.Target);
            Assert.DoesNotContain("Invalid message", result.Arguments?.FirstOrDefault()?.ToString() ?? string.Empty);
            Assert.Equal("CancellationProgressUpdated", result.Target);
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
            Assert.Equal("Invalid JSON", result.Arguments[0]);
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
        public void ProcessProgressEvents_WithNullDeserialization_ShouldLogError()
        {
            // Arrange
            var nullJson = "null";

            // Act
            _signalRProgressFunctions.ProcessProgressEvents(nullJson);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to parse progress event JSON")),
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
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("About to broadcast via SignalR")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("About to broadcast via SignalR")),
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
            Assert.Contains(methods, m => m.Name == "GetSignalRConnectionInfo");
            Assert.Contains(methods, m => m.Name == "OnConnected");
            Assert.Contains(methods, m => m.Name == "OnDisconnected");
        }

        [Fact]
        public void SignalRProgressFunctions_ShouldDependOnAbstractions()
        {
            // Assert - Constructor should depend on abstractions (ILogger and ISignalRMessageConverter interfaces)
            var constructor = typeof(SignalRProgressFunctions).GetConstructors()[0];
            var parameters = constructor.GetParameters();

            Assert.Equal(2, parameters.Length);
            Assert.Equal(typeof(ILogger<SignalRProgressFunctions>), parameters[0].ParameterType);
            Assert.Equal(typeof(BigCommerce.Migration.Core.Services.ISignalRMessageConverter), parameters[1].ParameterType);
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