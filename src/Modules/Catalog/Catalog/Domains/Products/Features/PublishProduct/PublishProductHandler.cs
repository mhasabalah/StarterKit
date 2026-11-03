namespace Catalog.Domains.Products.Features.PublishProduct;

public class PublishProductCommandValidator : AbstractValidator<PublishProductCommand>
{
    public PublishProductCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
    }
}

/// <summary>Approved → Published (publicly visible; raises ProductPublishedIntegrationEvent). Admin only. Shares the load/authorize/version/save skeleton via <see cref="ProductWorkflowRunner"/>.</summary>
public class PublishProductHandler(ProductWorkflowRunner workflow, IProductAuthorizationService authorization)
    : ICommandHandler<PublishProductCommand, ProductStatusChangedResult>
{
    public Task<ProductStatusChangedResult> Handle(PublishProductCommand command, CancellationToken cancellationToken) =>
        workflow.RunAsync(
            command.ProductId,
            command.Version,
            authorize: _ => authorization.EnsureAdmin(),
            transition: p => p.Publish(),
            cancellationToken);
}
