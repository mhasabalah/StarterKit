namespace Catalog.Tests.Domain;

public class ProductTests
{
    private static Product NewProduct(bool isActive = true) =>
        Product.Create("Widget", "A widget", "wid-001", 10m, isActive);

    [Fact]
    public void Create_WithValidData_SetsStateAndNormalizes()
    {
        var imageId = Guid.NewGuid();

        var product = Product.Create("  Widget  ", "  A widget  ", "  wid-001 ", 19.99m, true, imageId);

        product.Id.Should().NotBeEmpty();
        product.Name.Should().Be("Widget");
        product.Description.Should().Be("A widget");
        product.Sku.Should().Be("WID-001");
        product.Price.Should().Be(19.99m);
        product.IsActive.Should().BeTrue();
        product.ImageAttachmentId.Should().Be(imageId);
        product.Version.Should().Be(0);
    }

    [Fact]
    public void Create_RaisesProductCreatedEvent()
    {
        var product = NewProduct();

        product.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ProductCreatedEvent>()
            .Which.Should().Be(new ProductCreatedEvent(product.Id, "Widget", "WID-001"));
    }

    [Fact]
    public void Create_WithWhitespaceDescription_StoresNull()
    {
        var product = Product.Create("Widget", "   ", "SKU1", 1m);

        product.Description.Should().BeNull();
    }

    [Fact]
    public void Create_WithZeroPrice_IsAllowed()
    {
        var product = Product.Create("Free sample", null, "FREE", 0m);

        product.Price.Should().Be(0m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingName_Throws(string? name)
    {
        var act = () => Product.Create(name!, null, "SKU1", 1m);

        act.Should().Throw<InvalidProductException>().WithMessage("*name*");
    }

    [Fact]
    public void Create_WithTooLongName_Throws()
    {
        var act = () => Product.Create(new string('a', Product.NameMaxLength + 1), null, "SKU1", 1m);

        act.Should().Throw<InvalidProductException>();
    }

    [Fact]
    public void Create_WithNameAtMaxLengthAfterTrim_IsAllowed()
    {
        var name = "  " + new string('a', Product.NameMaxLength) + "  ";

        var product = Product.Create(name, null, "SKU1", 1m);

        product.Name.Should().HaveLength(Product.NameMaxLength);
    }

    [Fact]
    public void Create_WithTooLongDescription_Throws()
    {
        var act = () => Product.Create(
            "Widget", new string('d', Product.DescriptionMaxLength + 1), "SKU1", 1m);

        act.Should().Throw<InvalidProductException>().WithMessage("*description*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithMissingSku_Throws(string sku)
    {
        var act = () => Product.Create("Widget", null, sku, 1m);

        act.Should().Throw<InvalidProductException>().WithMessage("*SKU*");
    }

    [Fact]
    public void Create_WithTooLongSku_Throws()
    {
        var act = () => Product.Create("Widget", null, new string('S', Product.SkuMaxLength + 1), 1m);

        act.Should().Throw<InvalidProductException>();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Create_WithNegativePrice_Throws(decimal price)
    {
        var act = () => Product.Create("Widget", null, "SKU1", price);

        act.Should().Throw<InvalidProductException>().WithMessage("*negative*");
    }

    [Fact]
    public void Create_WithPriceAboveMax_Throws()
    {
        var act = () => Product.Create("Widget", null, "SKU1", Product.MaxPrice + 1m);

        act.Should().Throw<InvalidProductException>();
    }

    [Fact]
    public void Create_WithEmptyImageAttachmentId_Throws()
    {
        var act = () => Product.Create("Widget", null, "SKU1", 1m, true, Guid.Empty);

        act.Should().Throw<InvalidProductException>();
    }

    [Fact]
    public void Update_ChangesStateAndRaisesUpdatedEvent()
    {
        var product = NewProduct();
        product.ClearDomainEvents();
        var imageId = Guid.NewGuid();

        product.Update("Gadget", null, "gad-9", 42.5m, false, imageId);

        product.Name.Should().Be("Gadget");
        product.Description.Should().BeNull();
        product.Sku.Should().Be("GAD-9");
        product.Price.Should().Be(42.5m);
        product.IsActive.Should().BeFalse();
        product.ImageAttachmentId.Should().Be(imageId);
        product.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new ProductUpdatedEvent(product.Id, "Gadget", "GAD-9", 42.5m, false));
    }

    [Fact]
    public void Update_WithInvalidData_ThrowsAndLeavesStateUntouched()
    {
        var product = NewProduct();
        product.ClearDomainEvents();

        var act = () => product.Update("Gadget", null, "GAD-9", -1m, false, null);

        act.Should().Throw<InvalidProductException>();
        product.Name.Should().Be("Widget");
        product.Sku.Should().Be("WID-001");
        product.Price.Should().Be(10m);
        product.IsActive.Should().BeTrue();
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_CanClearImage()
    {
        var product = Product.Create("Widget", null, "SKU1", 1m, true, Guid.NewGuid());

        product.Update("Widget", null, "SKU1", 1m, true, null);

        product.ImageAttachmentId.Should().BeNull();
    }

    [Fact]
    public void Deactivate_WhenActive_DeactivatesAndRaisesEvent()
    {
        var product = NewProduct(isActive: true);
        product.ClearDomainEvents();

        product.Deactivate();

        product.IsActive.Should().BeFalse();
        product.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductUpdatedEvent>();
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_IsNoOp()
    {
        var product = NewProduct(isActive: false);
        product.ClearDomainEvents();

        product.Deactivate();

        product.IsActive.Should().BeFalse();
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Activate_WhenInactive_ActivatesAndRaisesEvent()
    {
        var product = NewProduct(isActive: false);
        product.ClearDomainEvents();

        product.Activate();

        product.IsActive.Should().BeTrue();
        product.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductUpdatedEvent>();
    }

    [Fact]
    public void Activate_WhenAlreadyActive_IsNoOp()
    {
        var product = NewProduct(isActive: true);
        product.ClearDomainEvents();

        product.Activate();

        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkDeleted_RaisesDeletedEventWithImageId()
    {
        var imageId = Guid.NewGuid();
        var product = Product.Create("Widget", null, "SKU1", 1m, true, imageId);
        product.ClearDomainEvents();

        product.MarkDeleted();

        product.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new ProductDeletedEvent(product.Id, imageId));
    }

    [Theory]
    [InlineData(" abc-1 ", "ABC-1")]
    [InlineData("ABC", "ABC")]
    [InlineData("x.y_z", "X.Y_Z")]
    public void NormalizeSku_TrimsAndUppercases(string input, string expected)
    {
        Product.NormalizeSku(input).Should().Be(expected);
    }
}
