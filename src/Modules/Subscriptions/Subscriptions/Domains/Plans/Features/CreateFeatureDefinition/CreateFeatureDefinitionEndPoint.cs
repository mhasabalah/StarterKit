using Shared.Contracts.Localization;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.CreateFeatureDefinition;

public record CreateFeatureDefinitionRequest(
    string Key,
    string DisplayNameEn,
    string DisplayNameAr,
    string? DescriptionEn,
    string? DescriptionAr,
    string? Category,
    int SortOrder = 0);

public class CreateFeatureDefinitionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/feature-definitions",
                async (CreateFeatureDefinitionRequest request, ISender sender, CancellationToken ct) =>
                {
                    var description = request.DescriptionEn is not null || request.DescriptionAr is not null
                        ? LocalizedText.Create(request.DescriptionEn ?? string.Empty, request.DescriptionAr ?? string.Empty)
                        : null;

                    var key = await sender.Send(new CreateFeatureDefinitionCommand(
                        request.Key,
                        LocalizedText.Create(request.DisplayNameEn, request.DisplayNameAr),
                        description,
                        request.Category,
                        request.SortOrder), ct);

                    return Results.Ok(ApiResponseExtensions.Success(new { key }));
                })
            .RequirePermission(Permissions.PlansManage)
            .WithName("CreateFeatureDefinition")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status409Conflict)
            .HasApiVersion(1.0);
    }
}
