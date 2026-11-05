using System.Security.Claims;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Sessions.Features.ActiveSessionIds;
using Identity.Services.Token;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using UserDirectory.Contracts.Queries;

namespace Identity.Tests.Sessions;

/// <summary>
/// Access tokens for real-time hubs: browsers cannot send <c>Authorization</c> on a WebSocket, so the JWT scheme reads
/// <c>?access_token=</c> - only under <c>/hubs</c> - and every revocation check runs on the token that was actually
/// validated, wherever it came from.
/// </summary>
public sealed class HubTokenTests
{
    private readonly Mock<ITokenBlacklistService> _blacklist = new();

    private TokenBlacklistJwtBearerEvents Events() => new(_blacklist.Object, NullLogger<TokenBlacklistJwtBearerEvents>.Instance);

    private static readonly AuthenticationScheme Scheme = new(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));

    private static MessageReceivedContext Message(string path, string? query = null, string? authorization = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        if (query is not null)
            http.Request.QueryString = new QueryString(query);
        if (authorization is not null)
            http.Request.Headers.Authorization = authorization;
        return new MessageReceivedContext(http, Scheme, new JwtBearerOptions());
    }

    [Fact]
    public async Task A_hub_request_takes_the_token_from_the_query_string()
    {
        var context = Message("/hubs/notifications", "?id=abc&access_token=the.jwt.value");

        await Events().MessageReceived(context);

        context.Token.Should().Be("the.jwt.value");
    }

    [Theory]
    [InlineData("/api/v1/notifications/inbox")]
    [InlineData("/hubsnot/notifications")]
    [InlineData("/api/hubs/notifications")]
    public async Task Any_other_route_never_accepts_a_token_in_the_url(string path)
    {
        var context = Message(path, "?access_token=the.jwt.value");

        await Events().MessageReceived(context);

        context.Token.Should().BeNull("a token in a URL leaks into logs, history and referrers");
    }

    [Fact]
    public async Task An_authorization_header_on_a_hub_request_wins_over_the_query_string()
    {
        var context = Message("/hubs/notifications", "?access_token=from.query", "Bearer from.header");

        await Events().MessageReceived(context);

        context.Token.Should().BeNull("the default header handling applies");
    }

    [Fact]
    public async Task A_logged_out_token_presented_in_the_query_string_is_rejected()
    {
        // Regression: the blacklist check used to read the raw token from the Authorization header only, so the same
        // revoked token was accepted when it arrived as ?access_token=.
        var raw = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(new byte[32]), SecurityAlgorithms.HmacSha256)
        });
        _blacklist.Setup(b => b.IsTokenBlacklistedAsync(raw)).ReturnsAsync(true);
        var http = new DefaultHttpContext();
        http.Request.Path = "/hubs/notifications";
        http.Request.QueryString = new QueryString("?access_token=" + raw);
        var context = new TokenValidatedContext(http, Scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer")),
            SecurityToken = new JsonWebToken(raw)
        };

        await Events().TokenValidated(context);

        context.Result?.Failure.Should().NotBeNull();
        TokenBlacklistJwtBearerEvents.RawToken(context).Should().Be(raw);
    }

    [Fact]
    public void Without_a_security_token_the_header_is_the_fallback()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer header.token.value";
        var context = new TokenValidatedContext(http, Scheme, new JwtBearerOptions());

        TokenBlacklistJwtBearerEvents.RawToken(context).Should().Be("header.token.value");
    }

    [Fact]
    public async Task Active_session_ids_are_the_live_refresh_token_families()
    {
        var user = Guid.NewGuid();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var repository = new Mock<IRefreshTokenRepository>();
        repository.Setup(r => r.ListActiveFamiliesAsync(user, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ActiveSession(a, null, null, "1.1.1.1", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null),
                new ActiveSession(b, null, null, "1.1.1.1", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null)
            ]);
        var handler = new GetActiveSessionIdsHandler(repository.Object, TimeProvider.System);

        (await handler.Handle(new GetActiveSessionIdsQuery(user), default)).Should().BeEquivalentTo([a, b]);
        (await handler.Handle(new GetActiveSessionIdsQuery(Guid.Empty), default)).Should().BeEmpty();
    }
}
