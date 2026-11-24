using Catalog.Contracts.Products.IntegrationEvents;
using Catalog.Contracts.Wishlist;
using Catalog.Domains.Products.EventHandlers;
using Catalog.Domains.Products.Features.ApproveProduct;
using Catalog.Domains.Products.Features.PublishProduct;
using Catalog.Domains.Products.Features.SubmitProductForReview;
using Catalog.Domains.Products.Features.ToggleProductFeatured;
using Catalog.Domains.Wishlist.Features;
using Catalog.Domains.Wishlist.Models;
using static Catalog.Tests.TestSupport;

namespace Catalog.Tests.Features;

public class WorkflowHandlerTests
{
    private readonly Mock<IProductRepository> _repository = new();
    private readonly CatalogDbContext _db = DisconnectedDbContext();

    private Product Track(Product product, long version = 5)
    {
        product.Version = version;
        _db.Attach(product);
        _repository.Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        return product;
    }

    private ProductWorkflowRunner Runner => new(_repository.Object, _db);

    [Fact]
    public async Task Submit_ByOwner_MovesToPendingReview_WithExpectedVersion()
    {
        var product = Track(NewProduct(OwnerId));

        var result = await new SubmitProductForReviewHandler(Runner, AuthAs(OwnerId))
            .Handle(new SubmitProductForReviewCommand(product.Id, Version: 4), CancellationToken.None);

        result.Status.Should().Be(ProductStatus.PendingReview);
        product.Status.Should().Be(ProductStatus.PendingReview);
        _db.Entry(product).Property(p => p.Version).OriginalValue.Should().Be(4);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_ByStranger_IsForbidden_AndNotSaved()
    {
        var product = Track(NewProduct(OwnerId));

        var act = () => new SubmitProductForReviewHandler(Runner, AuthAs(StrangerId))
            .Handle(new SubmitProductForReviewCommand(product.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        product.Status.Should().Be(ProductStatus.Draft);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_RecordsTheCurrentAdminAsReviewer()
    {
        var product = Track(ProductIn(ProductStatus.PendingReview));

        await new ApproveProductHandler(Runner, AuthAs(AdminId, admin: true))
            .Handle(new ApproveProductCommand(product.Id, "ok"), CancellationToken.None);

        product.Status.Should().Be(ProductStatus.Approved);
        product.ReviewedBy.Should().Be(AdminId);
    }

    [Fact]
    public async Task Approve_ByNonAdmin_IsForbidden_EvenIfTheEndpointWasBypassed()
    {
        var product = Track(ProductIn(ProductStatus.PendingReview, OwnerId));

        var act = () => new ApproveProductHandler(Runner, AuthAs(OwnerId))
            .Handle(new ApproveProductCommand(product.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Publish_FromDraft_IsAnInvalidTransition()
    {
        var product = Track(NewProduct());

        var act = () => new PublishProductHandler(Runner, AuthAs(AdminId, admin: true))
            .Handle(new PublishProductCommand(product.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProductStatusTransitionException>();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Workflow_OnMissingProduct_ThrowsNotFound()
    {
        var act = () => new PublishProductHandler(Runner, AuthAs(AdminId, admin: true))
            .Handle(new PublishProductCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ProductNotFoundException>();
    }

    [Fact]
    public async Task ToggleFeatured_OnPublishedProduct()
    {
        var product = ProductIn(ProductStatus.Published);
        _repository.Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(product);

        await new ToggleProductFeaturedHandler(_repository.Object)
            .Handle(new ToggleProductFeaturedCommand(product.Id, true), CancellationToken.None);

        product.IsFeatured.Should().BeTrue();
    }
}

public class ProductPublishedIntegrationEventTests
{
    private static Product Approved()
    {
        var p = NewProduct(sku: "SKU-1");
        p.SubmitForReview();
        p.Approve(AdminId);
        p.ClearIntegrationEvents();
        return p;
    }

    [Fact]
    public void Publish_AddsExactlyOneIntegrationEvent_WithTheProductFacts()
    {
        var product = Approved();

        product.Publish();

        var evt = product.IntegrationEvents.OfType<ProductPublishedIntegrationEvent>().Should().ContainSingle().Subject;
        evt.ProductId.Should().Be(product.Id);
        evt.OwnerId.Should().Be(OwnerId);
        evt.Sku.Should().Be("SKU-1");
        evt.PublishedAt.Should().Be(product.PublishedAt!.Value);
        evt.EventId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void OtherTransitions_AddNoPublishedEvent()
    {
        var product = NewProduct();
        product.SubmitForReview();
        product.Approve(AdminId);
        product.IntegrationEvents.OfType<ProductPublishedIntegrationEvent>().Should().BeEmpty();

        var rejected = NewProduct(sku: "R-1");
        rejected.SubmitForReview();
        rejected.Reject(AdminId, "no");
        rejected.IntegrationEvents.OfType<ProductPublishedIntegrationEvent>().Should().BeEmpty();

        var changes = NewProduct(sku: "C-1");
        changes.SubmitForReview();
        changes.RequestChanges(AdminId, "photos");
        changes.IntegrationEvents.OfType<ProductPublishedIntegrationEvent>().Should().BeEmpty();

        var published = Approved();
        published.Publish();
        published.ClearIntegrationEvents();
        published.Archive();
        published.ReturnToDraft();
        published.IntegrationEvents.OfType<ProductPublishedIntegrationEvent>().Should().BeEmpty();
    }
}

public class WishlistHandlerTests
{
    private readonly Mock<IWishlistRepository> _wishlist = new();

    [Fact]
    public async Task Remove_WhenNotWishlisted_ThrowsNotFound()
    {
        var act = () => new RemoveFromWishlistHandler(_wishlist.Object, AuthAs(OwnerId))
            .Handle(new RemoveFromWishlistCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Remove_RemovesTheCallersItem()
    {
        var productId = Guid.NewGuid();
        var item = WishlistItem.Create(OwnerId, productId);
        _wishlist.Setup(w => w.GetAsync(OwnerId, productId, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var result = await new RemoveFromWishlistHandler(_wishlist.Object, AuthAs(OwnerId))
            .Handle(new RemoveFromWishlistCommand(productId), CancellationToken.None);

        result.Success.Should().BeTrue();
        _wishlist.Verify(w => w.RemoveAsync(item, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Anonymous_CannotUseTheWishlist()
    {
        var act = () => new RemoveFromWishlistHandler(_wishlist.Object, AuthAs(null))
            .Handle(new RemoveFromWishlistCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public void WishlistItem_RequiresIds()
    {
        FluentActions.Invoking(() => WishlistItem.Create(Guid.Empty, Guid.NewGuid())).Should().Throw<BadRequestException>();
        FluentActions.Invoking(() => WishlistItem.Create(Guid.NewGuid(), Guid.Empty)).Should().Throw<BadRequestException>();
    }

    // The duplicate → 409 path of AddToWishlistHandler needs a real product query; it is covered against
    // Postgres in Catalog.IntegrationTests (pre-check AND the unique-index race).
}
