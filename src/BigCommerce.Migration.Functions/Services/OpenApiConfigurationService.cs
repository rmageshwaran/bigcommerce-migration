using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BigCommerce.Migration.Functions.Services;

/// <summary>
/// Configuration service for OpenAPI documentation
/// Sets up Swagger/OpenAPI 3.0 specification for the BigCommerce Migration API
/// </summary>
public static class OpenApiConfigurationService
{
    /// <summary>
    /// Configures OpenAPI services for Azure Functions
    /// </summary>
    public static IServiceCollection AddOpenApiConfiguration(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            // Basic API information
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "BigCommerce Migration API",
                Version = "v1.0.0",
                Description = @"
**Enterprise BigCommerce Migration System API**

This API provides comprehensive migration capabilities for BigCommerce stores, including:
- Migration management (start, monitor, cancel)
- Real-time progress tracking
- System health monitoring
- API key management and authentication

## Authentication

The API uses API key authentication. Include your API key in requests using one of these methods:

- **Header**: `X-API-Key: your-api-key`
- **Authorization**: `Authorization: ApiKey your-api-key` 
- **Query Parameter**: `?api_key=your-api-key` (less secure)

## Rate Limiting

Rate limits vary by API key role:
- **ReadOnly**: 100 requests/minute
- **User**: 500 requests/minute  
- **Admin**: 1000 requests/minute

## Error Handling

All errors follow a consistent format with appropriate HTTP status codes:

```json
{
  ""error"": {
    ""code"": ""ERROR_CODE"",
    ""message"": ""Human readable message"",
    ""details"": [
      {
        ""field"": ""fieldName"",
        ""message"": ""Field-specific error"",
        ""code"": ""FIELD_ERROR_CODE""
      }
    ],
    ""timestamp"": ""2024-12-10T10:30:00Z"",
    ""requestId"": ""req_12345"",
    ""documentation"": ""https://docs.bigcommerce-migration.com/api/errors""
  }
}
```

## Pagination

List endpoints support pagination with query parameters:
- `page`: Page number (default: 1)
- `pageSize`: Items per page (1-1000, default: 10)

## Migration Process

1. **Validate Stores**: Ensure source and destination stores are accessible
2. **Start Migration**: Create migration with specified entity types
3. **Monitor Progress**: Track real-time progress via status endpoints
4. **Handle Completion**: Process results and handle any errors

For detailed guides and examples, visit our [documentation](https://docs.bigcommerce-migration.com).",
                Contact = new OpenApiContact
                {
                    Name = "BigCommerce Migration Support",
                    Email = "support@bigcommerce-migration.com",
                    Url = new Uri("https://docs.bigcommerce-migration.com/support")
                },
                License = new OpenApiLicense
                {
                    Name = "MIT License",
                    Url = new Uri("https://opensource.org/licenses/MIT")
                },
                TermsOfService = new Uri("https://bigcommerce-migration.com/terms")
            });

            // Add security definitions
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-API-Key",
                Description = "API Key authentication. Provide your API key in the X-API-Key header.",
                Scheme = "ApiKey"
            });

            options.AddSecurityDefinition("ApiKeyAuth", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "ApiKey",
                Description = "API Key authentication using Authorization header. Format: 'Authorization: ApiKey your-api-key'"
            });

            options.AddSecurityDefinition("ApiKeyQuery", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Query,
                Name = "api_key",
                Description = "API Key authentication via query parameter (less secure). Format: '?api_key=your-api-key'"
            });

            // Add global security requirement
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
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
            });

            // Add XML comments for better documentation
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            // Configure schema generation
            options.UseInlineDefinitionsForEnums();
            options.DescribeAllParametersInCamelCase();

            // Add custom schema filters
            options.SchemaFilter<EnumSchemaFilter>();
            options.DocumentFilter<TagOrderDocumentFilter>();

            // Add operation filters for better documentation
            options.OperationFilter<AddResponseHeadersFilter>();
            options.OperationFilter<AddSecurityRequirementsOperationFilter>();
        });

        return services;
    }

    /// <summary>
    /// Gets the OpenAPI document information
    /// </summary>
    public static OpenApiInfo GetApiInfo()
    {
        return new OpenApiInfo
        {
            Title = "BigCommerce Migration API",
            Version = "v1.0.0",
            Description = "Enterprise BigCommerce Migration System",
            Contact = new OpenApiContact
            {
                Name = "Support Team",
                Email = "support@bigcommerce-migration.com"
            }
        };
    }

    /// <summary>
    /// Gets predefined server configurations
    /// </summary>
    public static List<OpenApiServer> GetServers()
    {
        return new List<OpenApiServer>
        {
            new OpenApiServer
            {
                Url = "https://api.bigcommerce-migration.com",
                Description = "Production Server"
            },
            new OpenApiServer
            {
                Url = "https://staging-api.bigcommerce-migration.com", 
                Description = "Staging Server"
            },
            new OpenApiServer
            {
                Url = "https://localhost:7071/api",
                Description = "Development Server"
            }
        };
    }

    /// <summary>
    /// Gets common response headers
    /// </summary>
    public static Dictionary<string, OpenApiHeader> GetCommonResponseHeaders()
    {
        return new Dictionary<string, OpenApiHeader>
        {
            {
                "X-Request-ID",
                new OpenApiHeader
                {
                    Description = "Unique request identifier for tracking and debugging",
                    Schema = new OpenApiSchema { Type = "string", Format = "uuid" }
                }
            },
            {
                "X-RateLimit-Limit", 
                new OpenApiHeader
                {
                    Description = "Request limit per time window",
                    Schema = new OpenApiSchema { Type = "integer" }
                }
            },
            {
                "X-RateLimit-Remaining",
                new OpenApiHeader
                {
                    Description = "Remaining requests in current time window", 
                    Schema = new OpenApiSchema { Type = "integer" }
                }
            },
            {
                "X-RateLimit-Reset",
                new OpenApiHeader
                {
                    Description = "Unix timestamp when rate limit resets",
                    Schema = new OpenApiSchema { Type = "integer", Format = "int64" }
                }
            }
        };
    }
}

/// <summary>
/// Schema filter to improve enum documentation
/// </summary>
public class EnumSchemaFilter : Swashbuckle.AspNetCore.SwaggerGen.ISchemaFilter
{
    public void Apply(OpenApiSchema schema, Swashbuckle.AspNetCore.SwaggerGen.SchemaFilterContext context)
    {
        if (context.Type.IsEnum)
        {
            schema.Type = "string";
            var enumNames = Enum.GetNames(context.Type);
            schema.Enum = new List<Microsoft.OpenApi.Any.IOpenApiAny>();
            
            foreach (var name in enumNames)
            {
                schema.Enum.Add(new Microsoft.OpenApi.Any.OpenApiString(name));
            }
            
            // Add description with all possible values
            var enumValues = string.Join(", ", enumNames);
            schema.Description = $"Possible values: {enumValues}";
        }
    }
}

/// <summary>
/// Document filter to order tags logically
/// </summary>
public class TagOrderDocumentFilter : Swashbuckle.AspNetCore.SwaggerGen.IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, Swashbuckle.AspNetCore.SwaggerGen.DocumentFilterContext context)
    {
        // Define tag order for logical grouping
        var tagOrder = new[]
        {
            "Authentication",
            "Migrations", 
            "Monitoring",
            "Dashboard",
            "System"
        };

        if (swaggerDoc.Tags != null)
        {
            swaggerDoc.Tags = swaggerDoc.Tags
                .OrderBy(tag => Array.IndexOf(tagOrder, tag.Name))
                .ThenBy(tag => tag.Name)
                .ToList();
        }
    }
}

/// <summary>
/// Operation filter to add response headers
/// </summary>
public class AddResponseHeadersFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    public void Apply(OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        var commonHeaders = OpenApiConfigurationService.GetCommonResponseHeaders();

        foreach (var response in operation.Responses.Values)
        {
            response.Headers ??= new Dictionary<string, OpenApiHeader>();

            // Add common headers to all responses
            foreach (var header in commonHeaders)
            {
                if (!response.Headers.ContainsKey(header.Key))
                {
                    response.Headers.Add(header.Key, header.Value);
                }
            }
        }
    }
}

/// <summary>
/// Operation filter to add security requirements
/// </summary>
public class AddSecurityRequirementsOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    public void Apply(OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        // Skip security for health and docs endpoints
        var skipSecurity = new[] { "/health", "/swagger", "/openapi" };
        var path = context.ApiDescription.RelativePath?.ToLowerInvariant();
        
        if (skipSecurity.Any(skip => path?.StartsWith(skip.TrimStart('/')) == true))
        {
            operation.Security?.Clear();
            return;
        }

        // Add security requirements if not already present
        if (operation.Security == null || !operation.Security.Any())
        {
            operation.Security = new List<OpenApiSecurityRequirement>
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
        }
    }
} 