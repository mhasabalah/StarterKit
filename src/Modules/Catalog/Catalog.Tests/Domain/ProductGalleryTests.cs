using static Catalog.Tests.TestSupport;

namespace Catalog.Tests.Domain;

public class ProductGalleryTests
{
    private static ProductImage Add(Product product, bool primary = false) =>
        product.AddImage(Guid.NewGuid(), $"https://cdn.example.com/{Guid.NewGuid():N}.jpg", null, primary);

    private static void ShouldHaveExactlyOnePrimary(Product product) =>
        product.Images.Count(i => i.IsPrimary).Should().Be(1);

    private static void ShouldBeNumberedContiguously(Product product) =>
        product.Images.Select(i => i.DisplayOrder).Order().Should().Equal(Enumerable.Range(0, product.Images.Count));

    [Fact]
    public void FirstImage_BecomesPrimaryAutomatically()
    {
        var product = NewProduct();

        var image = Add(product);

        image.IsPrimary.Should().BeTrue();
        image.DisplayOrder.Should().Be(0);
        product.PrimaryImage.Should().BeSameAs(image);
    }

    [Fact]
    public void AddingAPrimaryImage_DemotesThePreviousPrimary()
    {
        var product = NewProduct();
        var first = Add(product);

        var second = Add(product, primary: true);

        first.IsPrimary.Should().BeFalse();
        second.IsPrimary.Should().BeTrue();
        second.DisplayOrder.Should().Be(1);
        ShouldHaveExactlyOnePrimary(product);
    }

    [Fact]
    public void AddImage_TrimsCaption_AndRejectsDuplicateAttachment()
    {
        var product = NewProduct();
        var attachment = Guid.NewGuid();

        var image = product.AddImage(attachment, "https://x/1.jpg", "  front  ");
        image.Caption.Should().Be("front");

        product.Invoking(p => p.AddImage(attachment, "https://x/2.jpg"))
            .Should().Throw<InvalidProductException>().WithMessage("*already*");
    }

    [Fact]
    public void AddImage_BeyondMax_Throws()
    {
        var product = NewProduct();
        for (var i = 0; i < Product.MaxImages; i++) Add(product);

        product.CanAddImage.Should().BeFalse();
        product.Invoking(p => Add(p)).Should().Throw<InvalidProductException>().WithMessage("*at most*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddImage_WithoutUrl_Throws(string url) =>
        NewProduct().Invoking(p => p.AddImage(Guid.NewGuid(), url)).Should().Throw<InvalidProductException>();

    [Fact]
    public void AddImage_WithEmptyAttachment_Throws() =>
        NewProduct().Invoking(p => p.AddImage(Guid.Empty, "https://x/1.jpg")).Should().Throw<InvalidProductException>();

    [Fact]
    public void SetPrimaryImage_LeavesExactlyOnePrimary()
    {
        var product = NewProduct();
        Add(product);
        var target = Add(product);
        Add(product);

        product.SetPrimaryImage(target.Id);

        product.PrimaryImage.Should().BeSameAs(target);
        ShouldHaveExactlyOnePrimary(product);
    }

    [Fact]
    public void SetPrimaryImage_UnknownImage_ThrowsNotFound() =>
        NewProduct().Invoking(p => p.SetPrimaryImage(Guid.NewGuid())).Should().Throw<ProductImageNotFoundException>();

    [Fact]
    public void RemovingThePrimary_PromotesTheFirstRemainingImage_AndRenumbers()
    {
        var product = NewProduct();
        var primary = Add(product);
        var second = Add(product);
        var third = Add(product);

        var removed = product.RemoveImage(primary.Id);

        removed.Should().BeSameAs(primary);
        second.IsPrimary.Should().BeTrue();
        third.IsPrimary.Should().BeFalse();
        second.DisplayOrder.Should().Be(0);
        third.DisplayOrder.Should().Be(1);
    }

    [Fact]
    public void RemovingTheLastImage_LeavesAnEmptyGallery()
    {
        var product = NewProduct();
        var only = Add(product);

        product.RemoveImage(only.Id);

        product.Images.Should().BeEmpty();
        product.PrimaryImage.Should().BeNull();
    }

    [Fact]
    public void RemoveImage_UnknownImage_ThrowsNotFound() =>
        NewProduct().Invoking(p => p.RemoveImage(Guid.NewGuid())).Should().Throw<ProductImageNotFoundException>();

    [Fact]
    public void ReorderImages_AppliesTheGivenOrder_AndKeepsThePrimary()
    {
        var product = NewProduct();
        var a = Add(product);
        var b = Add(product);
        var c = Add(product);

        product.ReorderImages([c.Id, a.Id, b.Id]);

        c.DisplayOrder.Should().Be(0);
        a.DisplayOrder.Should().Be(1);
        b.DisplayOrder.Should().Be(2);
        a.IsPrimary.Should().BeTrue("reordering does not change which image is primary");
        ShouldBeNumberedContiguously(product);
    }

    public static TheoryData<string> InvalidOrders => new() { "missing", "duplicate", "foreign", "extra" };

    [Theory]
    [MemberData(nameof(InvalidOrders))]
    public void ReorderImages_RejectsAnythingButAPermutation(string kind)
    {
        var product = NewProduct();
        var a = Add(product);
        var b = Add(product);
        List<Guid> order = kind switch
        {
            "missing" => [a.Id],
            "duplicate" => [a.Id, a.Id],
            "foreign" => [a.Id, Guid.NewGuid()],
            _ => [a.Id, b.Id, Guid.NewGuid()]
        };

        product.Invoking(p => p.ReorderImages(order)).Should().Throw<InvalidProductException>();
        a.DisplayOrder.Should().Be(0);
        b.DisplayOrder.Should().Be(1);
    }

    [Fact]
    public void GalleryChanges_StampUpdatedAt_SoTheAggregateVersionCoversThem()
    {
        var product = NewProduct();
        product.UpdatedAt.Should().BeNull();

        Add(product);

        product.UpdatedAt.Should().NotBeNull();
    }
}
