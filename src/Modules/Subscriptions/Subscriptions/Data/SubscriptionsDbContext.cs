using System.Reflection;

namespace Subscriptions.Data;

public class SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options)
    : AppDbContextBase(options), IDbContext
{
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<PlanFeatureDefinition> FeatureDefinitions => Set<PlanFeatureDefinition>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionPayment> Payments => Set<SubscriptionPayment>();
    public DbSet<SubscriptionInvoice> Invoices => Set<SubscriptionInvoice>();
    public DbSet<ScheduledPlanChange> ScheduledPlanChanges => Set<ScheduledPlanChange>();
    public DbSet<SubscriptionTransitionLog> TransitionLogs => Set<SubscriptionTransitionLog>();
    public DbSet<AdminActionAuditLog> AdminAuditLogs => Set<AdminActionAuditLog>();
    public DbSet<SubscriptionRefundRequest> RefundRequests => Set<SubscriptionRefundRequest>();
    public DbSet<PaymentInstructions> PaymentInstructions => Set<PaymentInstructions>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("subscriptions");

        // Backs InvoiceService.GenerateInvoiceNumberAsync (nextval('subscriptions.invoice_number_seq')).
        // Declared in the model so regenerated migrations create it.
        builder.HasSequence<long>("invoice_number_seq").StartsAt(1).IncrementsBy(1);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
    }
}
