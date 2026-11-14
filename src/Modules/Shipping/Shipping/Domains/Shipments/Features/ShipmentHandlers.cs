using System.Linq.Expressions;

namespace Shipping.Domains.Shipments.Features;

/// <summary>SQL projection of a shipment with its documents.</summary>
public static class ShipmentProjections
{
    public static readonly Expression<Func<Shipment, ShipmentDto>> ToDto = s => new ShipmentDto(
        s.Id, s.Reference, s.OriginCountry, s.DestinationCountry, s.HsCode, s.DeclaredValue, s.Currency, s.WeightKg,
        s.Status, s.Documents.Select(d => new ShipmentDocumentDto(d.Kind, d.Reference, d.Content)).ToList(),
        s.CreatedAt, s.Version);

    private static readonly Func<Shipment, ShipmentDto> Compiled = ToDto.Compile();

    public static ShipmentDto Map(Shipment shipment) => Compiled(shipment);
}

public class CreateShipmentCommandValidator : AbstractValidator<CreateShipmentCommand>
{
    public CreateShipmentCommandValidator(IStringLocalizer<ShippingResource> localizer)
    {
        RuleFor(x => x.Reference).NotEmpty().WithMessage(_ => localizer["Validation.Reference.Required"])
            .MaximumLength(Shipment.ReferenceMaxLength).WithMessage(_ => localizer["Validation.Reference.MaxLength"]);
        RuleFor(x => x.OriginCountry).Must(c => Shipment.CountryPattern().IsMatch((c ?? string.Empty).Trim().ToUpperInvariant()))
            .WithMessage(_ => localizer["Validation.Country.Format"]);
        RuleFor(x => x.DestinationCountry).Must(c => Shipment.CountryPattern().IsMatch((c ?? string.Empty).Trim().ToUpperInvariant()))
            .WithMessage(_ => localizer["Validation.Country.Format"]);
        RuleFor(x => x.HsCode).Must(c => Shipment.HsCodePattern().IsMatch((c ?? string.Empty).Trim()))
            .WithMessage(_ => localizer["Validation.HsCode.Format"]);
        RuleFor(x => x.Currency).Must(c => Shipment.CurrencyPattern().IsMatch((c ?? string.Empty).Trim().ToUpperInvariant()))
            .WithMessage(_ => localizer["Validation.Currency.Format"]);
        RuleFor(x => x.DeclaredValue).GreaterThan(0).WithMessage(_ => localizer["Validation.Amount.Positive"]);
        RuleFor(x => x.WeightKg).GreaterThan(0).WithMessage(_ => localizer["Validation.Amount.Positive"]);
        RuleFor(x => x.Documents!.Count).LessThanOrEqualTo(Shipment.MaxDocuments)
            .WithMessage(_ => localizer["Validation.Documents.Max"]).When(x => x.Documents is not null);
        RuleForEach(x => x.Documents).ChildRules(document =>
        {
            document.RuleFor(d => d.Kind).IsInEnum().WithMessage(_ => localizer["Validation.Document.Kind"]);
            document.RuleFor(d => d.Reference).NotEmpty().WithMessage(_ => localizer["Validation.Document.Reference"])
                .MaximumLength(ShipmentDocument.ReferenceMaxLength).WithMessage(_ => localizer["Validation.Document.Reference"]);
            document.RuleFor(d => d.Content).MaximumLength(ShipmentDocument.ContentMaxLength)
                .WithMessage(_ => localizer["Validation.Document.Content"]);
        });
    }
}

public class CreateShipmentHandler(IShipmentRepository repository) : ICommandHandler<CreateShipmentCommand, CreateShipmentResult>
{
    public async Task<CreateShipmentResult> Handle(CreateShipmentCommand command, CancellationToken cancellationToken)
    {
        var shipment = Shipment.Create(command.Reference, command.OriginCountry, command.DestinationCountry, command.HsCode,
            command.DeclaredValue, command.Currency, command.WeightKg, command.Documents);
        if (await repository.ReferenceExistsAsync(shipment.Reference, cancellationToken))
            throw new ShipmentAlreadyExistsException(shipment.Reference);

        repository.Add(shipment);
        await repository.SaveChangesAsync(cancellationToken);
        return new CreateShipmentResult(shipment.Id);
    }
}

public class MarkShipmentReadyForClearanceCommandValidator : AbstractValidator<MarkShipmentReadyForClearanceCommand>
{
    public MarkShipmentReadyForClearanceCommandValidator(IStringLocalizer<ShippingResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
    }
}

/// <summary>
/// Hands a shipment to customs preparation. The integration event it raises (same transaction, outbox) starts the
/// preparation agent on behalf of the caller (<c>ShipmentReadyForClearanceConsumer</c>).
/// </summary>
public class MarkShipmentReadyForClearanceHandler(
    IShipmentRepository repository,
    ShippingDbContext dbContext,
    IHttpContextService httpContext) : ICommandHandler<MarkShipmentReadyForClearanceCommand, ShipmentDto>
{
    public async Task<ShipmentDto> Handle(MarkShipmentReadyForClearanceCommand command, CancellationToken cancellationToken)
    {
        var userId = httpContext.GetUserIdAsGuid()
                     ?? throw new UnauthorizedException(ShippingText.Get("Shipment.AuthenticationRequired"));
        var shipment = await repository.GetByIdAsync(command.Id, cancellationToken)
                       ?? throw new ShipmentNotFoundException(command.Id);
        dbContext.ExpectVersion(shipment, command.Version);

        shipment.MarkReadyForClearance(userId);
        await repository.SaveChangesAsync(cancellationToken);
        return ShipmentProjections.Map(shipment);
    }
}

public class GetShipmentByIdHandler(ShippingDbContext dbContext) : IQueryHandler<GetShipmentByIdQuery, ShipmentDto>
{
    public async Task<ShipmentDto> Handle(GetShipmentByIdQuery query, CancellationToken cancellationToken) =>
        await dbContext.Shipments.AsNoTracking()
            .Where(s => s.Id == query.Id)
            .Select(ShipmentProjections.ToDto)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new ShipmentNotFoundException(query.Id);
}

public class GetShipmentsQueryValidator : AbstractValidator<GetShipmentsQuery>
{
    public GetShipmentsQueryValidator(IStringLocalizer<ShippingResource> localizer)
    {
        Include(new Shared.Core.Pagination.BaseQueryValidator<GetShipmentsQuery>());
        RuleFor(x => x.Search).MaximumLength(Shipment.ReferenceMaxLength).WithMessage(_ => localizer["Validation.Reference.MaxLength"]);
    }
}

public class GetShipmentsHandler(ShippingDbContext dbContext) : IQueryHandler<GetShipmentsQuery, PaginatedResult<ShipmentDto>>
{
    public async Task<PaginatedResult<ShipmentDto>> Handle(GetShipmentsQuery query, CancellationToken cancellationToken)
    {
        var shipments = dbContext.Shipments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = Shipment.NormalizeReference(query.Search);
            shipments = shipments.Where(s => s.Reference.Contains(search));
        }

        return await shipments
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Select(ShipmentProjections.ToDto)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
