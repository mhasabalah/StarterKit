using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Xunit;

namespace Api.E2ETests.Infrastructure;

/// <summary>
/// Migration drift guard (CI runs this suite): every module DbContext registered by the real Api host must match its
/// migrations. A model change without a migration (or a runtime/design-time mapping mismatch, e.g. a timestamp switch)
/// fails here with the context name, instead of at deploy time.
/// Fix a failure with <c>dotnet ef migrations add &lt;Name&gt; --context &lt;Context&gt;
/// --project src/Modules/&lt;Module&gt;/&lt;Module&gt; --startup-project src/Api --output-dir Data/Migrations</c>.
/// </summary>
[Collection(E2ECollection.Name)]
public class PendingModelChangesTests(E2ETestFixture fixture)
{
    /// <summary>The module contexts the host must register (a new module adds its context here).</summary>
    private static readonly string[] ExpectedContexts =
    [
        "ApplicationIdentityDbContext",
        "TenancyDbContext",
        "AttachmentDbContext",
#if (SAMPLE)
        "CatalogDbContext",
#endif
#if (SAMPLE && AGENTIC)
        "ShippingDbContext",
#endif
#if (AGENTIC)
        "AgenticDbContext",
#endif
#if (SEARCH)
        "SearchDbContext",
#endif
        "SubscriptionsDbContext",
        "NotificationDbContext"
    ];

    [Fact]
    public async Task Every_module_DbContext_matches_its_migrations()
    {
        await fixture.WithScopeAsync(async sp =>
        {
            using var host = TenantScope.BeginHostScope();
            var contexts = sp.GetServices<IDbContext>().Cast<DbContext>().ToList();

            contexts.Select(c => c.GetType().Name).Should().Contain(ExpectedContexts);

            foreach (var context in contexts)
            {
                var name = context.GetType().Name;
                context.Database.HasPendingModelChanges().Should()
                    .BeFalse($"{name} has model changes without a migration (add one with dotnet ef migrations add)");
                (await context.Database.GetPendingMigrationsAsync()).Should()
                    .BeEmpty($"startup must have applied every {name} migration");
            }
        });
    }
}
