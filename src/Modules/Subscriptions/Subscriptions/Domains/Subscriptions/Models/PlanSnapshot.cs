namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>
/// The plan terms copied onto a subscription (price, limits, features) so later plan edits never change what a
/// subscriber already bought. Built by <see cref="From"/> (one place instead of three copies).
/// </summary>
public sealed record PlanSnapshot(
    Guid PlanId,
    string PlanName,
    decimal Price,
    string Currency,
    int BillingCycleInMonths,
    int? MaxItems,
    int? MaxStorageGb,
    int? MaxUsers,
    string FeaturesJson)
{
    /// <param name="plan">The plan.</param>
    /// <param name="effectivePrice">The price to snapshot (usually <c>PricingPolicy.PlanPriceAt(plan, now)</c>).</param>
    public static PlanSnapshot From(Plan plan, decimal effectivePrice) => new(
        plan.Id,
        plan.Name.En,
        effectivePrice,
        plan.Currency,
        plan.BillingCycleInMonths,
        plan.MaxItems,
        plan.MaxStorageGb,
        plan.MaxUsers,
        Plans.Models.FeaturesJson.FromPlan(plan));
}
