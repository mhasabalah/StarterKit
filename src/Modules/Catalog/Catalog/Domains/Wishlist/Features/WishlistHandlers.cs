namespace Catalog.Domains.Wishlist.Features;

public class AddToWishlistCommandValidator : AbstractValidator<AddToWishlistCommand>
{
    public AddToWishlistCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
    }
}

/// <summary>Only published products can be wishlisted. Duplicate → 409 (pre-check + unique index for races).</summary>
public class AddToWishlistHandler(
    IWishlistRepository wishlist,
    CatalogDbContext dbContext,
    IProductAuthorizationService authorization)
    : ICommandHandler<AddToWishlistCommand, AddToWishlistResult>
{
    public async Task<AddToWishlistResult> Handle(AddToWishlistCommand command, CancellationToken cancellationToken)
    {
        var userId = authorization.RequireCurrentUserId();

        var isPublished = await dbContext.Products.AnyAsync(
            p => p.Id == command.ProductId && p.Status == ProductStatus.Published, cancellationToken);
        if (!isPublished)
            throw new ProductNotFoundException(command.ProductId);

        if (await wishlist.GetAsync(userId, command.ProductId, cancellationToken) is not null)
            throw new ConflictException(WishlistRepository.AlreadyWishlistedMessage);

        var item = WishlistItem.Create(userId, command.ProductId);
        await wishlist.AddAsync(item, cancellationToken);

        return new AddToWishlistResult(item.Id);
    }
}

public class RemoveFromWishlistCommandValidator : AbstractValidator<RemoveFromWishlistCommand>
{
    public RemoveFromWishlistCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
    }
}

public class RemoveFromWishlistHandler(IWishlistRepository wishlist, IProductAuthorizationService authorization)
    : ICommandHandler<RemoveFromWishlistCommand, RemoveFromWishlistResult>
{
    public async Task<RemoveFromWishlistResult> Handle(RemoveFromWishlistCommand command, CancellationToken ct)
    {
        var userId = authorization.RequireCurrentUserId();

        var item = await wishlist.GetAsync(userId, command.ProductId, ct)
                   ?? throw new NotFoundException("The product is not in your wishlist.");

        await wishlist.RemoveAsync(item, ct);
        return new RemoveFromWishlistResult(true);
    }
}

public class GetWishlistQueryValidator : AbstractValidator<GetWishlistQuery>
{
    public GetWishlistQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        this.AddPagingRules(localizer);
    }
}

/// <summary>The current user's wishlist, newest first. Items of soft-deleted products are filtered out.</summary>
public class GetWishlistHandler(CatalogDbContext dbContext, IProductAuthorizationService authorization)
    : IQueryHandler<GetWishlistQuery, PaginatedResult<WishlistItemDto>>
{
    public async Task<PaginatedResult<WishlistItemDto>> Handle(GetWishlistQuery query, CancellationToken ct)
    {
        var userId = authorization.RequireCurrentUserId();
        var arabic = CatalogCulture.IsArabic();

        return await dbContext.WishlistItems.AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => new WishlistItemDto
            {
                Id = w.Id,
                ProductId = w.ProductId,
                Name = arabic ? w.Product.Name.Ar : w.Product.Name.En,
                Sku = w.Product.Sku,
                Price = w.Product.Pricing.Price,
                CompareAtPrice = w.Product.Pricing.CompareAtPrice,
                Currency = w.Product.Pricing.Currency,
                PrimaryImageUrl = w.Product.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault(),
                IsAvailable = w.Product.Status == ProductStatus.Published,
                AddedAt = w.CreatedAt
            })
            .ApplySort(query, q => q.OrderByDescending(x => x.AddedAt), x => x.Id)
            .PagedResult(query.PageNumber, query.PageSize, ct);
    }
}
