using Catalog.Domains.Products.Features.CreateProduct;
using FluentValidation.TestHelper;

namespace Catalog.Tests.Features;

public class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new();

    private static CreateProductCommand Valid() =>
        new("Widget", "A widget", "WID-001", 19.99m);

    [Fact]
    public void ValidCommand_Passes()
    {
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ValidCommand_WithImage_Passes()
    {
        _validator.TestValidate(Valid() with { ImageAttachmentId = Guid.NewGuid() })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ZeroPrice_Passes()
    {
        _validator.TestValidate(Valid() with { Price = 0m }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    public void NegativePrice_Fails(decimal price)
    {
        _validator.TestValidate(Valid() with { Price = price })
            .ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void PriceWithMoreThanTwoDecimals_Fails()
    {
        _validator.TestValidate(Valid() with { Price = 1.999m })
            .ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void PriceAboveMax_Fails()
    {
        _validator.TestValidate(Valid() with { Price = Product.MaxPrice + 1m })
            .ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyName_Fails(string name)
    {
        _validator.TestValidate(Valid() with { Name = name })
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void TooLongName_Fails()
    {
        _validator.TestValidate(Valid() with { Name = new string('a', Product.NameMaxLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void NullDescription_Passes()
    {
        _validator.TestValidate(Valid() with { Description = null })
            .ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void TooLongDescription_Fails()
    {
        _validator.TestValidate(Valid() with { Description = new string('d', Product.DescriptionMaxLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    [InlineData("bad/char")]
    [InlineData("emojié")]
    public void InvalidSku_Fails(string sku)
    {
        _validator.TestValidate(Valid() with { Sku = sku })
            .ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("abc-123")]
    [InlineData("X.Y_Z-9")]
    public void ValidSku_Passes(string sku)
    {
        _validator.TestValidate(Valid() with { Sku = sku })
            .ShouldNotHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void TooLongSku_Fails()
    {
        _validator.TestValidate(Valid() with { Sku = new string('S', Product.SkuMaxLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.Sku);
    }

    [Fact]
    public void EmptyImageAttachmentId_Fails()
    {
        _validator.TestValidate(Valid() with { ImageAttachmentId = Guid.Empty })
            .ShouldHaveValidationErrorFor(x => x.ImageAttachmentId);
    }
}
