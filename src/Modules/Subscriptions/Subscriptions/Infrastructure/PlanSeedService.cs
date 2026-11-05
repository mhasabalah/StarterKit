using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Contracts.Localization;
using Subscriptions.Domains.Plans.Models;

namespace Subscriptions.Infrastructure;

public class PlanSeedService(
    IUnitOfWorkRunner unitOfWork,
    IConfiguration configuration) : IHostedService
{
    private const string StarterSeedKey = "seed:starter";
    private const string ProfessionalSeedKey = "seed:professional";
    private const string EnterpriseSeedKey = "seed:enterprise";

    // Catalog keys — all available features across all plans
    private static readonly (string Key, string En, string Ar, string? Category, int Sort)[] CatalogDefinitions =
    [
        ("analytics.basic",    "Basic Analytics",    "تحليلات أساسية",   "Analytics", 10),
        ("analytics.advanced", "Advanced Analytics", "تحليلات متقدمة",   "Analytics", 20),
        ("analytics.full",     "Full Analytics",     "تحليلات كاملة",    "Analytics", 30),
        ("custom.branding",    "Custom Branding",    "علامة تجارية مخصصة", "Branding",     40),
        ("priority.support",   "Priority Support",   "دعم أولوية",       "Support",      50),
        ("api.access",         "API Access",         "وصول API",         "Integrations", 60),
    ];

    // Canonical Arabic plan names (proper Arabic, not transliterations)
    private const string StarterNameAr = "مبتدئ";
    private const string ProfessionalNameAr = "احترافي";
    private const string EnterpriseNameAr = "مؤسسي";

    // Which keys each plan gets
    private static readonly string[] StarterFeatures = ["analytics.basic"];
    private static readonly string[] ProfessionalFeatures = ["analytics.basic", "analytics.advanced", "custom.branding", "api.access"];
    private static readonly string[] EnterpriseFeatures = ["analytics.basic", "analytics.advanced", "analytics.full", "custom.branding", "priority.support", "api.access"];

    public Task StartAsync(CancellationToken cancellationToken) =>
        unitOfWork.RunAsync(async (services, ct) =>
        {
            var dbContext = services.GetRequiredService<SubscriptionsDbContext>();

            await SeedCatalog(dbContext, ct);
            await SeedPlans(dbContext, ct);
            await SeedPaymentInstructions(dbContext, ct);
            await SeedPaymentMethods(dbContext, ct);
        }, cancellationToken);

    private static async Task SeedCatalog(SubscriptionsDbContext dbContext, CancellationToken cancellationToken)
    {
        var existingKeys = await dbContext.FeatureDefinitions
            .Select(x => x.Key)
            .ToListAsync(cancellationToken);

        var existingSet = existingKeys.ToHashSet();

        foreach (var (key, en, ar, category, sort) in CatalogDefinitions)
        {
            if (existingSet.Contains(key))
                continue;

            var definition = PlanFeatureDefinition.Create(
                key,
                LocalizedText.Create(en, ar),
                description: null,
                category: category,
                sortOrder: sort);

            await dbContext.FeatureDefinitions.AddAsync(definition, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedPlans(SubscriptionsDbContext dbContext, CancellationToken cancellationToken)
    {
        // Load AsNoTracking — property updates go through ExecuteUpdateAsync which has no WHERE version = N.
        // This avoids a DbUpdateConcurrencyException from the Version concurrency token on Plan.
        var existingPlans = await dbContext.Plans
            .Include(x => x.Features)
            .AsNoTracking()
            .Where(x =>
                x.SeedKey == StarterSeedKey ||
                x.SeedKey == ProfessionalSeedKey ||
                x.SeedKey == EnterpriseSeedKey ||
                x.Name.En == "Starter" ||
                x.Name.En == "Professional" ||
                x.Name.En == "Enterprise")
            .ToListAsync(cancellationToken);

        var starter = existingPlans.FirstOrDefault(x => x.SeedKey == StarterSeedKey || x.Name.En == "Starter");
        var professional = existingPlans.FirstOrDefault(x => x.SeedKey == ProfessionalSeedKey || x.Name.En == "Professional");
        var enterprise = existingPlans.FirstOrDefault(x => x.SeedKey == EnterpriseSeedKey || x.Name.En == "Enterprise");

        // ── 1. INSERT any plans that don't exist yet ─────────────────────────
        // INSERTs have no WHERE version = N, so no concurrency token issue.
        if (starter is null)
        {
            starter = Plan.Create(
                LocalizedText.Create("Starter", StarterNameAr),
                LocalizedText.Create("Starter plan", "الخطة المبتدئة"),
                // price, currency, cycle (months), maxItems, maxStorageGb, maxUsers
                19m, "USD", 1, 100, 5, 2, isPublic: true, seedKey: StarterSeedKey);
            dbContext.Plans.Add(starter);
        }
        if (professional is null)
        {
            professional = Plan.Create(
                LocalizedText.Create("Professional", ProfessionalNameAr),
                LocalizedText.Create("Professional plan", "الخطة الاحترافية"),
                49m, "USD", 1, 1000, 50, 10, isPublic: true, seedKey: ProfessionalSeedKey);
            dbContext.Plans.Add(professional);
        }
        if (enterprise is null)
        {
            enterprise = Plan.Create(
                LocalizedText.Create("Enterprise", EnterpriseNameAr),
                LocalizedText.Create("Enterprise plan", "الخطة المؤسسية"),
                99m, "USD", 1, null, null, null, isPublic: true, seedKey: EnterpriseSeedKey);
            dbContext.Plans.Add(enterprise);
        }
        if (dbContext.ChangeTracker.HasChanges())
            await dbContext.SaveChangesAsync(cancellationToken);

        // ── 2. UPDATE existing plans via ExecuteUpdateAsync ───────────────────
        // ExecuteUpdateAsync generates: UPDATE ... SET col = val WHERE id = X
        // — no WHERE version = N, so the Version concurrency token is never checked.
        await SyncSeededPlan(dbContext, starter, StarterSeedKey, StarterNameAr, cancellationToken);
        await SyncSeededPlan(dbContext, professional, ProfessionalSeedKey, ProfessionalNameAr, cancellationToken);
        await SyncSeededPlan(dbContext, enterprise, EnterpriseSeedKey, EnterpriseNameAr, cancellationToken);

        // ── 3. INSERT any missing features ───────────────────────────────────
        // Features on newly created plans: starter.Features is empty → all keys are added.
        // Features on existing plans: loaded with AsNoTracking → only missing keys are added.
        // Feature INSERTs also have no WHERE version = N.
        var planFeatureMap = new[]
        {
            (Plan: starter,      Keys: StarterFeatures),
            (Plan: professional, Keys: ProfessionalFeatures),
            (Plan: enterprise,   Keys: EnterpriseFeatures),
        };
        foreach (var (plan, keys) in planFeatureMap)
        {
            if (plan is null)
                continue;
            var existing = plan.Features.Select(f => f.Key).ToHashSet();
            foreach (var key in keys)
            {
                if (!existing.Contains(key))
                    dbContext.PlanFeatures.Add(PlanFeature.Create(plan.Id, key));
            }
        }
        if (dbContext.ChangeTracker.HasChanges())
            await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Updates seed key and Arabic name for an existing plan using ExecuteUpdateAsync,
    // which issues a raw UPDATE with no WHERE version = N (no concurrency token check).
    private static async Task SyncSeededPlan(
        SubscriptionsDbContext dbContext,
        Plan? plan,
        string seedKey,
        string arabicName,
        CancellationToken cancellationToken)
    {
        if (plan is null)
            return;

        if (string.IsNullOrWhiteSpace(plan.SeedKey))
            await dbContext.Plans
                .Where(x => x.Id == plan.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.SeedKey, seedKey), cancellationToken);

        if (plan.Name.Ar != arabicName)
            await dbContext.Plans
                .Where(x => x.Id == plan.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name.Ar, arabicName), cancellationToken);
    }

    private async Task SeedPaymentInstructions(SubscriptionsDbContext dbContext, CancellationToken cancellationToken)
    {
        var whatsApp = configuration["PaymentInstructions:SupportWhatsApp"] ?? string.Empty;
        var email = configuration["PaymentInstructions:SupportEmail"] ?? "support@example.com";

        var existing = await dbContext.PaymentInstructions.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            await dbContext.PaymentInstructions.AddAsync(
                PaymentInstructions.Create(whatsApp, email), cancellationToken);
        }
        else
        {
            // Always sync from config so changes to appsettings take effect on restart.
            existing.Update(whatsApp, email);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static readonly (string Type, string DisplayName, string Value, string? Description, int SortOrder, bool IsActive)[] PaymentMethodDefinitions =
    [
        // Placeholder manual-payment channels — replace with your own via the admin API or this table.
        ("BankTransfer",  "Bank Transfer",  "IBAN XX00 0000 0000 0000 0000", "Transfer to our bank account and attach the receipt", 1, true),
        ("MobileWallet",  "Mobile Wallet",  "+1 555 000 0000",               null,                                                   2, true),
        ("Card",          "Card",           "n/a",                           "Coming soon - not yet active",                         3, false),
    ];

    private static async Task SeedPaymentMethods(SubscriptionsDbContext dbContext, CancellationToken cancellationToken)
    {
        var existing = await dbContext.PaymentMethods.ToListAsync(cancellationToken);
        var existingByType = existing.ToDictionary(m => m.Type, StringComparer.OrdinalIgnoreCase);

        foreach (var (type, displayName, value, description, sortOrder, isActive) in PaymentMethodDefinitions)
        {
            if (existingByType.TryGetValue(type, out var method))
            {
                // Always sync from the definition so corrections take effect on next deploy,
                // mirroring how SeedPaymentInstructions handles its row.
                method.Update(type, displayName, value, description, null, sortOrder, isActive);
            }
            else
            {
                dbContext.PaymentMethods.Add(
                    PaymentMethod.Create(type, displayName, value, description, null, sortOrder));

                if (!isActive)
                {
                    // Retrieve the just-added instance and deactivate it
                    var added = dbContext.PaymentMethods.Local
                        .First(m => m.Type == type);
                    added.Update(type, displayName, value, description, null, sortOrder, isActive: false);
                }
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
            await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
