using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;

namespace Api.Tests.Architecture;

/// <summary>
/// CLAUDE.md (EF): enums are stored as strings. A string column survives reordering or inserting enum members and
/// reads in SQL; an int column silently changes meaning when a member moves. Scans the REAL models of the host, so
/// every module, current and future, is covered. Columns that predate the rule are listed with the reason they stay.
/// </summary>
public sealed class EnumStorageTests
{
    /// <summary>
    /// "Context: Entity.Property" of enum columns stored as integers before the rule was enforced. Converting them is a
    /// data migration (ALTER COLUMN ... USING CASE) on existing tables, financial ones included, so they stay until a
    /// product decides to migrate them. Never add to this list: new enum columns use HasConversion&lt;string&gt;().
    /// </summary>
    private static readonly HashSet<string> LegacyIntegerEnumColumns = new(StringComparer.Ordinal)
    {
        "ApplicationIdentityDbContext: OnboardingProcess.Status",
        "ApplicationIdentityDbContext: OtpCode.Purpose",
        "SubscriptionsDbContext: ScheduledPlanChange.ChangeType",
        "SubscriptionsDbContext: Subscription.CancellationReason",
        "SubscriptionsDbContext: SubscriptionInvoice.Status",
        "SubscriptionsDbContext: SubscriptionInvoice.Type",
        "SubscriptionsDbContext: SubscriptionPayment.Status",
        "SubscriptionsDbContext: SubscriptionRefundRequest.RefundType",
        "SubscriptionsDbContext: SubscriptionRefundRequest.Status",
    };

    [Fact]
    public void Enum_properties_are_stored_as_strings()
    {
        using var scope = RealHost.Services.CreateScope();
        using var host = TenantScope.BeginHostScope();

        var contexts = scope.ServiceProvider.GetServices<IDbContext>().OfType<DbContext>().ToList();
        contexts.Should().HaveCountGreaterThanOrEqualTo(6, "the scan must see every module context");

        var integerEnums = contexts
            .SelectMany(context => context.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()
                .Where(IsEnumStoredAsNonString)
                .Select(property => $"{context.GetType().Name}: {entity.DisplayName()}.{property.Name}")))
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        string.Join(Environment.NewLine, integerEnums.Where(c => !LegacyIntegerEnumColumns.Contains(c))).Should().BeEmpty(
            "map enum properties with .HasConversion<string>() (and a max length)");

        LegacyIntegerEnumColumns.Except(integerEnums).Should().BeEmpty(
            "a legacy column that is now a string (or gone) must leave the allow-list");
    }

    private static bool IsEnumStoredAsNonString(IProperty property)
    {
        var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (!clr.IsEnum)
            return false;

        var provider = property.GetValueConverter()?.ProviderClrType ?? property.GetProviderClrType() ?? clr;
        provider = Nullable.GetUnderlyingType(provider) ?? provider;
        return provider != typeof(string);
    }
}
