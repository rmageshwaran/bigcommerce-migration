using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using BigCommerce.Migration.Activities.Services;
using BigCommerce.Migration.Core.Models;

namespace BigCommerce.Migration.Tests.Unit.Configuration;

/// <summary>
/// Tests for validating Product-Images phase configuration loading and parsing
/// Task 7.1: Verify Application Settings
/// </summary>
public class ProductImagesConfigurationTests
{
    private readonly IConfiguration _developmentConfig;
    private readonly IConfiguration _productionConfig;
    private readonly IConfiguration _dockerConfig;

    public ProductImagesConfigurationTests()
    {
        // Load actual configuration files for testing
        _developmentConfig = LoadConfiguration("src/BigCommerce.Migration.Functions/appsettings.Development.json");
        _productionConfig = LoadConfiguration("src/BigCommerce.Migration.Functions/Configuration/appsettings.json");
        _dockerConfig = LoadConfiguration("src/BigCommerce.Migration.Functions/local.settings.docker.json");
    }

    [Fact]
    public void DevelopmentConfig_ContainsProductImagesConfiguration()
    {
        // Arrange & Act
        var productImagesConfig = _developmentConfig.GetSection("ParallelProcessing:subBatchConfigurations:product-images");

        // Assert
        Assert.NotNull(productImagesConfig);
        Assert.True(productImagesConfig.Exists(), "product-images configuration section should exist in Development config");
        
        // Verify required properties
        Assert.Equal("product-images", productImagesConfig["entityType"]);
        Assert.Equal("20", productImagesConfig["chunkSize"]);
        Assert.Equal("1", productImagesConfig["subBatchSize"]);
        Assert.Equal("5", productImagesConfig["maxConcurrency"]);
        Assert.Equal("50", productImagesConfig["imageChunkSize"]);
        Assert.Equal("300", productImagesConfig["imageChunkDelayMs"]);
        Assert.Equal("1000", productImagesConfig["maxImagesPerProduct"]);
        Assert.Equal("5", productImagesConfig["cancellationCheckInterval"]);
    }

    [Fact]
    public void ProductionConfig_ContainsProductImagesConfiguration()
    {
        // Arrange & Act
        var productImagesConfig = _productionConfig.GetSection("ParallelProcessing:subBatchConfigurations:product-images");

        // Assert
        Assert.NotNull(productImagesConfig);
        Assert.True(productImagesConfig.Exists(), "product-images configuration section should exist in Production config");
        
        // Verify required properties match development (should be identical for consistency)
        Assert.Equal("product-images", productImagesConfig["entityType"]);
        Assert.Equal("20", productImagesConfig["chunkSize"]);
        Assert.Equal("1", productImagesConfig["subBatchSize"]);
        Assert.Equal("5", productImagesConfig["maxConcurrency"]);
        Assert.Equal("50", productImagesConfig["imageChunkSize"]);
        Assert.Equal("300", productImagesConfig["imageChunkDelayMs"]);
        Assert.Equal("1000", productImagesConfig["maxImagesPerProduct"]);
    }

    [Fact]
    public void DockerConfig_ContainsProductImagesConfiguration()
    {
        // Arrange & Act - Docker config uses standard JSON structure, not environment variable format
        var productImagesConfig = _dockerConfig.GetSection("ParallelProcessing:subBatchConfigurations:product-images");

        // Assert
        Assert.NotNull(productImagesConfig);
        Assert.True(productImagesConfig.Exists(), "product-images configuration section should exist in Docker config");
        
        // Verify required properties
        Assert.Equal("product-images", productImagesConfig["entityType"]);
        Assert.Equal("20", productImagesConfig["chunkSize"]);
        Assert.Equal("1", productImagesConfig["subBatchSize"]);
        Assert.Equal("5", productImagesConfig["maxConcurrency"]);
    }

    [Fact]
    public void SubBatchConfigurationService_LoadsProductImagesConfig()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(_developmentConfig);
        services.AddLogging();
        services.AddScoped<ISubBatchConfigurationService, SubBatchConfigurationService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var configService = serviceProvider.GetRequiredService<ISubBatchConfigurationService>();

        // Act
        var config = configService.GetConfiguration("product-images");

        // Assert
        Assert.NotNull(config);
        Assert.Equal("product-images", config.EntityType);
        Assert.Equal(20, config.ChunkSize);
        Assert.Equal(1, config.SubBatchSize);
        Assert.Equal(5, config.MaxConcurrency);
        Assert.False(config.ProcessSubBatchesSequentially);
        Assert.True(config.EnableSubBatching);

        // Verify custom settings
        Assert.NotNull(config.CustomSettings);
        Assert.True(config.CustomSettings.ContainsKey("imageChunkSize"));
        Assert.True(config.CustomSettings.ContainsKey("imageChunkDelayMs"));
        Assert.True(config.CustomSettings.ContainsKey("maxImagesPerProduct"));
        
        // Verify custom setting values
        Assert.Equal(50, Convert.ToInt32(config.CustomSettings["imageChunkSize"], CultureInfo.InvariantCulture));
        Assert.Equal(300, Convert.ToInt32(config.CustomSettings["imageChunkDelayMs"], CultureInfo.InvariantCulture));
        Assert.Equal(1000, Convert.ToInt32(config.CustomSettings["maxImagesPerProduct"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ProductImagesConfig_HasTimeoutSafeSettings()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(_developmentConfig);
        services.AddLogging();
        services.AddScoped<ISubBatchConfigurationService, SubBatchConfigurationService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var configService = serviceProvider.GetRequiredService<ISubBatchConfigurationService>();
        var config = configService.GetConfiguration("product-images");

        // Act & Assert - Validate timeout-safe calculations
        var maxConcurrency = config.MaxConcurrency;
        var imageChunkSize = Convert.ToInt32(config.CustomSettings["imageChunkSize"], CultureInfo.InvariantCulture);
        var imageChunkDelayMs = Convert.ToInt32(config.CustomSettings["imageChunkDelayMs"], CultureInfo.InvariantCulture);
        var maxImagesPerProduct = Convert.ToInt32(config.CustomSettings["maxImagesPerProduct"], CultureInfo.InvariantCulture);

        // Calculate worst-case scenario timing (from Task 1 design)
        var chunksPerProduct = (int)Math.Ceiling((double)maxImagesPerProduct / imageChunkSize); // 20 chunks for 1000 images
        var delayTimePerProduct = (chunksPerProduct - 1) * imageChunkDelayMs; // 19 * 300ms = 5.7s delays
        var estimatedApiTimePerChunk = 2500; // 2.5s per API call (conservative estimate)
        var apiTimePerProduct = chunksPerProduct * estimatedApiTimePerChunk; // 20 * 2.5s = 50s
        var totalTimePerProduct = (delayTimePerProduct + apiTimePerProduct) / 1000.0; // Convert to seconds

        // Verify timeout safety (worst case should be < 60s, well under 120s Azure Function timeout)
        Assert.True(totalTimePerProduct < 60, $"Worst case time per product ({totalTimePerProduct:F1}s) should be < 60s");
        Assert.True(totalTimePerProduct < 120, $"Worst case time per product ({totalTimePerProduct:F1}s) should be < 120s (Azure Function timeout)");
        
        // Verify our original calculation (55.7s) is still valid
        Assert.True(totalTimePerProduct <= 56);
    }

    [Fact]
    public void ProductImagesConfig_HasMemoryEfficientSettings()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(_developmentConfig);
        services.AddLogging();
        services.AddScoped<ISubBatchConfigurationService, SubBatchConfigurationService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var configService = serviceProvider.GetRequiredService<ISubBatchConfigurationService>();
        var config = configService.GetConfiguration("product-images");

        // Act & Assert - Validate memory-efficient settings
        var subBatchSize = config.SubBatchSize;
        var maxConcurrency = config.MaxConcurrency;
        var imageChunkSize = Convert.ToInt32(config.CustomSettings["imageChunkSize"], CultureInfo.InvariantCulture);

        // Verify individual product processing (subBatchSize = 1)
        Assert.Equal(1, subBatchSize);
        
        // Verify reasonable concurrency (5 concurrent products)
        Assert.Equal(5, maxConcurrency);
        
        // Verify chunk size limits memory per API call (50 images per chunk)
        Assert.Equal(50, imageChunkSize);
        
        // Calculate estimated memory usage per concurrent product
        var estimatedMemoryPerImage = 1024; // 1KB per image metadata (conservative)
        var memoryPerChunk = imageChunkSize * estimatedMemoryPerImage; // 50KB per chunk
        var memoryPerProduct = memoryPerChunk; // Only one chunk in memory at a time due to streaming
        var totalMemoryForConcurrency = memoryPerProduct * maxConcurrency; // 250KB total

        // Verify memory usage stays well under 1MB per concurrent product
        Assert.True(memoryPerProduct < 1024 * 1024, $"Memory per product ({memoryPerProduct} bytes) should be < 1MB");
        Assert.True(totalMemoryForConcurrency < 5 * 1024 * 1024, $"Total memory for concurrency ({totalMemoryForConcurrency} bytes) should be < 5MB");
    }

    [Theory]
    [InlineData("src/BigCommerce.Migration.Functions/appsettings.json")]
    [InlineData("src/BigCommerce.Migration.Functions/appsettings.Development.json")]
    [InlineData("src/BigCommerce.Migration.Functions/Configuration/appsettings.json")]
    public void AllConfigurationFiles_ExistAndAreValid(string configFilePath)
    {
        // Arrange & Act
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", configFilePath);
        var fileExists = File.Exists(fullPath);

        // Assert
        Assert.True(fileExists, $"Configuration file should exist at: {fullPath}");

        if (fileExists)
        {
            var jsonContent = File.ReadAllText(fullPath);
            Assert.False(string.IsNullOrWhiteSpace(jsonContent), "Configuration file should not be empty");
            
            // Verify it's valid JSON
            try
            {
                JsonDocument.Parse(jsonContent);
            }
            catch (JsonException ex)
            {
                Assert.Fail($"Configuration file should contain valid JSON: {ex.Message}");
            }
        }
    }

    [Fact]
    public void ProductImagesConfig_HasConsistentSettingsAcrossEnvironments()
    {
        // Arrange & Act
        var devConfig = _developmentConfig.GetSection("ParallelProcessing:subBatchConfigurations:product-images");
        var prodConfig = _productionConfig.GetSection("ParallelProcessing:subBatchConfigurations:product-images");

        // Assert - Critical settings should be identical across environments
        Assert.Equal(devConfig["entityType"], prodConfig["entityType"]);
        Assert.Equal(devConfig["subBatchSize"], prodConfig["subBatchSize"]);
        Assert.Equal(devConfig["maxConcurrency"], prodConfig["maxConcurrency"]);
        Assert.Equal(devConfig["imageChunkSize"], prodConfig["imageChunkSize"]);
        Assert.Equal(devConfig["imageChunkDelayMs"], prodConfig["imageChunkDelayMs"]);
        Assert.Equal(devConfig["maxImagesPerProduct"], prodConfig["maxImagesPerProduct"]);
    }

    #region Helper Methods

    private static IConfiguration LoadConfiguration(string relativePath)
    {
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", relativePath);
        
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Configuration file not found at: {fullPath}");
        }

        var builder = new ConfigurationBuilder()
            .AddJsonFile(fullPath, optional: false, reloadOnChange: false);

        return builder.Build();
    }

    #endregion
}
