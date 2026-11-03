namespace Catalog.Domains.Products.Features.RequestProductChanges;

public class RequestProductChangesCommandValidator : AbstractValidator<RequestProductChangesCommand>
{
    public RequestProductChangesCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
        RuleFor(x => x.Notes)
            .NotEmpty().WithMessage(_ => localizer["Validation.Reason.Required"])
            .Length(ProductWorkflowRules.ReasonMinLength, Product.ReviewNotesMaxLength)
            .WithMessage(_ => localizer["Validation.Reason.Length"]);
    }
}

/// <summary>PendingReview → ChangesRequested (notes required). Admin only. Shares the load/authorize/version/save skeleton via <see cref="ProductWorkflowRunner"/>.</summary>
public class RequestProductChangesHandler(ProductWorkflowRunner workflow, IProductAuthorizationService authorization)
    : ICommandHandler<RequestProductChangesCommand, ProductStatusChangedResult>
{
    public Task<ProductStatusChangedResult> Handle(RequestProductChangesCommand command, CancellationToken cancellationToken) =>
        workflow.RunAsync(
            command.ProductId,
            command.Version,
            authorize: _ => authorization.EnsureAdmin(),
            transition: p => p.RequestChanges(authorization.RequireCurrentUserId(), command.Notes),
            cancellationToken);
}
