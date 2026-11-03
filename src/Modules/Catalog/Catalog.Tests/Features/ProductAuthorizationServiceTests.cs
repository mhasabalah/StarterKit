using static Catalog.Tests.TestSupport;

namespace Catalog.Tests.Features;

public class ProductAuthorizationServiceTests
{
    [Fact]
    public void Owner_CanModify_StrangerCannot()
    {
        var product = NewProduct(OwnerId);

        AuthAs(OwnerId).Invoking(a => a.EnsureCanModify(product)).Should().NotThrow();
        AuthAs(StrangerId).Invoking(a => a.EnsureCanModify(product)).Should().Throw<ForbiddenException>();
        AuthAs(null).Invoking(a => a.EnsureCanModify(product)).Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Admin_CanModifyAnyProduct() =>
        AuthAs(AdminId, admin: true).Invoking(a => a.EnsureCanModify(NewProduct(OwnerId))).Should().NotThrow();

    [Fact]
    public void SuperAdmin_CountsAsAdmin() =>
        new ProductAuthorizationService(Http(AdminId, Shared.Core.Authorization.RoleConstants.SuperAdmin).Object)
            .IsAdmin().Should().BeTrue();

    [Theory]
    [InlineData(ProductStatus.Draft, true)]
    [InlineData(ProductStatus.ChangesRequested, true)]
    [InlineData(ProductStatus.Rejected, true)]
    [InlineData(ProductStatus.PendingReview, false)]
    [InlineData(ProductStatus.Approved, false)]
    [InlineData(ProductStatus.Published, false)]
    [InlineData(ProductStatus.Archived, false)]
    public void Owner_CanEditOnlyInEditableStatuses(ProductStatus status, bool allowed)
    {
        var act = () => AuthAs(OwnerId).EnsureCanEdit(ProductIn(status, OwnerId));

        if (allowed) act.Should().NotThrow();
        else act.Should().Throw<ForbiddenException>().WithMessage($"*{status}*");
    }

    [Theory]
    [InlineData(ProductStatus.PendingReview)]
    [InlineData(ProductStatus.Published)]
    public void Admin_CanEditInAnyStatus(ProductStatus status) =>
        AuthAs(AdminId, admin: true).Invoking(a => a.EnsureCanEdit(ProductIn(status))).Should().NotThrow();

    [Fact]
    public void Stranger_CannotEditEvenADraft() =>
        AuthAs(StrangerId).Invoking(a => a.EnsureCanEdit(NewProduct(OwnerId))).Should().Throw<ForbiddenException>();

    [Fact]
    public void NonAdmin_CannotSpoofTheOwner() =>
        AuthAs(OwnerId).ResolveOwnerId(StrangerId).Should().Be(OwnerId);

    [Fact]
    public void Admin_MayCreateOnBehalfOfASeller()
    {
        var admin = AuthAs(AdminId, admin: true);

        admin.ResolveOwnerId(OwnerId).Should().Be(OwnerId);
        admin.ResolveOwnerId(null).Should().Be(AdminId);
        admin.ResolveOwnerId(Guid.Empty).Should().Be(AdminId);
    }

    [Fact]
    public void Anonymous_CannotResolveAnOwner() =>
        AuthAs(null).Invoking(a => a.ResolveOwnerId(null)).Should().Throw<ForbiddenException>();

    [Fact]
    public void EnsureAdmin_RejectsNonAdmins()
    {
        AuthAs(OwnerId).Invoking(a => a.EnsureAdmin()).Should().Throw<ForbiddenException>();
        AuthAs(AdminId, admin: true).Invoking(a => a.EnsureAdmin()).Should().NotThrow();
    }
}
