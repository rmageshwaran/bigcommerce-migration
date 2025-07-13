using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using BigCommerce.Migration.Functions.Extensions;
using BigCommerce.Migration.Functions.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BigCommerce.Migration.Functions;

/// <summary>
/// Main program entry point for Azure Functions
/// Configures host and services for the BigCommerce Migration System
/// </summary>
public class Program
{
    /// <summary>
    /// Main entry point for Azure Functions host
    /// </summary>
    /// <param name="args">Command line arguments</param>
    public static void Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults(workerApplication =>
            {
                // Add middleware pipeline in correct order
                // 1. Global exception handler (outermost - catches all exceptions)
                workerApplication.UseMiddleware<GlobalExceptionHandlerMiddleware>();
                
                // 2. Request validation (validates input before processing)
                workerApplication.UseMiddleware<RequestValidationMiddleware>();
                
                // 3. Authentication (validates API keys and sets user context)
                workerApplication.UseMiddleware<ApiKeyAuthenticationMiddleware>();
            })

            .ConfigureAppConfiguration((context, config) =>
            {
                var env = context.HostingEnvironment;
                
                // Add configuration sources in order of precedence
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                      .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
                      .AddEnvironmentVariables("BIGCOMMERCE_")
                      .AddEnvironmentVariables("OPENSEARCH_");
                
                // Add Azure Key Vault in production
                if (env.IsProduction())
                {
                    var builtConfig = config.Build();
                    var keyVaultUrl = builtConfig["KeyVault:VaultUri"];
                    
                    if (!string.IsNullOrEmpty(keyVaultUrl))
                    {
                        config.AddAzureKeyVault(new Uri(keyVaultUrl), new DefaultAzureCredential());
                    }
                }
            })
            .ConfigureServices((context, services) =>
            {
                // Add Application Insights telemetry
                services.AddApplicationInsightsTelemetryWorkerService();
                services.ConfigureFunctionsApplicationInsights();
                
                // Add SignalR service configuration
                var signalRConnectionString = context.Configuration.GetConnectionString("AzureSignalR");
                if (!string.IsNullOrEmpty(signalRConnectionString))
                {
                    services.AddSignalR().AddAzureSignalR(signalRConnectionString);
                }
                
                // Add BigCommerce Migration services with full dependency injection
                services.AddBigCommerceMigrationServices(context.Configuration);
                
                // Add additional services
                services.AddMemoryCache();
                services.AddOptions();
            })
            .Build();

        host.Run();
    }
} 