using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

/// <summary>
/// The admin proof-review queue: submitted payments, oldest first by default. All filters run in SQL.
/// <para><see cref="Method"/> matches the payment method type or display name (case-insensitive).
/// <see cref="Search"/>: subscriber name/email snapshot or plan name contains, or an exact payment / subscription /
/// subscriber id. Dates filter the submission time, [From, To).</para>
/// </summary>
public record GetPendingPaymentsQuery : BaseQuery, IQuery<PaginatedResult<PendingPaymentDto>>
{
    public string? Method { get; init; }
    public Guid? PaymentMethodId { get; init; }
    public string? Purpose { get; init; }
    public DateTimeOffset? SubmittedFrom { get; init; }
    public DateTimeOffset? SubmittedTo { get; init; }
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public string? Search { get; init; }
    public bool? DuplicateProofOnly { get; init; }
}
