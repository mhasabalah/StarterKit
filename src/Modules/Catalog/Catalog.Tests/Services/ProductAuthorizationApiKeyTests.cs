using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Shared.Core.Authorization;
using Shared.Core.Web;

namespace Catalog.Tests.Services;

public class ProductAuthorizationApiKeyTests
{
    private static ProductAuthorizationService For(ClaimsPrincipal user) =>
        new(new HttpContextService(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } }));

    // The principal exactly as the Identity module builds it for an Admin's key: no Role claim, owner roles apart.
    private static ClaimsPrincipal AdminOwnedKey(Guid userId) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ApiKeyClaimTypes.KeyId, Guid.NewGuid().ToString()),
            new Claim(ApiKeyClaimTypes.OwnerRole, "Admin"),
            new Claim(ApiKeyClaimTypes.Scope, Permissions.BillingRead)
        ], "ApiKey", ClaimTypes.Name, ClaimTypes.Role));

    [Fact]
    public void Admin_owned_api_key_is_not_an_admin_for_product_ownership_checks()
    {
        var service = For(AdminOwnedKey(Guid.NewGuid()));

        service.IsAdmin().Should().BeFalse();
        service.Invoking(s => s.EnsureAdmin()).Should().Throw<ForbiddenException>();
    }
}
