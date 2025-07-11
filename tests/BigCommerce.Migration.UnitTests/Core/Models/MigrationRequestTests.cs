using FluentAssertions;
using BigCommerce.Migration.Core.Models;
using Xunit;

namespace BigCommerce.Migration.UnitTests.Core.Models;

/// <summary>
/// Unit tests for MigrationRequest model with new request-based architecture
/// Tests validation and summary generation with nested store configurations
/// </summary>
public class MigrationRequestTests
{
    [Fact]
    public void MigrationRequest_WithValidStoreConfigurations_ShouldBeValid()
    {
        // Arrange
        var request = new MigrationRequest
        {
            SourceStore = new StoreConfiguration
            {
                StoreId = "v6q95r5n91",
                AccessToken = "source-token",
                ChannelId = "1"
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "in2msaitrc",
                AccessToken = "dest-token",
                ChannelId = "2"
            },
            Entities = new List<string> { "categories", "products" }
        };

        // Act
        var isValid = request.IsValid();

        // Assert
        isValid.Should().BeTrue();
        request.SourceStore!.StoreId.Should().Be("v6q95r5n91");
        request.SourceStore!.ChannelId.Should().Be("1");
        request.DestinationStore!.StoreId.Should().Be("in2msaitrc");
        request.DestinationStore!.ChannelId.Should().Be("2");
        request.Entities.Should().Contain("categories");
        request.Entities.Should().Contain("products");
    }

    [Fact]
    public void MigrationRequest_WithNullSourceStore_ShouldNotBeValid()
    {
        // Arrange
        var request = new MigrationRequest
        {
            SourceStore = null,
            DestinationStore = new StoreConfiguration
            {
                StoreId = "in2msaitrc",
                AccessToken = "dest-token",
                ChannelId = "2"
            },
            Entities = new List<string> { "categories" }
        };

        // Act
        var isValid = request.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void MigrationRequest_WithNullDestinationStore_ShouldNotBeValid()
    {
        // Arrange
        var request = new MigrationRequest
        {
            SourceStore = new StoreConfiguration
            {
                StoreId = "v6q95r5n91",
                AccessToken = "source-token",
                ChannelId = "1"
            },
            DestinationStore = null,
            Entities = new List<string> { "categories" }
        };

        // Act
        var isValid = request.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void MigrationRequest_WithEmptyEntities_ShouldNotBeValid()
    {
        // Arrange
        var request = new MigrationRequest
        {
            SourceStore = new StoreConfiguration
            {
                StoreId = "v6q95r5n91",
                AccessToken = "source-token",
                ChannelId = "1"
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "in2msaitrc",
                AccessToken = "dest-token",
                ChannelId = "2"
            },
            Entities = new List<string>()
        };

        // Act
        var isValid = request.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void GetSummary_ShouldReturnFormattedSummary()
    {
        // Arrange
        var request = new MigrationRequest
        {
            SourceStore = new StoreConfiguration
            {
                StoreId = "v6q95r5n91",
                AccessToken = "source-token",
                ChannelId = "1"
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "in2msaitrc",
                AccessToken = "dest-token",
                ChannelId = "2"
            },
            Entities = new List<string> { "categories", "products", "brands" }
        };

        // Act
        var summary = request.GetSummary();

        // Assert
        summary.Should().Be("Migration: v6q95r5n91[1] -> in2msaitrc[2], Entities: categories, products, brands");
    }

    [Fact]
    public void MigrationRequest_WithSettings_ShouldPreserveSettings()
    {
        // Arrange
        var settings = new MigrationSettings
        {
            MaxApiCallsPerSecond = 10,
            EnableAdaptiveBatching = false,
            LogLevel = "DEBUG",
            RequestTimeoutSeconds = 60,
            MaxRetries = 5
        };

        var request = new MigrationRequest
        {
            SourceStore = new StoreConfiguration
            {
                StoreId = "v6q95r5n91",
                AccessToken = "source-token",
                ChannelId = "1"
            },
            DestinationStore = new StoreConfiguration
            {
                StoreId = "in2msaitrc",
                AccessToken = "dest-token",
                ChannelId = "2"
            },
            Entities = new List<string> { "categories" },
            Settings = settings
        };

        // Act & Assert
        request.Settings.Should().NotBeNull();
        request.Settings!.MaxApiCallsPerSecond.Should().Be(10);
        request.Settings.EnableAdaptiveBatching.Should().BeFalse();
        request.Settings.LogLevel.Should().Be("DEBUG");
        request.Settings.RequestTimeoutSeconds.Should().Be(60);
        request.Settings.MaxRetries.Should().Be(5);
    }
} 