using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Shared.Core.OpenApi;

/// <summary>
/// Adds the Bearer (JWT) and API-key security schemes to the OpenAPI document and makes Bearer the
/// document-wide default requirement. Written against Microsoft.OpenApi v2 (.NET 10).
/// </summary>
public class SecuritySchemeDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string BearerSchemeId = "Bearer";
    private const string ApiKeySchemeId = "ApiKey";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[BearerSchemeId] = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description =
                "Enter 'Bearer' [space] and your token in the text input below.\n\nExample: 'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...'"
        };

        document.Components.SecuritySchemes[ApiKeySchemeId] = new OpenApiSecurityScheme
        {
            Name = "X-API-KEY",
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Description = "Enter your API key in the text input below.\n\nExample: '12345-abcdef'"
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(BearerSchemeId, document)] = []
        });

        return Task.CompletedTask;
    }
}
