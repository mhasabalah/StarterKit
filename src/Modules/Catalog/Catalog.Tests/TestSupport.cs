using System.Globalization;
using Catalog.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;
using Shared.Core.Web;

namespace Catalog.Tests;

/// <summary>Builders and fakes shared by the Catalog unit tests.</summary>
public static class TestSupport
{
    public static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid AdminId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    public static readonly Guid StrangerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    public static LocalizedText Name(string en = "Widget", string ar = "أداة") => LocalizedText.Create(en, ar);

    public static Pricing Price(decimal price = 10m, decimal? compareAt = null, Currency currency = Currency.USD) =>
        Pricing.Create(price, compareAt, currency);

    public static Product NewProduct(Guid? ownerId = null, string sku = "WID-001", decimal price = 10m)
    {
        var product = Product.Create(Name(), "A widget", sku, ProductType.Physical, Price(price), ownerId ?? OwnerId);
        product.ClearDomainEvents();
        return product;
    }

    /// <summary>Walks the real state machine to reach <paramref name="status"/> (no reflection shortcuts).</summary>
    public static Product ProductIn(ProductStatus status, Guid? ownerId = null)
    {
        var p = NewProduct(ownerId);
        var reviewer = AdminId;
        switch (status)
        {
            case ProductStatus.Draft:
                break;
            case ProductStatus.PendingReview:
                p.SubmitForReview();
                break;
            case ProductStatus.Approved:
                p.SubmitForReview();
                p.Approve(reviewer);
                break;
            case ProductStatus.Published:
                p.SubmitForReview();
                p.Approve(reviewer);
                p.Publish();
                break;
            case ProductStatus.Rejected:
                p.SubmitForReview();
                p.Reject(reviewer, "Not good enough");
                break;
            case ProductStatus.ChangesRequested:
                p.SubmitForReview();
                p.RequestChanges(reviewer, "Add photos");
                break;
            case ProductStatus.Archived:
                p.SubmitForReview();
                p.Approve(reviewer);
                p.Publish();
                p.Archive();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        p.ClearDomainEvents();
        return p;
    }

    /// <summary>A real localizer over the module's embedded en/ar resx files.</summary>
    public static IStringLocalizer<CatalogResource> Localizer()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<CatalogResource>(factory);
    }

    /// <summary>Real model, no connection: enough for change tracking / ExpectVersion assertions.</summary>
    public static CatalogDbContext DisconnectedDbContext() =>
        new(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=catalog_unit_tests")
            .UseSnakeCaseNamingConvention()
            .Options);

    public static Mock<IHttpContextService> Http(Guid? userId, params string[] roles)
    {
        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserIdAsGuid()).Returns(userId);
        http.Setup(h => h.GetUserId()).Returns(userId?.ToString());
        http.Setup(h => h.GetRoles()).Returns(roles);
        return http;
    }

    public static ProductAuthorizationService AuthAs(Guid? userId, bool admin = false) =>
        new(Http(userId, admin ? [RoleConstants.Admin] : [RoleConstants.User]).Object);
}

/// <summary>Temporarily switches CurrentUICulture (restored on dispose).</summary>
public sealed class UiCulture : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    public UiCulture(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

    public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}
