namespace Catalog.Tests.Domain;

public class PricingTests
{
    [Theory]
    [InlineData(80, 100, 20)]
    [InlineData(99.99, 100, 0.01)]
    [InlineData(66.67, 100, 33.33)]
    [InlineData(1, 3, 66.67)]
    [InlineData(0, 10, 100)]
    public void DiscountPercentage_IsComputedAndRounded(decimal price, decimal compareAt, decimal expected) =>
        Pricing.Create(price, compareAt, Currency.USD).DiscountPercentage.Should().Be(expected);

    [Fact]
    public void DiscountPercentage_IsZeroWithoutCompareAtPrice() =>
        Pricing.Create(10m, null, Currency.EUR).DiscountPercentage.Should().Be(0m);

    [Theory]
    [InlineData(null)]
    [InlineData("10")]
    [InlineData("5")]
    [InlineData("0")]
    public void CalculateDiscountPercentage_IsZeroWhenThereIsNoRealDiscount(string? compareAt) =>
        Pricing.CalculateDiscountPercentage(10m, compareAt is null ? null : decimal.Parse(compareAt))
            .Should().Be(0m);

    [Fact]
    public void Create_ZeroPrice_IsAllowed() => Pricing.Create(0m, null, Currency.USD).Price.Should().Be(0m);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Create_NegativePrice_Throws(decimal price) =>
        FluentActions.Invoking(() => Pricing.Create(price, null, Currency.USD))
            .Should().Throw<InvalidProductException>().WithMessage("*negative*");

    [Fact]
    public void Create_PriceAboveMax_Throws() =>
        FluentActions.Invoking(() => Pricing.Create(Pricing.MaxAmount + 1, null, Currency.USD))
            .Should().Throw<InvalidProductException>();

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 9)]
    public void Create_CompareAtNotAbovePrice_Throws(decimal price, decimal compareAt) =>
        FluentActions.Invoking(() => Pricing.Create(price, compareAt, Currency.USD))
            .Should().Throw<InvalidProductException>().WithMessage("*greater*");

    [Fact]
    public void Create_UndefinedCurrency_Throws() =>
        FluentActions.Invoking(() => Pricing.Create(1m, null, (Currency)42)).Should().Throw<InvalidProductException>();
}
