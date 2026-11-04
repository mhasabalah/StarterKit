using Attachment.Data;
using Attachment.Infrastructure.Storage;
using Attachment.Tests.TestSupport;
using Hangfire;
using Shared.Contracts.Mediation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;
using Testcontainers.PostgreSql;

namespace Attachment.Tests.Integration;

/// <summary>
/// Attachment as it runs in a MULTI-TENANT production deployment:
/// <list type="bullet">
/// <item>the REAL migrations (tenant columns, indexes, Postgres row-level security) applied as the admin;</item>
/// <item>the module (<c>AddMultiTenancy</c> enabled + <c>AddAttachmentModule</c>) connecting as a NON-superuser role,
/// so RLS is really enforced under the handlers (superusers bypass it);</item>
/// <item>two tenants, entered with <see cref="TenantScope"/> exactly like tenant resolution does per request.</item>
/// </list>
/// Faked boundaries only: the caller, Hangfire and the blob store (local temp folder).
/// </summary>
public sealed class AttachmentMultiTenantFixture : IAsyncLifetime
{
    private const string AppRole = "attachment_app";
    private const string AppPassword = "attachment_app_password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TempDirectory _storageRoot = new();
    private ServiceProvider? _provider;

    public static readonly TenantInfo TenantA = new(Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001"), "acme", "Acme");
    public static readonly TenantInfo TenantB = new(Guid.Parse("bbbbbbbb-2222-0000-0000-000000000002"), "globex", "Globex");

    public CallerAccessor Callers { get; } = new();
    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized");
    public string AdminConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // 1. Real migrations, as the admin (owner of the schema).
        var adminOptions = new DbContextOptionsBuilder<AttachmentDbContext>()
            .UseNpgsql(AdminConnectionString, b => b.UseModuleMigrationsHistory<AttachmentDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var admin = new AttachmentDbContext(adminOptions))
        {
            await admin.Database.MigrateAsync();
        }

        // 2. The application role: DML only, NOT a superuser and NOT the table owner -> RLS applies.
        await using (var connection = new NpgsqlConnection(AdminConnectionString))
        {
            await connection.OpenAsync();
            foreach (var sql in new[]
                     {
                         $"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS",
                         $"GRANT USAGE ON SCHEMA attachment TO {AppRole}",
                         $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA attachment TO {AppRole}",
                         $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA attachment TO {AppRole}"
                     })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
            }
        }

        var appConnection = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = AppRole,
            Password = AppPassword
        }.ConnectionString;

        // 3. The real module, multi-tenant, as the app role.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = appConnection,
                ["MultiTenancy:Enabled"] = "true",
                ["Attachment:Storage:Provider"] = "Local",
                ["Attachment:Storage:Local:UploadPath"] = _storageRoot.Path,
                ["Attachment:Download:SigningKey"] = "unit-test-signing-key-0123456789abcdef",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMultiTenancy(configuration);
        services.AddAttachmentModule(configuration);

        services.AddSingleton(Callers);
        services.AddScoped<IHttpContextService>(_ => Callers.Current.ToHttpContext());
        services.RemoveAll<IBackgroundJobClient>();
        services.AddSingleton<IBackgroundJobClient>(new RecordingJobClient());
        services.RemoveAll<IStorageService>();
        services.AddSingleton<IStorageService>(new FlakyStorage(TestServices.LocalStorage(_storageRoot.Path)));

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
        _storageRoot.Dispose();
    }

    /// <summary>One HTTP request of <paramref name="caller"/> resolved to <paramref name="tenant"/>.</summary>
    public async Task<T> SendAsync<T>(TenantInfo tenant, Caller caller, IRequest<T> request)
    {
        using (TenantScope.Begin(tenant))
        {
            Callers.Current = caller;
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
        }
    }

    /// <summary>Reads through the module's DbContext inside <paramref name="tenant"/>'s scope (EF filters + RLS apply).</summary>
    public async Task<T> InTenantDbAsync<T>(TenantInfo tenant, Func<AttachmentDbContext, Task<T>> work)
    {
        using (TenantScope.Begin(tenant))
        {
            await using var scope = Services.CreateAsyncScope();
            return await work(scope.ServiceProvider.GetRequiredService<AttachmentDbContext>());
        }
    }

    /// <summary>
    /// Raw SQL through the module's own DbContext (and so its RLS session interceptor) as the app role, inside the
    /// given tenant scope (or host scope when <paramref name="tenant"/> is null). Skips every EF query filter.
    /// </summary>
    public async Task<long> CountRowsAsync(TenantInfo? tenant, string table)
    {
        using var ambient = tenant is null ? TenantScope.BeginHostScope() : TenantScope.Begin(tenant);
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AttachmentDbContext>();
        return await db.Database
            .SqlQueryRaw<long>($"SELECT count(*) AS \"Value\" FROM attachment.{table}")
            .SingleAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class AttachmentMultiTenantCollection : ICollectionFixture<AttachmentMultiTenantFixture>
{
    public const string Name = "attachment-multi-tenant";
}
