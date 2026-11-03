namespace Catalog.Domains.Products.Features.RejectProduct;

public class RejectProductCommandValidator : AbstractValidator<RejectProductCommand>
{
    public RejectProductCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage(_ => localizer["Validation.Reason.Required"])
            .Length(ProductWorkflowRules.ReasonMinLength, Product.ReviewNotesMaxLength)
            .WithMessage(_ => localizer["Validation.Reason.Length"]);
    }
}

/// <summary>PendingReview → Rejected (reason required). Admin only. Shares the load/authorize/version/save skeleton via <see cref="ProductWorkflowRunner"/>.</summary>
public class RejectProductHandler(ProductWorkflowRunner workflow, IProductAuthorizationService authorization)
    : ICommandHandler<RejectProductCommand, ProductStatusChangedResult>
{
    public Task<ProductStatusChangedResult> Handle(RejectProductCommand command, CancellationToken cancellationToken) =>
        workflow.RunAsync(
            command.ProductId,
            command.Version,
            authorize: _ => authorization.EnsureAdmin(),
            transition: p => p.Reject(authorization.RequireCurrentUserId(), command.Reason),
            cancellationToken);
}
