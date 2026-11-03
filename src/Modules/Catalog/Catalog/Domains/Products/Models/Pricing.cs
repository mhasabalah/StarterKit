namespace Catalog.Domains.Products.Models;

/// <summary>
/// Owned value object (mapped with OwnsOne to explicit columns). Immutable: changing a price means
/// replacing the whole Pricing. <see cref="DiscountPercentage"/> is computed, never stored.
/// </summary>
public sealed class Pricing
{
    /// <summary>Largest value that fits numeric(18,2).</summary>
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public decimal Price { get; private set; }

    /// <summary>Optional "was" price shown struck-through; must be greater than <see cref="Price"/>.</summary>
    public decimal? CompareAtPrice { get; private set; }

    public Currency Currency { get; private set; }

    public decimal DiscountPercentage => CalculateDiscountPercentage(Price, CompareAtPrice);

    // EF Core
    private Pricing()
    {
    }

    public static Pricing Create(decimal price, decimal? compareAtPrice, Currency currency)
    {
        if (price < 0)
            throw new InvalidProductException("Product price must not be negative.");
        if (price > MaxAmount)
            throw new InvalidProductException("Product price is too large.");
        if (compareAtPrice is { } compareAt)
        {
            if (compareAt <= price)
                throw new InvalidProductException("Compare-at price must be greater than the price.");
            if (compareAt > MaxAmount)
                throw new InvalidProductException("Compare-at price is too large.");
        }

        if (!Enum.IsDefined(currency))
            throw new InvalidProductException($"Currency '{currency}' is not supported.");

        return new Pricing { Price = price, CompareAtPrice = compareAtPrice, Currency = currency };
    }

    /// <summary>Percentage off the compare-at price, rounded to 2 decimals; 0 when there is no discount.</summary>
    public static decimal CalculateDiscountPercentage(decimal price, decimal? compareAtPrice) =>
        compareAtPrice is { } compareAt && compareAt > price && compareAt > 0
            ? Math.Round((compareAt - price) / compareAt * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;
}
