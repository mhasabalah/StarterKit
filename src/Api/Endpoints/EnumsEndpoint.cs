using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Contracts.Enums;
using Shared.Core.Enums;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Api.Endpoints;

/// <summary>
/// Generic enum lookup endpoint for all modules.
/// Provides bilingual (EN/AR) enum values using reflection-based EnumLookupService.
/// </summary>
public class EnumsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // Get specific enum by name
        app.MapGet(EndpointConfig.BaseApiPath + "/enums/{enumName}",
                (string enumName, [FromServices] IEnumLookupService service) =>
                {
                    try
                    {
                        var result = service.GetEnumValues(enumName);
                        return Results.Ok(ApiResponseExtensions.Success(
                            result,
                            $"Successfully retrieved {result.Count} items for {result.EnumName}"));
                    }
                    catch (ArgumentException ex)
                    {
                        return Results.BadRequest(ApiResponseExtensions.Error(ex.Message, 400));
                    }
                })
            .WithName("GetEnumValues")
            .WithApiVersionSet(app.NewApiVersionSet("Enums").Build())
            .Produces<ApiResponse<EnumResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Get Enum Values")
            .WithDescription("""
                Retrieve enum values with bilingual localization (English/Arabic).
                Works for ANY enum across ALL modules (Identity, Attachment, Subscriptions, etc.).

                **Supported Enum Types** (case-insensitive, accepts kebab-case, snake_case, PascalCase):
                - Attachment Enums: `attachment-type`, `attachment-category`, `entity-type`
                - More enums from other modules...

                **Usage Examples:**
                - `/api/v1/enums/attachment-category`
                - `/api/v1/enums/AttachmentCategory`
                - `/api/v1/enums/ATTACHMENTCATEGORY`
                - `/api/v1/enums/attachment-type`

                **Response Format:**
                ```json
                {
                  "isSuccess": true,
                  "statusCode": 200,
                  "data": {
                    "enumName": "AttachmentType",
                    "count": 4,
                    "items": [
                      {
                        "id": 0,
                        "key": "Image",
                        "nameEn": "Image",
                        "nameAr": "صورة"
                      }
                    ]
                  }
                }
                ```

                **Cache:** Results are cached for 24 hours for optimal performance.
                """)
            .AllowAnonymous()
            .HasApiVersion(1.0);

        // Get all registered enums
        app.MapGet(EndpointConfig.BaseApiPath + "/enums",
                ([FromServices] IEnumLookupService service) =>
                {
                    var enums = service.GetAllRegisteredEnums();
                    return Results.Ok(ApiResponseExtensions.Success(
                        new
                        {
                            SupportedEnums = enums,
                            Count = enums.Count,
                            BaseUrl = "/api/v1/enums/{enumName}"
                        },
                        "Available enum types across all modules"));
                })
            .WithName("GetAllEnums")
            .WithApiVersionSet(app.NewApiVersionSet("Enums").Build())
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .WithSummary("Get All Registered Enums")
            .WithDescription("""
                Returns a list of all enum types registered in the system.

                Use this endpoint to discover which enum types are available
                for the GET /api/v1/enums/{enumName} endpoint.

                **Response Format:**
                ```json
                {
                  "isSuccess": true,
                  "statusCode": 200,
                  "data": {
                    "supportedEnums": [
                      "AttachmentType",
                      "AttachmentCategory",
                      "EntityType"
                    ],
                    "count": 3,
                    "baseUrl": "/api/v1/enums/{enumName}"
                  }
                }
                ```
                """)
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}
