namespace Catalog.Domains.Products.Features.ApproveProduct;

public class ApproveProductCommandValidator : AbstractValidator<ApproveProductCommand>
{
    public ApproveProductCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
        RuleFor(x => x.ReviewNotes)
            .MaximumLength(Product.ReviewNotesMaxLength).WithMessage(_ => localizer["Validation.ReviewNotes.MaxLength"])
            .When(x => x.ReviewNotes is not null);
    }
}

/// <summary>PendingReview → Approved. Admin only. Shares the load/authorize/version/save skeleton via <see cref="ProductWorkflowRunner"/>.</summary>
public class ApproveProductHandler(ProductWorkflowRunner workflow, IProductAuthorizationService authorization)
    : ICommandHandler<ApproveProductCommand, ProductStatusChangedResult>
{
    public Task<ProductStatusChangedResult> Handle(ApproveProductCommand command, CancellationToken cancellationToken) =>
        workflow.RunAsync(
            command.ProductId,
            command.Version,
            authorize: _ => authorization.EnsureAdmin(),
            transition: p => p.Approve(authorization.RequireCurrentUserId(), command.ReviewNotes),
            cancellationToken);
}
