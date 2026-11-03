using static Catalog.Tests.TestSupport;

namespace Catalog.Tests.Domain;

public class ProductTests
{
    [Fact]
    public void Create_SetsDraftState_NormalizesAndRaisesIdOnlyEvent()
    {
        var product = Product.Create(Name("  Widget ", " أداة "), "  desc  ", "  wid-001 ", ProductType.Digital,
            Price(19.99m), OwnerId, categoryId: null);

        product.Id.Should().NotBeEmpty();
        product.Name.Should().Be(LocalizedText.Create("Widget", "أداة"));
        product.Description.Should().Be("desc");
        product.Sku.Should().Be("WID-001");
        product.Type.Should().Be(ProductType.Digital);
        product.Status.Should().Be(ProductStatus.Draft);
        product.OwnerId.Should().Be(OwnerId);
        product.Version.Should().Be(0);
        product.IsDeleted.Should().BeFalse();
        product.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new ProductCreatedEvent(product.Id, OwnerId, "WID-001"));
    }

    [Fact]
    public void Create_DoesNotShareTheCallersLocalizedTextInstance()
    {
        var name = Name();

        var product = Product.Create(name, null, "S1", ProductType.Physical, Price(), OwnerId);

        product.Name.Should().NotBeSameAs(name, "owned instances must never be shared between owners");
    }

    [Theory]
    [InlineData("", "عربي")]
    [InlineData("English", "")]
    [InlineData("   ", "عربي")]
    public void Create_WithMissingLanguage_Throws(string en, string ar)
    {
        var act = () => Product.Create(Name(en, ar), null, "S1", ProductType.Physical, Price(), OwnerId);

        act.Should().Throw<InvalidProductException>().WithMessage("*name*");
    }

    [Fact]
    public void Create_WithTooLongName_Throws()
    {
        var act = () => Product.Create(Name(new string('a', Product.NameMaxLength + 1)), null, "S1",
            ProductType.Physical, Price(), OwnerId);

        act.Should().Throw<InvalidProductException>();
    }

    [Fact]
    public void Create_WithNameAtMaxLengthAfterTrim_IsAllowed()
    {
        var product = Product.Create(Name("  " + new string('a', Product.NameMaxLength) + "  "), null, "S1",
            ProductType.Physical, Price(), OwnerId);

        product.Name.En.Should().HaveLength(Product.NameMaxLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithMissingSku_Throws(string sku)
    {
        var act = () => Product.Create(Name(), null, sku, ProductType.Physical, Price(), OwnerId);

        act.Should().Throw<InvalidProductException>().WithMessage("*SKU*");
    }

    [Fact]
    public void Create_WithTooLongDescription_Throws()
    {
        var act = () => Product.Create(Name(), new string('d', Product.DescriptionMaxLength + 1), "S1",
            ProductType.Physical, Price(), OwnerId);

        act.Should().Throw<InvalidProductException>().WithMessage("*description*");
    }

    [Fact]
    public void Create_WithoutOwner_Throws()
    {
        var act = () => Product.Create(Name(), null, "S1", ProductType.Physical, Price(), Guid.Empty);

        act.Should().Throw<InvalidProductException>().WithMessage("*owner*");
    }

    [Fact]
    public void Create_WithUndefinedType_Throws()
    {
        var act = () => Product.Create(Name(), null, "S1", (ProductType)99, Price(), OwnerId);

        act.Should().Throw<InvalidProductException>();
    }

    [Fact]
    public void Create_WithEmptyCategoryId_Throws()
    {
        var act = () => Product.Create(Name(), null, "S1", ProductType.Physical, Price(), OwnerId, Guid.Empty);

        act.Should().Throw<InvalidProductException>();
    }

    [Fact]
    public void Update_ReplacesDetails_StampsUpdatedAt_AndRaisesEvent()
    {
        var product = NewProduct();
        var category = Guid.NewGuid();

        product.Update(Name("Gadget", "جهاز"), null, "gad-9", ProductType.Service, Price(42.5m, 50m), category);

        product.Name.En.Should().Be("Gadget");
        product.Description.Should().BeNull();
        product.Sku.Should().Be("GAD-9");
        product.Type.Should().Be(ProductType.Service);
        product.Pricing.Price.Should().Be(42.5m);
        product.CategoryId.Should().Be(category);
        product.UpdatedAt.Should().NotBeNull();
        product.DomainEvents.Should().ContainSingle().Which.Should().Be(new ProductUpdatedEvent(product.Id));
    }

    [Fact]
    public void Update_WithInvalidData_ThrowsAndLeavesStateUntouched()
    {
        var product = NewProduct();

        var act = () => product.Update(Name(""), null, "X", ProductType.Physical, Price(1m), null);

        act.Should().Throw<InvalidProductException>();
        product.Name.En.Should().Be("Widget");
        product.Sku.Should().Be("WID-001");
        product.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(" abc-1 ", "ABC-1")]
    [InlineData("ABC", "ABC")]
    [InlineData("x.y_z", "X.Y_Z")]
    public void NormalizeSku_TrimsAndUppercases(string input, string expected) =>
        Product.NormalizeSku(input).Should().Be(expected);

    // ── Attributes / tags ────────────────────────────────────────────────────

    [Fact]
    public void AddAttribute_Twice_UpsertsTheValue()
    {
        var product = NewProduct();
        var color = Guid.NewGuid();

        product.AddAttribute(color, "Red");
        product.AddAttribute(color, " Blue ");

        product.Attributes.Should().ContainSingle()
            .Which.Should().Match<ProductAttribute>(a => a.AttributeTypeId == color && a.Value == "Blue");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddAttribute_WithBlankValue_Throws(string value)
    {
        var act = () => NewProduct().AddAttribute(Guid.NewGuid(), value);

        act.Should().Throw<InvalidProductException>();
    }

    [Fact]
    public void RemoveAndClearAttributes()
    {
        var product = NewProduct();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        product.AddAttribute(a, "1");
        product.AddAttribute(b, "2");

        product.RemoveAttribute(a);
        product.Attributes.Select(x => x.AttributeTypeId).Should().Equal(b);

        product.ClearAttributes();
        product.Attributes.Should().BeEmpty();
    }

    [Fact]
    public void SyncAttributes_RemovesMissing_UpdatesExisting_AddsNew()
    {
        var product = NewProduct();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var add = Guid.NewGuid();
        product.AddAttribute(keep, "old");
        product.AddAttribute(drop, "x");
        var keptEntity = product.Attributes.Single(a => a.AttributeTypeId == keep);

        product.SyncAttributes(new Dictionary<Guid, string> { [keep] = "new", [add] = "added" });

        product.Attributes.Select(a => a.AttributeTypeId).Should().BeEquivalentTo([keep, add]);
        product.Attributes.Single(a => a.AttributeTypeId == keep).Should().BeSameAs(keptEntity,
            "an unchanged attribute must be updated in place, not deleted and re-inserted");
        keptEntity.Value.Should().Be("new");
    }

    [Fact]
    public void AddTag_IsIdempotent_AndRemoveClearWork()
    {
        var product = NewProduct();
        var tag = Guid.NewGuid();

        product.AddTag(tag);
        product.AddTag(tag);
        product.Tags.Should().ContainSingle();

        product.RemoveTag(tag);
        product.Tags.Should().BeEmpty();

        product.AddTag(Guid.NewGuid());
        product.ClearTags();
        product.Tags.Should().BeEmpty();
    }

    [Fact]
    public void SyncTags_KeepsExistingEntities_AndAppliesDelta()
    {
        var product = NewProduct();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var add = Guid.NewGuid();
        product.AddTag(keep);
        product.AddTag(drop);
        var keptEntity = product.Tags.Single(t => t.TagId == keep);

        product.SyncTags([keep, add, add]);

        product.Tags.Select(t => t.TagId).Should().BeEquivalentTo([keep, add]);
        product.Tags.Single(t => t.TagId == keep).Should().BeSameAs(keptEntity);
    }

    [Fact]
    public void AddTag_WithEmptyId_Throws() =>
        FluentActions.Invoking(() => NewProduct().AddTag(Guid.Empty)).Should().Throw<InvalidProductException>();

    // ── Featured ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetFeatured_OnlyForPublishedProducts()
    {
        var draft = NewProduct();
        draft.Invoking(p => p.SetFeatured(true)).Should().Throw<InvalidProductException>();

        var published = ProductIn(ProductStatus.Published);
        published.SetFeatured(true);
        published.IsFeatured.Should().BeTrue();

        published.SetFeatured(false);
        published.IsFeatured.Should().BeFalse();
    }

    [Fact]
    public void Archive_ClearsFeatured()
    {
        var product = ProductIn(ProductStatus.Published);
        product.SetFeatured(true);

        product.Archive();

        product.IsFeatured.Should().BeFalse();
    }

    // ── Soft delete / restore / purge ────────────────────────────────────────

    [Fact]
    public void Delete_SetsSoftDeleteFlags_AndRaisesEvent()
    {
        var product = ProductIn(ProductStatus.Published);
        product.SetFeatured(true);
        product.ClearDomainEvents();

        product.Delete();

        product.IsDeleted.Should().BeTrue();
        product.DeletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        product.IsFeatured.Should().BeFalse();
        product.DomainEvents.Should().ContainSingle().Which.Should().Be(new ProductDeletedEvent(product.Id));
    }

    [Fact]
    public void Delete_Twice_Throws()
    {
        var product = NewProduct();
        product.Delete();

        product.Invoking(p => p.Delete()).Should().Throw<InvalidProductException>();
    }

    [Fact]
    public void Restore_UndoesSoftDelete_AndRaisesEvent()
    {
        var product = NewProduct();
        product.Delete();
        product.ClearDomainEvents();

        product.Restore();

        product.IsDeleted.Should().BeFalse();
        product.DeletedAt.Should().BeNull();
        product.DomainEvents.Should().ContainSingle().Which.Should().Be(new ProductRestoredEvent(product.Id));
    }

    [Fact]
    public void Restore_WhenNotDeleted_Throws() =>
        NewProduct().Invoking(p => p.Restore()).Should().Throw<InvalidProductException>();

    [Fact]
    public void MarkPurged_RequiresSoftDelete()
    {
        var product = NewProduct();
        product.Invoking(p => p.MarkPurged()).Should().Throw<InvalidProductException>();

        product.Delete();
        product.ClearDomainEvents();
        product.MarkPurged();

        product.DomainEvents.Should().ContainSingle().Which.Should().Be(new ProductPurgedEvent(product.Id));
    }
}
