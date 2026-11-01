using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shared.Core.Phone;
using Shared.Core.Phone.Features.ValidatePhone;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Api.Endpoints;

/// <summary>
/// Phone number validation endpoint.
/// Validation failure is data (IsValid=false), not an HTTP error.
/// 422 is returned only when the request shape is malformed (missing required fields).
/// </summary>
public class PhoneEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/phone/validate",
                async (ValidatePhoneRequest request, ISender sender) =>
                {
                    var query = new ValidatePhoneQuery(request.PhoneNumber, request.CountryCode);
                    var result = await sender.Send(query);
                    return Results.Ok(ApiResponseExtensions.Success(result, "Phone validation completed."));
                })
            .WithName("ValidatePhonePost")
            .WithApiVersionSet(app.NewApiVersionSet("Phone").Build())
            .Produces<ApiResponse<PhoneValidationResult>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Validate Phone Number")
            .WithDescription("""
                Validates a phone number and returns structured information.

                **IsValid=false** is a valid 200 response — it means the number is invalid for the given country.
                **422** is returned only when the request itself is malformed (empty phone or country code).

                **CountryCode** examples: `EG`, `SA`, `AE`, `US`, `GB`
                Use `ZZ` to validate any international number (requires leading '+').

                **Example request:**
                ```json
                { "phoneNumber": "01012345678", "countryCode": "EG" }
                ```

                **Example response (valid):**
                ```json
                {
                  "isValid": true,
                  "e164": "+201012345678",
                  "international": "+20 10 1234 5678",
                  "national": "010 1234 5678",
                  "kind": "Mobile",
                  "countryCode": "EG"
                }
                ```
                """)
            .WithOpenApi()
            .AllowAnonymous()
            .HasApiVersion(1.0);

        app.MapGet(EndpointConfig.BaseApiPath + "/phone/validate",
                async (string phoneNumber, string countryCode, ISender sender) =>
                {
                    var query = new ValidatePhoneQuery(phoneNumber, countryCode);
                    var result = await sender.Send(query);
                    return Results.Ok(ApiResponseExtensions.Success(result, "Phone validation completed."));
                })
            .WithName("ValidatePhoneGet")
            .WithApiVersionSet(app.NewApiVersionSet("Phone").Build())
            .Produces<ApiResponse<PhoneValidationResult>>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Validate Phone Number (GET)")
            .WithDescription("GET variant of phone validation. Query params: phoneNumber, countryCode.")
            .WithOpenApi()
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}

public record ValidatePhoneRequest(string PhoneNumber, string CountryCode);
