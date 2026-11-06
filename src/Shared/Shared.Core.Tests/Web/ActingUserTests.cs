using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Shared.Core.Web;
using Xunit;

namespace Shared.Core.Tests.Web;

/// <summary>Background work on behalf of a user: the acting user is the "current user" only where no request exists.</summary>
public sealed class ActingUserTests
{
    private static HttpContextService Service(HttpContext? context) =>
        new(new HttpContextAccessor { HttpContext = context });

    [Fact]
    public void Without_a_request_or_a_scope_there_is_no_user() =>
        Service(null).GetUserIdAsGuid().Should().BeNull();

    [Fact]
    public void Without_a_request_the_acting_user_is_the_current_user_and_the_scope_restores()
    {
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();
        var service = Service(null);

        using (ActingUser.Begin(outer))
        {
            service.GetUserIdAsGuid().Should().Be(outer);
            using (ActingUser.Begin(inner))
                service.GetUserIdAsGuid().Should().Be(inner);
            service.GetUserIdAsGuid().Should().Be(outer);
        }

        service.GetUserIdAsGuid().Should().BeNull();
    }

    [Fact]
    public void A_real_request_always_wins_over_an_acting_user_and_an_anonymous_request_stays_anonymous()
    {
        var signedIn = Guid.NewGuid();
        var withUser = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, signedIn.ToString())], "t"))
        };

        using (ActingUser.Begin(Guid.NewGuid()))
        {
            Service(withUser).GetUserIdAsGuid().Should().Be(signedIn);
            Service(new DefaultHttpContext()).GetUserIdAsGuid().Should().BeNull("an anonymous request is never upgraded");
        }
    }

    [Fact]
    public void The_acting_user_grants_no_roles()
    {
        using (ActingUser.Begin(Guid.NewGuid()))
            Service(null).GetRoles().Should().BeEmpty();
    }

    [Fact]
    public void An_empty_user_is_rejected() =>
        ((Action)(() => ActingUser.Begin(Guid.Empty))).Should().Throw<ArgumentException>();
}
