namespace Catalog.Domains.Products.Features.ArchiveProduct;

public class ArchiveProductCommandValidator : AbstractValidator<ArchiveProductCommand>
{
    public ArchiveProductCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
    }
}

/// <summary>Published → Archived (taken off sale). Owner or admin. Shares the load/authorize/version/save skeleton via <see cref="ProductWorkflowRunner"/>.</summary>
public class ArchiveProductHandler(ProductWorkflowRunner workflow, IProductAuthorizationService authorization)
    : ICommandHandler<ArchiveProductCommand, ProductStatusChangedResult>
{
    public Task<ProductStatusChangedResult> Handle(ArchiveProductCommand command, CancellationToken cancellationToken) =>
        workflow.RunAsync(
            command.ProductId,
            command.Version,
            authorize: p => authorization.EnsureCanModify(p),
            transition: p => p.Archive(),
            cancellationToken);
}
