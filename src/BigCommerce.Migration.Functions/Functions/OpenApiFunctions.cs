using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using System.Net;
using System.Text.Json;
using BigCommerce.Migration.Functions.Services;

namespace BigCommerce.Migration.Functions.Functions;

/// <summary>
/// Azure Functions for serving OpenAPI documentation and Swagger UI
/// </summary>
public class OpenApiFunctions
{
    private readonly ILogger<OpenApiFunctions> _logger;

    public OpenApiFunctions(ILogger<OpenApiFunctions> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Serves the OpenAPI specification document
    /// </summary>
    [Function("GetOpenApiDocument")]
    [OpenApiOperation(operationId: "GetOpenApiDocument", tags: new[] { "System" }, 
        Summary = "Get OpenAPI Specification", 
        Description = "Returns the complete OpenAPI 3.0 specification for the BigCommerce Migration API")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", 
        bodyType: typeof(object), Description = "OpenAPI specification document")]
    public async Task<HttpResponseData> GetOpenApiDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "openapi.json")] HttpRequestData req,
        FunctionContext executionContext)
    {
        _logger.LogInformation("Serving OpenAPI specification document");

        try
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

            // Generate OpenAPI document
            var openApiDocument = GenerateOpenApiDocument();
            
            await response.WriteStringAsync(JsonSerializer.Serialize(openApiDocument, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating OpenAPI document");
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            errorResponse.Headers.Add("Content-Type", "application/json");
            
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "OPENAPI_GENERATION_ERROR",
                    message = "Failed to generate OpenAPI specification",
                    timestamp = DateTime.UtcNow
                }
            }));

            return errorResponse;
        }
    }

    /// <summary>
    /// Serves the Swagger UI interface
    /// </summary>
    [Function("GetSwaggerUI")]
    [OpenApiOperation(operationId: "GetSwaggerUI", tags: new[] { "System" },
        Summary = "Get Swagger UI",
        Description = "Returns the interactive Swagger UI for exploring the API")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/html",
        bodyType: typeof(string), Description = "Swagger UI HTML page")]
    public async Task<HttpResponseData> GetSwaggerUI(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "swagger")] HttpRequestData req,
        FunctionContext executionContext)
    {
        _logger.LogInformation("Serving Swagger UI");

        try
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/html");

            var swaggerHtml = GenerateSwaggerHtml(req);
            await response.WriteStringAsync(swaggerHtml);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving Swagger UI");
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            errorResponse.Headers.Add("Content-Type", "text/html");
            
            await errorResponse.WriteStringAsync("<h1>Error loading Swagger UI</h1><p>Please try again later.</p>");
            return errorResponse;
        }
    }

    /// <summary>
    /// Serves API documentation landing page
    /// </summary>
    [Function("GetApiDocs")]
    [OpenApiOperation(operationId: "GetApiDocs", tags: new[] { "System" },
        Summary = "Get API Documentation",
        Description = "Returns the main API documentation page with links to Swagger UI and other resources")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/html",
        bodyType: typeof(string), Description = "API documentation landing page")]
    public async Task<HttpResponseData> GetApiDocs(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "docs")] HttpRequestData req,
        FunctionContext executionContext)
    {
        _logger.LogInformation("Serving API documentation landing page");

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html");

        var docsHtml = GenerateDocumentationHtml(req);
        await response.WriteStringAsync(docsHtml);

        return response;
    }

    /// <summary>
    /// Gets API information and health status
    /// </summary>
    [Function("GetApiInfo")]
    [OpenApiOperation(operationId: "GetApiInfo", tags: new[] { "System" },
        Summary = "Get API Information",
        Description = "Returns basic information about the API including version, description, and available endpoints")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json",
        bodyType: typeof(object), Description = "API information")]
    public async Task<HttpResponseData> GetApiInfo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "info")] HttpRequestData req,
        FunctionContext executionContext)
    {
        _logger.LogInformation("Serving API information");

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        var apiInfo = new
        {
            name = "BigCommerce Migration API",
            version = "1.0.0",
            description = "Enterprise BigCommerce Migration System",
            documentation = new
            {
                swagger = GetBaseUrl(req) + "/swagger",
                openapi = GetBaseUrl(req) + "/openapi.json",
                docs = GetBaseUrl(req) + "/docs"
            },
            endpoints = new
            {
                migrations = GetBaseUrl(req) + "/migrations",
                dashboard = GetBaseUrl(req) + "/dashboard",
                auth = GetBaseUrl(req) + "/auth",
                health = GetBaseUrl(req) + "/health"
            },
            support = new
            {
                email = "support@bigcommerce-migration.com",
                documentation = "https://docs.bigcommerce-migration.com"
            },
            timestamp = DateTime.UtcNow
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(apiInfo, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));

        return response;
    }

    /// <summary>
    /// Generates the complete OpenAPI document
    /// </summary>
    private OpenApiDocument GenerateOpenApiDocument()
    {
        var document = new OpenApiDocument
        {
            Info = OpenApiConfigurationService.GetApiInfo(),
            Servers = OpenApiConfigurationService.GetServers(),
            Paths = new OpenApiPaths(),
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>
                {
                    {
                        "ApiKey",
                        new OpenApiSecurityScheme
                        {
                            Type = SecuritySchemeType.ApiKey,
                            In = ParameterLocation.Header,
                            Name = "X-API-Key",
                            Description = "API Key authentication"
                        }
                    }
                },
                Schemas = GetApiSchemas()
            },
            Tags = new List<OpenApiTag>
            {
                new OpenApiTag { Name = "Authentication", Description = "API key management and authentication" },
                new OpenApiTag { Name = "Migrations", Description = "Migration management operations" },
                new OpenApiTag { Name = "Monitoring", Description = "Real-time monitoring and progress tracking" },
                new OpenApiTag { Name = "Dashboard", Description = "Dashboard data and statistics" },
                new OpenApiTag { Name = "System", Description = "System information and health checks" }
            }
        };

        // Add security requirement
        document.SecurityRequirements = new List<OpenApiSecurityRequirement>
        {
            new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "ApiKey"
                        }
                    },
                    Array.Empty<string>()
                }
            }
        };

        return document;
    }

    /// <summary>
    /// Gets API schemas for common models
    /// </summary>
    private Dictionary<string, OpenApiSchema> GetApiSchemas()
    {
        return new Dictionary<string, OpenApiSchema>
        {
            {
                "ErrorResponse",
                new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>
                    {
                        { "error", new OpenApiSchema { Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = "ErrorDetails" } } }
                    }
                }
            },
            {
                "ErrorDetails",
                new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>
                    {
                        { "code", new OpenApiSchema { Type = "string", Description = "Error code" } },
                        { "message", new OpenApiSchema { Type = "string", Description = "Error message" } },
                        { "details", new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = "ValidationError" } } } },
                        { "timestamp", new OpenApiSchema { Type = "string", Format = "date-time", Description = "Error timestamp" } },
                        { "requestId", new OpenApiSchema { Type = "string", Description = "Request identifier" } },
                        { "documentation", new OpenApiSchema { Type = "string", Format = "uri", Description = "Documentation URL" } }
                    }
                }
            },
            {
                "ValidationError",
                new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>
                    {
                        { "field", new OpenApiSchema { Type = "string", Description = "Field name" } },
                        { "message", new OpenApiSchema { Type = "string", Description = "Validation error message" } },
                        { "code", new OpenApiSchema { Type = "string", Description = "Validation error code" } }
                    }
                }
            },
            {
                "ApiKeyRole",
                new OpenApiSchema
                {
                    Type = "string",
                    Enum = new List<Microsoft.OpenApi.Any.IOpenApiAny>
                    {
                        new Microsoft.OpenApi.Any.OpenApiString("ReadOnly"),
                        new Microsoft.OpenApi.Any.OpenApiString("User"),
                        new Microsoft.OpenApi.Any.OpenApiString("Admin")
                    },
                    Description = "API key role with different permission levels"
                }
            }
        };
    }

    /// <summary>
    /// Generates Swagger UI HTML
    /// </summary>
    private string GenerateSwaggerHtml(HttpRequestData req)
    {
        var baseUrl = GetBaseUrl(req);
        
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>BigCommerce Migration API - Swagger UI</title>
    <link rel=""stylesheet"" type=""text/css"" href=""https://unpkg.com/swagger-ui-dist@5.10.5/swagger-ui.css"" />
    <link rel=""icon"" type=""image/png"" href=""https://unpkg.com/swagger-ui-dist@5.10.5/favicon-32x32.png"" sizes=""32x32"" />
    <style>
        html {{ box-sizing: border-box; overflow: -moz-scrollbars-vertical; overflow-y: scroll; }}
        *, *:before, *:after {{ box-sizing: inherit; }}
        body {{ margin:0; background: #fafafa; }}
        .swagger-ui .topbar {{ background-color: #2196F3; }}
        .swagger-ui .topbar .download-url-wrapper {{ display: none; }}
    </style>
</head>
<body>
    <div id=""swagger-ui""></div>
    <script src=""https://unpkg.com/swagger-ui-dist@5.10.5/swagger-ui-bundle.js""></script>
    <script src=""https://unpkg.com/swagger-ui-dist@5.10.5/swagger-ui-standalone-preset.js""></script>
    <script>
    window.onload = function() {{
        const ui = SwaggerUIBundle({{
            url: '{baseUrl}/openapi.json',
            dom_id: '#swagger-ui',
            deepLinking: true,
            presets: [
                SwaggerUIBundle.presets.apis,
                SwaggerUIStandalonePreset
            ],
            plugins: [
                SwaggerUIBundle.plugins.DownloadUrl
            ],
            layout: ""StandaloneLayout"",
            defaultModelsExpandDepth: 1,
            defaultModelExpandDepth: 1,
            docExpansion: ""list"",
            filter: true,
            showExtensions: true,
            showCommonExtensions: true,
            tryItOutEnabled: true,
            requestInterceptor: function(request) {{
                // Add request ID header for tracking
                request.headers['X-Request-ID'] = 'swagger-' + Date.now() + '-' + Math.random().toString(36).substr(2, 9);
                return request;
            }}
        }});
    }};
    </script>
</body>
</html>";
    }

    /// <summary>
    /// Generates documentation landing page HTML
    /// </summary>
    private string GenerateDocumentationHtml(HttpRequestData req)
    {
        var baseUrl = GetBaseUrl(req);
        
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>BigCommerce Migration API Documentation</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 0; padding: 40px; background: #f5f5f5; }}
        .container {{ max-width: 800px; margin: 0 auto; background: white; padding: 40px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        h1 {{ color: #2196F3; margin-bottom: 10px; }}
        .subtitle {{ color: #666; margin-bottom: 30px; }}
        .section {{ margin-bottom: 30px; }}
        .card {{ background: #f8f9fa; padding: 20px; border-radius: 6px; margin-bottom: 20px; }}
        .card h3 {{ margin-top: 0; color: #333; }}
        .btn {{ display: inline-block; padding: 12px 24px; background: #2196F3; color: white; text-decoration: none; border-radius: 4px; margin: 5px 10px 5px 0; }}
        .btn:hover {{ background: #1976D2; }}
        .code {{ background: #f1f1f1; padding: 2px 6px; border-radius: 3px; font-family: 'Monaco', 'Consolas', monospace; }}
        ul {{ line-height: 1.6; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>BigCommerce Migration API</h1>
        <p class=""subtitle"">Enterprise BigCommerce Migration System - API Documentation</p>
        
        <div class=""section"">
            <h2>Quick Start</h2>
            <div class=""card"">
                <h3>🚀 Interactive API Explorer</h3>
                <p>Explore and test the API endpoints using our interactive Swagger UI:</p>
                <a href=""{baseUrl}/swagger"" class=""btn"">Open Swagger UI</a>
            </div>
            
            <div class=""card"">
                <h3>📋 OpenAPI Specification</h3>
                <p>Download the complete OpenAPI 3.0 specification:</p>
                <a href=""{baseUrl}/openapi.json"" class=""btn"">Download OpenAPI JSON</a>
            </div>
        </div>
        
        <div class=""section"">
            <h2>Authentication</h2>
            <div class=""card"">
                <p>The API uses API key authentication. Include your API key in requests:</p>
                <ul>
                    <li><strong>Header:</strong> <span class=""code"">X-API-Key: your-api-key</span></li>
                    <li><strong>Authorization:</strong> <span class=""code"">Authorization: ApiKey your-api-key</span></li>
                    <li><strong>Query Parameter:</strong> <span class=""code"">?api_key=your-api-key</span> (less secure)</li>
                </ul>
            </div>
        </div>
        
        <div class=""section"">
            <h2>Main Endpoints</h2>
            <div class=""card"">
                <h3>Migration Management</h3>
                <ul>
                    <li><strong>POST</strong> <span class=""code"">/migrations</span> - Start a new migration</li>
                    <li><strong>GET</strong> <span class=""code"">/migrations/{{id}}</span> - Get migration status</li>
                    <li><strong>GET</strong> <span class=""code"">/migrations</span> - List migrations</li>
                    <li><strong>POST</strong> <span class=""code"">/migrations/{{id}}/cancel</span> - Cancel migration</li>
                </ul>
            </div>
            
            <div class=""card"">
                <h3>Monitoring & Dashboard</h3>
                <ul>
                    <li><strong>GET</strong> <span class=""code"">/dashboard/health</span> - System health</li>
                    <li><strong>GET</strong> <span class=""code"">/dashboard/statistics</span> - Migration statistics</li>
                    <li><strong>GET</strong> <span class=""code"">/dashboard/queues</span> - Queue status</li>
                </ul>
            </div>
            
            <div class=""card"">
                <h3>Authentication</h3>
                <ul>
                    <li><strong>GET</strong> <span class=""code"">/auth/status</span> - Check auth status</li>
                    <li><strong>POST</strong> <span class=""code"">/auth/api-keys</span> - Create API key</li>
                    <li><strong>GET</strong> <span class=""code"">/auth/api-keys</span> - List API keys</li>
                </ul>
            </div>
        </div>
        
        <div class=""section"">
            <h2>Resources</h2>
            <div class=""card"">
                <ul>
                    <li><a href=""https://docs.bigcommerce-migration.com"">Complete Documentation</a></li>
                    <li><a href=""https://docs.bigcommerce-migration.com/guides/getting-started"">Getting Started Guide</a></li>
                    <li><a href=""https://docs.bigcommerce-migration.com/api/errors"">Error Reference</a></li>
                    <li><a href=""mailto:support@bigcommerce-migration.com"">Support</a></li>
                </ul>
            </div>
        </div>
        
        <div class=""section"">
            <p><em>Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</em></p>
        </div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Gets the base URL for the current request
    /// </summary>
    private string GetBaseUrl(HttpRequestData req)
    {
        var scheme = req.Url.Scheme;
        var host = req.Url.Host;
        var port = req.Url.Port;
        
        var baseUrl = $"{scheme}://{host}";
        if ((scheme == "http" && port != 80) || (scheme == "https" && port != 443))
        {
            baseUrl += $":{port}";
        }
        
        return baseUrl + "/api";
    }
} 