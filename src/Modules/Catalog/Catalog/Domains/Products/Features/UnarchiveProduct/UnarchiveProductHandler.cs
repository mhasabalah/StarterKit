namespace Catalog.Domains.Products.Features.UnarchiveProduct;

public class UnarchiveProductCommandValidator : AbstractValidator<UnarchiveProductCommand>
{
    public UnarchiveProductCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
    }
}

/// <summary>Archived → Draft, so the owner can rework and resubmit. Owner or admin. Shares the load/authorize/version/save skeleton via <see cref="ProductWorkflowRunner"/>.</summary>
public class UnarchiveProductHandler(ProductWorkflowRunner workflow, IProductAuthorizationService authorization)
    : ICommandHandler<UnarchiveProductCommand, ProductStatusChangedResult>
{
    public Task<ProductStatusChangedResult> Handle(UnarchiveProductCommand command, CancellationToken cancellationToken) =>
        workflow.RunAsync(
            command.ProductId,
            command.Version,
            authorize: p => authorization.EnsureCanModify(p),
            transition: p => p.ReturnToDraft(),
            cancellationToken);
}
