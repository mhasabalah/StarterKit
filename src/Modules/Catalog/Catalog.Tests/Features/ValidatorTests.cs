using Catalog.Contracts.Categories;
using Catalog.Contracts.Products.Queries;
using Catalog.Domains.Categories.Features.UpdateCategory;
using Catalog.Domains.Products.Features.CreateProduct;
using Catalog.Domains.Products.Features.GetProducts;
using Catalog.Domains.Products.Features.RejectProduct;
using Catalog.Domains.Products.Features.ReorderProductImages;
using Catalog.Domains.Products.Features.SearchProducts;
using Catalog.Domains.Products.Features.UpdateProduct;
using Catalog.Domains.Products.Features.UploadProductImage;
using FluentValidation.TestHelper;
using static Catalog.Tests.TestSupport;

namespace Catalog.Tests.Features;

public class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new(Localizer());

    private static CreateProductCommand Valid() =>
        new(Name(), "A widget", "WID-001", ProductType.Physical, 19.99m, null, Currency.USD);

    [Fact]
    public void ValidCommand_Passes() => _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void FullCommand_Passes() =>
        _validator.TestValidate(Valid() with
        {
            CompareAtPrice = 25m,
            CategoryId = Guid.NewGuid(),
            TagIds = [Guid.NewGuid(), Guid.NewGuid()],
            Attributes = [new ProductAttributeInput(Guid.NewGuid(), "Red")],
            AttachmentSessionId = Guid.NewGuid()
        }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void MissingName_Fails() =>
        _validator.TestValidate(Valid() with { Name = null! }).ShouldHaveValidationErrorFor(x => x.Name);

    [Theory]
    [InlineData("", "عربي", "Name.En")]
    [InlineData("English", "", "Name.Ar")]
    public void MissingLanguage_FailsOnThatLanguage(string en, string ar, string property) =>
        _validator.TestValidate(Valid() with { Name = Name(en, ar) }).ShouldHaveValidationErrorFor(property);

    [Fact]
    public void TooLongName_Fails() =>
        _validator.TestValidate(Valid() with { Name = Name(new string('a', Product.NameMaxLength + 1)) })
            .ShouldHaveValidationErrorFor("Name.En");

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    [InlineData("bad/char")]
    public void InvalidSku_Fails(string sku) =>
        _validator.TestValidate(Valid() with { Sku = sku }).ShouldHaveValidationErrorFor(x => x.Sku);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.999)]
    public void InvalidPrice_Fails(decimal price) =>
        _validator.TestValidate(Valid() with { Price = price }).ShouldHaveValidationErrorFor(x => x.Price);

    [Fact]
    public void CompareAtPriceNotAbovePrice_Fails() =>
        _validator.TestValidate(Valid() with { CompareAtPrice = 19.99m })
            .ShouldHaveValidationErrorFor(x => x.CompareAtPrice);

    [Fact]
    public void UndefinedEnums_Fail()
    {
        var result = _validator.TestValidate(Valid() with { Type = (ProductType)9, Currency = (Currency)9 });
        result.ShouldHaveValidationErrorFor(x => x.Type);
        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    [Fact]
    public void DuplicateTags_Fail()
    {
        var tag = Guid.NewGuid();
        _validator.TestValidate(Valid() with { TagIds = [tag, tag] }).ShouldHaveValidationErrorFor(x => x.TagIds);
    }

    [Fact]
    public void DuplicateAttributeTypes_AndBlankValues_Fail()
    {
        var type = Guid.NewGuid();
        var result = _validator.TestValidate(Valid() with
        {
            Attributes = [new ProductAttributeInput(type, "a"), new ProductAttributeInput(type, "")]
        });

        result.ShouldHaveValidationErrorFor(x => x.Attributes);
        result.ShouldHaveValidationErrorFor("Attributes[1].Value");
    }

    [Fact]
    public void EmptyGuids_Fail()
    {
        var result = _validator.TestValidate(Valid() with
        {
            CategoryId = Guid.Empty,
            AttachmentSessionId = Guid.Empty,
            OwnerId = Guid.Empty
        });
        result.ShouldHaveValidationErrorFor(x => x.CategoryId);
        result.ShouldHaveValidationErrorFor(x => x.AttachmentSessionId);
        result.ShouldHaveValidationErrorFor(x => x.OwnerId);
    }
}

/// <summary>Validators resolve their messages from CatalogResource(.ar).resx for the request culture.</summary>
public class LocalizedValidationMessageTests
{
    private static string FirstSkuError(string culture)
    {
        using var _ = new UiCulture(culture);
        var validator = new CreateProductCommandValidator(Localizer());
        var result = validator.TestValidate(
            new CreateProductCommand(Name(), null, "", ProductType.Physical, 1m, null, Currency.USD));
        return result.Errors.First(e => e.PropertyName == "Sku").ErrorMessage;
    }

    [Fact]
    public void English_MessageComesFromResx() => FirstSkuError("en").Should().Be("SKU is required.");

    [Fact]
    public void Arabic_MessageComesFromArabicResx()
    {
        var message = FirstSkuError("ar");

        message.Should().NotBe("Validation.Sku.Required", "a missing key would echo the key back");
        message.Should().MatchRegex("[؀-ۿ]", "the Arabic resource must be used");
    }

    [Fact]
    public void Placeholders_AreFilledInLocalizedMessages()
    {
        using var _ = new UiCulture("en");
        var result = new CreateProductCommandValidator(Localizer()).TestValidate(new CreateProductCommand(
            Name(new string('a', Product.NameMaxLength + 1)), null, "S", ProductType.Physical, 1m, null, Currency.USD));

        result.Errors.Single(e => e.PropertyName == "Name.En").ErrorMessage
            .Should().Be($"Name must not exceed {Product.NameMaxLength} characters.");
    }

    [Fact]
    public void EveryResourceKeyExistsInBothLanguages()
    {
        var english = Catalog.Resources.CatalogResource.ResourceManager
            .GetResourceSet(new System.Globalization.CultureInfo("en"), true, true)!
            .Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        var arabic = Catalog.Resources.CatalogResource.ResourceManager
            .GetResourceSet(new System.Globalization.CultureInfo("ar"), true, false)!
            .Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();

        english.Should().NotBeEmpty();
        arabic.Should().BeEquivalentTo(english);
    }
}

public class OtherValidatorTests
{
    [Fact]
    public void Update_RejectsNegativeVersion_AndEmptyId()
    {
        var result = new UpdateProductCommandValidator(Localizer()).TestValidate(new UpdateProductCommand(
            Guid.Empty, Name(), null, "S", ProductType.Physical, 1m, null, Currency.USD, Version: -1));

        result.ShouldHaveValidationErrorFor(x => x.Id);
        result.ShouldHaveValidationErrorFor(x => x.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too short")]
    public void Reject_RequiresAMeaningfulReason(string reason) =>
        new RejectProductCommandValidator(Localizer())
            .TestValidate(new RejectProductCommand(Guid.NewGuid(), reason))
            .ShouldHaveValidationErrorFor(x => x.Reason);

    [Fact]
    public void Reorder_RejectsDuplicates() =>
        new ReorderProductImagesCommandValidator(Localizer())
            .TestValidate(new ReorderProductImagesCommand(Guid.NewGuid(), [Guid.Empty, Guid.Empty]))
            .ShouldHaveValidationErrorFor(x => x.OrderedImageIds);

    [Theory]
    [InlineData("application/pdf", 10)]
    [InlineData("image/png", 0)]
    [InlineData("image/png", UploadProductImageCommandValidator.MaxFileSizeBytes + 1)]
    public void Upload_RejectsNonImagesAndBadSizes(string contentType, long size)
    {
        var result = new UploadProductImageCommandValidator(Localizer()).TestValidate(
            new UploadProductImageCommand(Guid.NewGuid(), "f.png", contentType, size, Stream.Null));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ProductsQuery_ValidatesPagingAndPriceRange()
    {
        var result = new GetProductsQueryValidator(Localizer()).TestValidate(new GetProductsQuery
        {
            PageNumber = 0, PageSize = 101, MinPrice = 10, MaxPrice = 5
        });

        result.ShouldHaveValidationErrorFor(x => x.PageNumber);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
        result.ShouldHaveValidationErrorFor(x => x.MaxPrice);
    }

    [Fact]
    public void Search_RequiresATerm() =>
        new SearchProductsQueryValidator(Localizer()).TestValidate(new SearchProductsQuery { SearchTerm = "" })
            .ShouldHaveValidationErrorFor(x => x.SearchTerm);

    [Fact]
    public void UpdateCategory_CannotBeItsOwnParent()
    {
        var id = Guid.NewGuid();
        new UpdateCategoryCommandValidator(Localizer())
            .TestValidate(new UpdateCategoryCommand(id, Name(), "slug", id, 0))
            .ShouldHaveValidationErrorFor(x => x.ParentId);
    }
}
