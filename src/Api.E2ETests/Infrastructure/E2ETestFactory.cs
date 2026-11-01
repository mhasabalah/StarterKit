using Microsoft.AspNetCore.Authentication;
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
        // Replace authentication with the header-driven test scheme.
        services.AddAuthentication(defaultScheme: TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

        services.PostConfigure<AuthenticationOptions>(o =>
        {
            o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            o.DefaultScheme = TestAuthHandler.SchemeName;
        });
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
