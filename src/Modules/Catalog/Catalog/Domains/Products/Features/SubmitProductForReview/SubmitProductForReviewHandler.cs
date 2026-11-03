namespace Catalog.Domains.Products.Features.SubmitProductForReview;

public class SubmitProductForReviewCommandValidator : AbstractValidator<SubmitProductForReviewCommand>
{
    public SubmitProductForReviewCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
    }
}

/// <summary>Draft / ChangesRequested / Rejected → PendingReview. Owner or admin. Shares the load/authorize/version/save skeleton via <see cref="ProductWorkflowRunner"/>.</summary>
public class SubmitProductForReviewHandler(ProductWorkflowRunner workflow, IProductAuthorizationService authorization)
    : ICommandHandler<SubmitProductForReviewCommand, ProductStatusChangedResult>
{
    public Task<ProductStatusChangedResult> Handle(SubmitProductForReviewCommand command, CancellationToken cancellationToken) =>
        workflow.RunAsync(
            command.ProductId,
            command.Version,
            authorize: p => authorization.EnsureCanModify(p, "You can only submit your own products for review."),
            transition: p => p.SubmitForReview(),
            cancellationToken);
}
