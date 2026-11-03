using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;
using Subscriptions.Domains.Subscriptions.Jobs;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Issues invoices and credit notes INSIDE the caller's transaction, in the same SaveChanges as the business change
/// (approval, renewal, upgrade, refund). The number comes from the per-year counter row locked with
/// <c>SELECT ... FOR UPDATE</c>, so numbers are gapless: a rolled-back transaction rolls the number back too.
/// PDF rendering and email happen afterwards in <see cref="InvoiceDeliveryJob"/> (call <see cref="EnqueueDelivery"/>
/// after commit).
/// </summary>
public interface IInvoiceIssuer
{
    /// <summary>Issues the invoice for a payment (activation, renewal or proration), snapshotting buyer/seller/plan/tax.</summary>
    Task<SubscriptionInvoice> IssueForPaymentAsync(
        Subscription subscription,
        SubscriptionPayment payment,
        CancellationToken ct = default);

    /// <summary>Issues a credit note against <paramref name="original"/> (tracked). Null amount credits the remainder.</summary>
    Task<SubscriptionInvoice> IssueCreditNoteAsync(
        SubscriptionInvoice original,
        decimal? grossAmount,
        string reason,
        CancellationToken ct = default);

    /// <summary>Queues PDF generation + email. Call only after the issuing transaction committed.</summary>
    void EnqueueDelivery(Guid invoiceId, bool forceResend = false);
}

public sealed class InvoiceIssuer(
    SubscriptionsDbContext dbContext,
    ISubscriberInfoProvider subscriberInfoProvider,
    IOptions<SubscriptionOptions> options,
    IBackgroundJobClient backgroundJobs,
    ILogger<InvoiceIssuer> logger)
    : IInvoiceIssuer
{
    public async Task<SubscriptionInvoice> IssueForPaymentAsync(
        Subscription subscription,
        SubscriptionPayment payment,
        CancellationToken ct = default)
    {
        if (payment.SubscriptionId != subscription.Id)
            throw new InvalidOperationException("The payment does not belong to the subscription.");

        var alreadyIssued = await dbContext.Invoices.AnyAsync(
            x => x.PaymentId == payment.Id && x.Type != InvoiceType.CreditNote, ct);
        if (alreadyIssued)
            throw new ConflictException("An invoice has already been issued for this payment.");

        var now = DateTimeOffset.UtcNow;
        var type = payment.Purpose switch
        {
            PaymentPurpose.Activation => InvoiceType.Activation,
            PaymentPurpose.Renewal => InvoiceType.Renewal,
            PaymentPurpose.Proration => InvoiceType.Proration,
            _ => throw new InvalidOperationException($"Unsupported payment purpose {payment.Purpose}.")
        };

        var (periodStart, periodEnd) = type == InvoiceType.Proration
            ? (payment.CreatedAt, subscription.RenewalDate)
            : (subscription.CurrentPeriodStart, subscription.RenewalDate);

        var settings = options.Value;
        var buyer = await ResolveBuyerAsync(subscription.SubscriberId, ct);
        var draft = new InvoiceDraft(
            subscription.Id,
            subscription.SubscriberId,
            payment.Id,
            type,
            Describe(type, subscription.PlanName, periodStart, periodEnd, payment.IncludedProrationAmount, payment.Currency),
            subscription.PlanId,
            subscription.PlanName,
            subscription.BillingCycleInMonths,
            periodStart,
            periodEnd,
            buyer,
            Seller(settings.Invoice),
            TaxCalculator.FromGross(payment.Amount, settings.Tax.ToPolicy()),
            payment.Currency);

        // Allocate the number last: the counter row stays locked until commit, so keep the window short.
        var number = await AllocateNumberAsync(now.Year, ct);
        var invoice = SubscriptionInvoice.Issue(draft, number, now);
        dbContext.Invoices.Add(invoice);

        logger.LogInformation(
            "Issued {InvoiceType} invoice {InvoiceNumber} for payment {PaymentId} ({Total} {Currency})",
            type, number, payment.Id, invoice.TotalAmount, invoice.Currency);

        return invoice;
    }

    public async Task<SubscriptionInvoice> IssueCreditNoteAsync(
        SubscriptionInvoice original,
        decimal? grossAmount,
        string reason,
        CancellationToken ct = default)
    {
        if (original.IsCreditNote)
            throw new ConflictException("A credit note cannot be credited.");

        var earlier = await dbContext.Invoices
            .Where(x => x.OriginalInvoiceId == original.Id && x.Type == InvoiceType.CreditNote)
            .Select(x => new { x.NetAmount, x.TaxAmount, x.TotalAmount })
            .ToListAsync(ct);

        var credited = new TaxBreakdown(
            -earlier.Sum(x => x.NetAmount),
            -earlier.Sum(x => x.TaxAmount),
            -earlier.Sum(x => x.TotalAmount),
            original.TaxRate,
            original.PricesIncludeTax);

        var amounts = CreditNotePolicy.Compute(original.Amounts, credited, grossAmount);

        var now = DateTimeOffset.UtcNow;
        var number = await AllocateNumberAsync(now.Year, ct);
        var note = SubscriptionInvoice.IssueCreditNote(original, amounts, number, reason, now);
        dbContext.Invoices.Add(note);

        logger.LogInformation(
            "Issued credit note {CreditNoteNumber} for invoice {InvoiceNumber} ({Total} {Currency})",
            number, original.InvoiceNumber, note.TotalAmount, note.Currency);

        return note;
    }

    public void EnqueueDelivery(Guid invoiceId, bool forceResend = false) =>
        backgroundJobs.Enqueue<InvoiceDeliveryJob>(job => job.DeliverAsync(invoiceId, forceResend));

    internal static string Describe(
        InvoiceType type,
        string planName,
        DateTimeOffset? periodStart,
        DateTimeOffset? periodEnd,
        decimal includedProration,
        string currency)
    {
        var period = periodStart is { } start && periodEnd is { } end
            ? $" ({start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to {end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})"
            : string.Empty;

        return type switch
        {
            InvoiceType.Activation => $"Subscription activation - {planName}{period}",
            InvoiceType.Renewal when includedProration > 0m =>
                $"Subscription renewal - {planName}{period}, incl. deferred upgrade proration {includedProration.ToString("F2", CultureInfo.InvariantCulture)} {currency}",
            InvoiceType.Renewal => $"Subscription renewal - {planName}{period}",
            InvoiceType.Proration => $"Upgrade proration - {planName}{period}",
            _ => planName
        };
    }

    private async Task<InvoiceParty> ResolveBuyerAsync(Guid subscriberId, CancellationToken ct)
    {
        var subscriber = await subscriberInfoProvider.GetAsync(subscriberId, ct);
        var name = subscriber?.BillingName ?? subscriber?.DisplayName;
        return new InvoiceParty(
            string.IsNullOrWhiteSpace(name) ? "N/A" : name,
            subscriber?.Email,
            subscriber?.BillingAddress,
            subscriber?.TaxId);
    }

    private static InvoiceParty Seller(InvoiceBrandingOptions branding) =>
        new(branding.CompanyName, branding.SupportEmail, branding.Address, branding.TaxId);

    /// <summary>
    /// Next gapless number for <paramref name="year"/>: ensure the counter row exists, lock it (FOR UPDATE), bump it.
    /// Must run in the transaction that inserts the invoice.
    /// </summary>
    private async Task<string> AllocateNumberAsync(int year, CancellationToken ct)
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Invoice numbers must be allocated inside the transaction that inserts the invoice.");

        var entity = dbContext.Model.FindEntityType(typeof(InvoiceNumberCounter))!;
        var table = $"\"{entity.GetSchema() ?? "subscriptions"}\".\"{entity.GetTableName()}\"";
        var connection = dbContext.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();

        await ExecuteAsync(connection, dbTransaction,
            $"INSERT INTO {table} (year, last_value) VALUES (@year, 0) ON CONFLICT (year) DO NOTHING", year, ct);

        var current = (long)(await ScalarAsync(connection, dbTransaction,
            $"SELECT last_value FROM {table} WHERE year = @year FOR UPDATE", year, ct))!;

        var next = current + 1;
        await ExecuteAsync(connection, dbTransaction,
            $"UPDATE {table} SET last_value = {next.ToString(CultureInfo.InvariantCulture)} WHERE year = @year", year, ct);

        return InvoiceNumberFormat.Format(options.Value.Invoice.NumberPrefix, year, next);
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql, int year, CancellationToken ct)
    {
        await using var command = CreateCommand(connection, transaction, sql, year);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, DbTransaction transaction, string sql, int year, CancellationToken ct)
    {
        await using var command = CreateCommand(connection, transaction, sql, year);
        return await command.ExecuteScalarAsync(ct);
    }

    private static DbCommand CreateCommand(DbConnection connection, DbTransaction transaction, string sql, int year)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "year";
        parameter.Value = year;
        command.Parameters.Add(parameter);
        return command;
    }
}
