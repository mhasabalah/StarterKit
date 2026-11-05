using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AuthenticationOptions = Microsoft.AspNetCore.Authentication.AuthenticationOptions;

namespace Api.E2ETests.Infrastructure;

/// <summary>
/// Boots the real Api host in-process against a Testcontainers Postgres instance and
/// replaces JWT authentication with a header-driven test scheme (see <see cref="TestAuthHandler"/>).
/// To fake a module boundary for a test run, remove/replace its registration in
/// <see cref="ConfigureTestServices"/>.
/// </summary>
public class E2ETestFactory : WebApplicationFactory<Program>
{
    /// <summary>Connection-string name used by every module's AddCustomDbContext.</summary>
    public const string ConnectionStringName = "starterkit";

    /// <summary>Signing secret of the Fake payment gateway enabled for the test host.</summary>
    public const string FakeGatewaySecret = "e2e-fake-gateway-secret-0123456789";

    private readonly string _connectionString;

    public E2ETestFactory(string connectionString)
    {
        _connectionString = connectionString;

        // Set BEFORE the host builds. Several components (e.g. Hangfire) capture the
        // connection string eagerly at service-registration time, which runs inside
        // Program's top-level statements before WebApplicationFactory's
        // ConfigureAppConfiguration sources are merged. Environment variables are
        // read by WebApplication.CreateBuilder up front, so they win for those.
        Environment.SetEnvironmentVariable($"ConnectionStrings__{ConnectionStringName}", _connectionString);
        Environment.SetEnvironmentVariable("Redis__Enabled", "false");

        // Every test client shares one IP, so the production auth (10/min) and otp (3/10min) budgets would make the
        // suite order-dependent (429s). Rate limiting is read at registration time (hence env vars, like above) and is
        // covered by Shared.Core.Tests/Web/RateLimitingTests. Indexes follow the policy order in appsettings.json;
        // Name is set too so a reorder fails loudly instead of silently raising another policy.
        Environment.SetEnvironmentVariable("RateLimiting__Policies__3__Name", "auth");
        Environment.SetEnvironmentVariable("RateLimiting__Policies__3__PermitLimit", "10000");
        Environment.SetEnvironmentVariable("RateLimiting__Policies__4__Name", "otp");
        Environment.SetEnvironmentVariable("RateLimiting__Policies__4__PermitLimit", "10000");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Added last so it overrides the Api's appsettings.json.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ConnectionStringName}"] = _connectionString,
                ["Redis:Enabled"] = "false",

                // Online payments: the Fake gateway signs webhooks with this secret (see Subscriptions/OnlinePaymentWebhookTests).
                ["Subscriptions:PaymentGateways:Fake:Enabled"] = "true",
                ["Subscriptions:PaymentGateways:Fake:Secret"] = FakeGatewaySecret,

                // Ensure JWT registration succeeds at startup (real values are
                // irrelevant — the test auth scheme replaces JWT for requests).
                ["JwtSettings:SecretKey"] = "e2e-tests-super-secret-signing-key-0123456789",
                ["JwtSettings:Issuer"] = "StarterKitTests",
                ["JwtSettings:Audience"] = "StarterKitTests",
                ["JwtSettings:RequireHttpsMetadata"] = "false",
            });
        });

        builder.ConfigureTestServices(ConfigureTestServices);
    }

    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        // Replace authentication with the header-driven test scheme. A real "Authorization: Bearer" token without
        // test headers is forwarded to the app's JWT bearer scheme (real signature, blacklist and claim handling).
        services.AddAuthentication(defaultScheme: TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, options => options.ForwardDefaultSelector = ForwardRealTokens);

        services.PostConfigure<AuthenticationOptions>(o =>
        {
            o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            o.DefaultScheme = TestAuthHandler.SchemeName;
        });
    }

    private static string? ForwardRealTokens(Microsoft.AspNetCore.Http.HttpContext context)
    {
        if (context.Request.Headers.ContainsKey(TestAuthHandler.UserIdHeader))
            return null;

        if (context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return JwtBearerDefaults.AuthenticationScheme;

        // X-Api-Key / "Authorization: ApiKey ..." goes to the app's REAL API key scheme (hash lookup, owner, scopes).
        return global::Identity.Services.ApiKeys.ApiKeyRequest.SelectScheme(context);
    }

    /// <summary>Creates a client that authenticates with a real API key (sent as <c>X-Api-Key</c>).</summary>
    public HttpClient CreateClientWithApiKey(string apiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(global::Identity.Services.ApiKeys.ApiKeyRequest.HeaderName, apiKey);
        return client;
    }

    /// <summary>Creates a client that sends a real access token (validated by the app's JWT bearer scheme).</summary>
    public HttpClient CreateClientWithToken(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Creates a client whose requests are authenticated as the given user/roles.</summary>
    public HttpClient CreateClientAs(Guid userId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(",", roles));
        }

        return client;
    }

    /// <summary>Creates an anonymous client (no auth headers).</summary>
    public HttpClient CreateAnonymousClient() => CreateClient();
}
