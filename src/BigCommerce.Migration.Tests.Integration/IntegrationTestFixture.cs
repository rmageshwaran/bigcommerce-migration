using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Infrastructure.Services;

namespace BigCommerce.Migration.Tests.Integration
{
    /// <summary>
    /// Test fixture for Phase 7 Integration Tests
    /// Provides dependency injection container and test infrastructure
    /// </summary>
    public class IntegrationTestFixture : IDisposable
    {
        public IServiceProvider ServiceProvider { get; private set; }
        public IConfiguration Configuration { get; private set; }

        public IntegrationTestFixture()
        {
            ServiceProvider = CreateTestServiceProvider();
            Configuration = ServiceProvider.GetRequiredService<IConfiguration>();
        }

        private static IServiceProvider CreateTestServiceProvider()
        {
            var services = new ServiceCollection();
            
            // Add test configuration
            var configDict = new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
                ["Logging:LogLevel:Default"] = "Debug",
                ["Logging:LogLevel:BigCommerce"] = "Information"
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
            
            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            // Add core services needed for RowNumber testing
            services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
            services.AddSingleton<IAzureTableInitializationService, AzureTableInitializationService>();
            services.AddSingleton<IRowNumberService, RowNumberService>();
            services.AddScoped<IMigrationStorageService, MigrationStorageService>();
            services.AddScoped<IEntityMappingsPaginationService, EntityMappingsPaginationService>();

            return services.BuildServiceProvider();
        }

        public void Dispose()
        {
            if (ServiceProvider is IDisposable disposableServiceProvider)
            {
                disposableServiceProvider.Dispose();
            }
        }
    }
}
