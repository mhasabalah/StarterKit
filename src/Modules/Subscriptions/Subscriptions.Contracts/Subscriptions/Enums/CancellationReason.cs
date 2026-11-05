namespace Subscriptions.Contracts.Subscriptions.Enums;

public enum CancellationReason
{
    NotSpecified = 0,
    PriceTooHigh = 1,
    NotUsing = 2,
    SwitchingPlan = 3,
    ServiceIssue = 4,
    BusinessClosed = 5,
    Other = 6
}
