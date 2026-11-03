using System.Reflection;
using Npgsql;
using Shared.Core.Messaging.Outbox;
using Subscriptions.Data.Configurations;

namespace Subscriptions.Data;

public class SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options)
    : AppDbContextBase(options), IDbContext
{
    private const string UniqueViolation = "23505";

    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<PlanFeatureDefinition> FeatureDefinitions => Set<PlanFeatureDefinition>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionPayment> Payments => Set<SubscriptionPayment>();
    public DbSet<SubscriptionInvoice> Invoices => Set<SubscriptionInvoice>();
    public DbSet<InvoiceNumberCounter> InvoiceNumberCounters => Set<InvoiceNumberCounter>();
    public DbSet<ScheduledPlanChange> ScheduledPlanChanges => Set<ScheduledPlanChange>();
    public DbSet<SubscriptionTransitionLog> TransitionLogs => Set<SubscriptionTransitionLog>();
    public DbSet<AdminActionAuditLog> AdminAuditLogs => Set<AdminActionAuditLog>();
    public DbSet<SubscriptionRefundRequest> RefundRequests => Set<SubscriptionRefundRequest>();
    public DbSet<PaymentInstructions> PaymentInstructions => Set<PaymentInstructions>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<SubscriptionPaymentReceipt> PaymentReceipts => Set<SubscriptionPaymentReceipt>();

    /// <summary>Append-only money ledger. Read with no-tracking queries; write only through <c>ILedgerRepository</c>.</summary>
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    /// <summary>
    /// Translates PostgreSQL unique violations (23505) into a 409 <see cref="ConflictException"/> with a safe message
    /// (no constraint/table names). The filtered unique indexes are the last line of defence against races.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureAppendOnly();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation } pg)
        {
            throw new ConflictException(UniqueViolationMessage(pg.ConstraintName));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// The ledger and payment receipts are insert-only: a Modified or Deleted row is a programming error, refused before
    /// anything reaches the database (EF also throws on modified properties, see the configurations).
    /// </summary>
    private void EnsureAppendOnly()
    {
        var offender = ChangeTracker.Entries()
            .FirstOrDefault(e => e.Entity is LedgerEntry or SubscriptionPaymentReceipt &&
                                 e.State is EntityState.Modified or EntityState.Deleted);

        if (offender is not null)
        {
            throw new InvalidOperationException(
                $"{offender.Entity.GetType().Name} rows are append-only; {offender.State} is not allowed. " +
                "Record a compensating entry instead.");
        }
    }

    internal static string UniqueViolationMessage(string? constraintName) => constraintName switch
    {
        SubscriptionConfiguration.OpenPerSubscriberIndex =>
            "Subscriber already has an active or pending subscription.",
        SubscriptionRefundRequestConfiguration.OneRefundPerSubscriptionIndex =>
            "A refund has already been requested or processed for this subscription.",
        ScheduledPlanChangeConfiguration.OnePendingPerSubscriptionIndex =>
            "A pending scheduled downgrade already exists. Cancel it first.",
        SubscriptionInvoiceConfiguration.OnePerPaymentIndex =>
            "An invoice has already been issued for this payment.",
        _ => "The request conflicts with an existing record."
    };

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("subscriptions");

        // Invoice numbers come from invoice_number_counters (gapless, per year), not from a sequence.
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        // Transactional outbox (SubscriptionActivated/Expired integration events consumed by Tenancy & co).
        builder.AddOutbox();
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
    }
}
