using Shared.Core.MultiTenancy.Authorization;
using Shipping.Contracts.Declarations;

namespace Shipping.Domains.Shipments.Features;

public sealed record CreateShipmentRequest(
    string Reference,
    string OriginCountry,
    string DestinationCountry,
    string HsCode,
    decimal DeclaredValue,
    string Currency,
    decimal WeightKg,
    IReadOnlyList<ShipmentDocumentInput>? Documents);

public sealed record VersionRequest(long? Version);

public sealed record PrepareDeclarationRequest(string? Notes);

/// <summary>
/// The Shipping sample's routes (<c>/api/v1/shipping</c>): shipments, their duties and their customs declaration. The
/// preparation agent uses the same commands through its tools. See wiki/Modules/Shipping.md.
/// </summary>
public class ShipmentsModule : ICarterModule
{
    public const string Base = EndpointConfig.BaseApiPath + "/shipping";
    private const string Tag = "Shipping";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Base)
            .WithTags(Tag)
            .WithApiVersionSet(app.NewApiVersionSet(Tag).Build())
            .HasApiVersion(1.0);

        group.MapPost("/shipments", async (CreateShipmentRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new CreateShipmentCommand(request.Reference, request.OriginCountry,
                    request.DestinationCountry, request.HsCode, request.DeclaredValue, request.Currency, request.WeightKg,
                    request.Documents), ct);
                return Results.Created($"{Base}/shipments/{result.Id}", ApiResponseExtensions.Success(result));
            })
            .WithName("CreateShipment").Produces<ApiResponse<CreateShipmentResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Create a shipment")
            .WithDescription("A shipment with its documents. The reference is unique in the tenant.")
            .RequireTenantPermission(TenantPermissions.ShipmentsManage);

        group.MapGet("/shipments", async ([AsParameters] GetShipmentsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetShipments").Produces<ApiResponse<PaginatedResult<ShipmentDto>>>()
            .WithSummary("List shipments")
            .WithDescription("Newest first; `search` matches the reference.")
            .RequireTenantMember();

        group.MapGet("/shipments/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetShipmentByIdQuery(id), ct))))
            .WithName("GetShipmentById").Produces<ApiResponse<ShipmentDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Shipment")
            .RequireTenantMember();

        group.MapPost("/shipments/{id:guid}/ready-for-clearance",
                async (Guid id, VersionRequest? request, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(
                        await sender.Send(new MarkShipmentReadyForClearanceCommand(id, request?.Version), ct))))
            .WithName("MarkShipmentReadyForClearance").Produces<ApiResponse<ShipmentDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Hand a shipment to customs preparation")
            .WithDescription(
                "Starts the shipment preparation agent on your behalf (when agents are enabled): it estimates duties, " +
                "prepares the declaration draft, checks the documents and asks a person before it submits.")
            .RequireTenantPermission(TenantPermissions.ShipmentsManage)
            .DisallowImpersonation();

        group.MapGet("/shipments/{id:guid}/duties", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new EstimateDutiesQuery(id), ct))))
            .WithName("EstimateShipmentDuties").Produces<ApiResponse<DutyEstimateDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Duty estimate")
            .WithDescription("Duties and VAT from the sample's deterministic tariff rules.")
            .RequireTenantMember();

        group.MapGet("/shipments/{id:guid}/declaration", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetDeclarationByShipmentQuery(id), ct))))
            .WithName("GetShipmentDeclaration").Produces<ApiResponse<DeclarationDraftDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Declaration of a shipment")
            .RequireTenantMember();

        group.MapPost("/shipments/{id:guid}/declaration",
                async (Guid id, PrepareDeclarationRequest? request, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(
                        await sender.Send(new PrepareDeclarationDraftCommand(id, request?.Notes), ct))))
            .WithName("PrepareShipmentDeclaration").Produces<ApiResponse<DeclarationDraftDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Prepare the declaration draft")
            .RequireTenantPermission(TenantPermissions.ShipmentsManage);

        group.MapPost("/declarations/{id:guid}/submit",
                async (Guid id, VersionRequest? request, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(
                        await sender.Send(new SubmitDeclarationCommand(id, request?.Version), ct))))
            .WithName("SubmitDeclaration").Produces<ApiResponse<DeclarationDraftDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Submit a declaration to customs")
            .WithDescription("External and irreversible: 409 `documents_missing` while a required document is missing.")
            .RequireTenantPermission(TenantPermissions.DeclarationsSubmit)
            .DisallowImpersonation();
    }
}
