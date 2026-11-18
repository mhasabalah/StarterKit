using System.Linq.Expressions;
using Shipping.Contracts.Declarations;
using Shipping.Domains.Declarations.Models;
using Shipping.Domains.Declarations.Services;

namespace Shipping.Domains.Declarations.Features;

public static class DeclarationProjections
{
    public static readonly Expression<Func<DeclarationDraft, DeclarationDraftDto>> ToDto = d => new DeclarationDraftDto(
        d.Id, d.ShipmentId, d.HsCode, d.CustomsValue, d.Duty, d.Vat, d.Currency, d.Notes, d.Status, d.SubmissionReference,
        d.SubmittedAt, d.Version);

    private static readonly Func<DeclarationDraft, DeclarationDraftDto> Compiled = ToDto.Compile();

    public static DeclarationDraftDto Map(DeclarationDraft draft) => Compiled(draft);
}

public class EstimateDutiesQueryValidator : AbstractValidator<EstimateDutiesQuery>
{
    public EstimateDutiesQueryValidator(IStringLocalizer<ShippingResource> localizer) =>
        RuleFor(x => x.ShipmentId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

/// <summary>Duties and VAT of a shipment, from the deterministic tariff rules.</summary>
public class EstimateDutiesHandler(ShippingDbContext dbContext) : IQueryHandler<EstimateDutiesQuery, DutyEstimateDto>
{
    public async Task<DutyEstimateDto> Handle(EstimateDutiesQuery query, CancellationToken cancellationToken)
    {
        var inputs = await dbContext.Shipments.AsNoTracking()
                         .Where(s => s.Id == query.ShipmentId)
                         .Select(s => new { s.HsCode, s.DestinationCountry, s.DeclaredValue, s.Currency })
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new ShipmentNotFoundException(query.ShipmentId);
        return DutyCalculator.Estimate(inputs.HsCode, inputs.DestinationCountry, inputs.DeclaredValue, inputs.Currency);
    }
}

public class PrepareDeclarationDraftCommandValidator : AbstractValidator<PrepareDeclarationDraftCommand>
{
    public PrepareDeclarationDraftCommandValidator(IStringLocalizer<ShippingResource> localizer)
    {
        RuleFor(x => x.ShipmentId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Notes).MaximumLength(DeclarationDraft.NotesMaxLength).WithMessage(_ => localizer["Validation.Notes.MaxLength"]);
    }
}

/// <summary>
/// Creates the shipment's declaration draft, or recomputes it while it is a draft. The shipment must be ready for
/// clearance. Repeating it with the same input changes nothing new (idempotent for the agent's retries).
/// </summary>
public class PrepareDeclarationDraftHandler(IShipmentRepository shipments, IDeclarationDraftRepository drafts)
    : ICommandHandler<PrepareDeclarationDraftCommand, DeclarationDraftDto>
{
    public async Task<DeclarationDraftDto> Handle(PrepareDeclarationDraftCommand command, CancellationToken cancellationToken)
    {
        var shipment = await shipments.GetByIdAsync(command.ShipmentId, cancellationToken)
                       ?? throw new ShipmentNotFoundException(command.ShipmentId);
        shipment.EnsureReadyForClearance();
        var estimate = DutyCalculator.Estimate(shipment);

        var draft = await drafts.GetByShipmentAsync(shipment.Id, cancellationToken);
        if (draft is null)
        {
            draft = DeclarationDraft.Prepare(shipment.Id, estimate, command.Notes);
            drafts.Add(draft);
        }
        else
        {
            draft.Recompute(estimate, command.Notes);
        }

        await drafts.SaveChangesAsync(cancellationToken);
        return DeclarationProjections.Map(draft);
    }
}

public class SubmitDeclarationCommandValidator : AbstractValidator<SubmitDeclarationCommand>
{
    public SubmitDeclarationCommandValidator(IStringLocalizer<ShippingResource> localizer)
    {
        RuleFor(x => x.DeclarationId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
    }
}

/// <summary>
/// Submits a declaration to customs. Every required document must be present (409 <c>documents_missing</c>). Already
/// submitted: returned unchanged, so a retried call does nothing twice; the gateway gets the declaration id as its
/// idempotency key, so a crash between its answer and the save does not submit a second declaration either.
/// </summary>
public class SubmitDeclarationHandler(
    IDeclarationDraftRepository drafts,
    IShipmentRepository shipments,
    ShippingDbContext dbContext,
    ICustomsGateway gateway,
    TimeProvider timeProvider,
    ILogger<SubmitDeclarationHandler> logger) : ICommandHandler<SubmitDeclarationCommand, DeclarationDraftDto>
{
    public async Task<DeclarationDraftDto> Handle(SubmitDeclarationCommand command, CancellationToken cancellationToken)
    {
        var draft = await drafts.GetByIdAsync(command.DeclarationId, cancellationToken)
                    ?? throw new DeclarationNotFoundException(command.DeclarationId);
        if (draft.Status == DeclarationStatus.Submitted)
            return DeclarationProjections.Map(draft);
        // ExpectVersion is enforced only at SaveChanges, AFTER the external call below: refuse a stale version
        // before anything leaves the system. ExpectVersion still guards the race between this load and the save.
        if (command.Version is { } expected && expected != draft.Version)
            throw new ConcurrencyConflictException();
        dbContext.ExpectVersion(draft, command.Version);

        var shipment = await shipments.GetByIdAsync(draft.ShipmentId, cancellationToken)
                       ?? throw new ShipmentNotFoundException(draft.ShipmentId);
        shipment.EnsureReadyForClearance();
        if (shipment.MissingDocuments().Count > 0)
            throw new InvalidShipmentStateException("documents_missing");

        var reference = await gateway.SubmitAsync(
            new CustomsSubmission(draft.Id, shipment.Reference, draft.HsCode, draft.CustomsValue, draft.Duty, draft.Vat, draft.Currency),
            draft.Id.ToString("N"), cancellationToken);
        draft.MarkSubmitted(reference, timeProvider.GetUtcNow().UtcDateTime);
        shipment.MarkDeclarationSubmitted();
        await drafts.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Declaration {DeclarationId} of shipment {ShipmentId} submitted: {Reference}", draft.Id, shipment.Id,
            reference);
        return DeclarationProjections.Map(draft);
    }
}

public class GetDeclarationByShipmentHandler(ShippingDbContext dbContext)
    : IQueryHandler<GetDeclarationByShipmentQuery, DeclarationDraftDto>
{
    public async Task<DeclarationDraftDto> Handle(GetDeclarationByShipmentQuery query, CancellationToken cancellationToken) =>
        await dbContext.DeclarationDrafts.AsNoTracking()
            .Where(d => d.ShipmentId == query.ShipmentId)
            .Select(DeclarationProjections.ToDto)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new DeclarationNotFoundException(query.ShipmentId);
}
