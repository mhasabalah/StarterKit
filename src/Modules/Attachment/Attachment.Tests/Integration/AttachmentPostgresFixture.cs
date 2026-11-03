using Attachment.Data;
using Attachment.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Data.Interceptors;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using Testcontainers.PostgreSql;

namespace Attachment.Tests.Integration;

/// <summary>
/// One PostgreSQL container for the integration collection. Contexts mirror Shared.Core AddCustomDbContext
/// (legacy timestamp switch, snake_case naming, auditable + tenant interceptors) and the schema is created from the model.
/// </summary>
public sealed class AttachmentPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext(Caller.Anonymous());
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A new context models a new HTTP request.</summary>
    public AttachmentDbContext CreateContext(Caller caller)
    {
        var options = new DbContextOptionsBuilder<AttachmentDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new AuditableEntityInterceptor(caller.ToHttpContext()),
                // Like AddCustomDbContext: stamps TenantId (single-tenant = the Default tenant) so reads match writes.
                new TenantEntityInterceptor(TenantContext.SingleTenant, NullLogger<TenantEntityInterceptor>.Instance))
            .Options;

        return new AttachmentDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class AttachmentPostgresCollection : ICollectionFixture<AttachmentPostgresFixture>
{
    public const string Name = "attachment-postgres";
}
