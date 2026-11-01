using Shared.Contracts.Localization;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.UpdateFeatureDefinition;

public record UpdateFeatureDefinitionRequest(
    string DisplayNameEn,
    string DisplayNameAr,
    string? DescriptionEn,
    string? DescriptionAr,
    string? Category,
    int SortOrder = 0);

public class UpdateFeatureDefinitionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/admin/feature-definitions/{key}",
                async (string key, UpdateFeatureDefinitionRequest request, ISender sender, CancellationToken ct) =>
                {
                    var description = request.DescriptionEn is not null || request.DescriptionAr is not null
                        ? LocalizedText.Create(request.DescriptionEn ?? string.Empty, request.DescriptionAr ?? string.Empty)
                        : null;

                    await sender.Send(new UpdateFeatureDefinitionCommand(
                        key,
                        LocalizedText.Create(request.DisplayNameEn, request.DisplayNameAr),
                        description,
                        request.Category,
                        request.SortOrder), ct);

                    return Results.Ok(ApiResponseExtensions.Success(true));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("UpdateFeatureDefinition")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
