using System.Text;
using System.Text.Json;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Attachments.Queries;
using Catalog.Domains.Categories.Cache;
using Catalog.Domains.Categories.EventHandlers;
using Catalog.Domains.Categories.Events;
using Catalog.Domains.Products.Mappings;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Shared.Core.Localization.Services;
using Shared.Core.MultiTenancy;
using static Catalog.Tests.TestSupport;

namespace Catalog.Tests.Services;

public class CategoryTreeCacheTests
{
    private readonly Mock<IDistributedCache> _redis = new();
    private readonly Mock<ILogger<CategoryTreeCache>> _logger = new();
    private static readonly TenantCacheKeys Keys =
        new(new TenantContext(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions())));

    // Single-tenant mode: the key lives under the Default tenant's prefix.
    private static readonly string TenantKey = Keys.For(CategoryTreeCache.CacheKey);

    private CategoryTreeCache Cache => new(_redis.Object, Keys, _logger.Object);

    private static readonly List<CategoryNodeSnapshot> Nodes =
        [new(Guid.NewGuid(), null, "Home", "المنزل", "home", 0)];

    [Fact]
    public async Task Get_Miss_ReturnsNull()
    {
        (await Cache.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Get_Hit_DeserializesTheSnapshot()
    {
        _redis.Setup(r => r.GetAsync(TenantKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Nodes)));

        (await Cache.GetAsync()).Should().BeEquivalentTo(Nodes);
    }

    [Fact]
    public async Task Set_WritesUnderTheKeyWithAnExpiry()
    {
        await Cache.SetAsync(Nodes);

        _redis.Verify(r => r.SetAsync(TenantKey, It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invalidate_RemovesTheKey()
    {
        await Cache.InvalidateAsync();

        _redis.Verify(r => r.RemoveAsync(TenantKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RedisFailures_AreSwallowedWithAWarning()
    {
        _redis.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        _redis.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis down"));
        _redis.Setup(r => r.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        (await Cache.GetAsync()).Should().BeNull();
        await FluentActions.Invoking(() => Cache.SetAsync(Nodes)).Should().NotThrowAsync();
        await FluentActions.Invoking(() => Cache.InvalidateAsync()).Should().NotThrowAsync();

        _logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        _redis.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await FluentActions.Invoking(() => Cache.GetAsync()).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CategoryChangedEvent_InvalidatesTheCache()
    {
        var cache = new Mock<ICategoryTreeCache>();

        await new CategoryChangedEventHandler(cache.Object)
            .Handle(new CategoryChangedEvent(Guid.NewGuid()), CancellationToken.None);

        cache.Verify(c => c.InvalidateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class ProductImageValidatorTests
{
    private readonly Mock<ISender> _sender = new();
    private ProductImageValidator Validator => new(_sender.Object);

    private void Setup(Guid id, AttachmentType type) =>
        _sender.Setup(s => s.Send(It.Is<GetAttachmentByIdQuery>(q => q.Id == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAttachmentByIdResult(new AttachmentDto { Id = id, Type = type }));

    [Fact]
    public async Task Image_Passes()
    {
        var id = Guid.NewGuid();
        Setup(id, AttachmentType.Image);

        await Validator.Invoking(v => v.EnsureValidImageAsync(id)).Should().NotThrowAsync();
        (await Validator.IsImageAsync(id)).Should().BeTrue();
    }

    [Fact]
    public async Task NonImage_IsRejected()
    {
        var id = Guid.NewGuid();
        Setup(id, AttachmentType.Document);

        await Validator.Invoking(v => v.EnsureValidImageAsync(id))
            .Should().ThrowAsync<InvalidProductException>().WithMessage("*not an image*");
        (await Validator.IsImageAsync(id)).Should().BeFalse();
    }

    [Fact]
    public async Task Missing_IsTranslated()
    {
        _sender.Setup(s => s.Send(It.IsAny<GetAttachmentByIdQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Attachment", Guid.Empty));

        await Validator.Invoking(v => v.EnsureValidImageAsync(Guid.NewGuid()))
            .Should().ThrowAsync<InvalidProductException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task Inaccessible_IsTranslated()
    {
        _sender.Setup(s => s.Send(It.IsAny<GetAttachmentByIdQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        await Validator.Invoking(v => v.EnsureValidImageAsync(Guid.NewGuid()))
            .Should().ThrowAsync<InvalidProductException>().WithMessage("*not accessible*");
        (await Validator.IsImageAsync(Guid.NewGuid())).Should().BeFalse();
    }
}

public class ProductDtoMapperTests
{
    private readonly ProductDtoMapper _mapper = new(new EnumLocalizationService());

    private static ProductRow Row() => new()
    {
        Id = Guid.NewGuid(), Name = "Widget", NameEn = "Widget", NameAr = "أداة", Sku = "W-1",
        Type = ProductType.Digital, Status = ProductStatus.Published, Price = 75m, CompareAtPrice = 100m,
        Currency = Currency.EUR, Version = 7, ReviewedBy = AdminId, IsDeleted = true
    };

    [Fact]
    public void Public_HasLabelsDiscountAndVersion_ButNoAdminFields()
    {
        using var _ = new UiCulture("en");

        var dto = _mapper.ToPublic(Row());

        dto.Should().BeOfType<Contracts.Products.Dtos.ProductDto>();
        dto.TypeLabel.Should().Be("Digital");
        dto.CurrencyLabel.Should().Be("Euro");
        dto.DiscountPercentage.Should().Be(25m);
        dto.Version.Should().Be(7);
    }

    [Fact]
    public void Labels_FollowTheRequestCulture()
    {
        using var _ = new UiCulture("ar");

        _mapper.ToMine(Row()).StatusLabel.Should().Be("منشور");
    }

    [Fact]
    public void Admin_CarriesTheRawBilingualNameAndAuditFields()
    {
        var dto = _mapper.ToAdmin(Row());

        dto.LocalizedName.Should().Be(LocalizedText.Create("Widget", "أداة"));
        dto.ReviewedBy.Should().Be(AdminId);
        dto.IsDeleted.Should().BeTrue();
        dto.Status.Should().Be(ProductStatus.Published);
    }

    [Fact]
    public void Pending_ComputesDaysPending()
    {
        var now = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        _mapper.ToPending(Row() with { SubmittedForReviewAt = now.AddDays(-3).AddHours(-1) }, now)
            .DaysPending.Should().Be(3);
        _mapper.ToPending(Row() with { SubmittedForReviewAt = null }, now).DaysPending.Should().Be(0);
    }
}
