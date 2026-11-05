using Shared.Contracts.Localization;

namespace Subscriptions.Contracts.Subscriptions.Commands.Plans;

public record UpdatePlanCommand(
    Guid PlanId,
    LocalizedText Name,
    LocalizedText? Description,
    string Currency,
    int BillingCycleInMonths,
    int? MaxItems,
    int? MaxStorageGb,
    int? MaxUsers,
    bool IsActive,
    bool IsPublic,
    long Version,
    decimal? DiscountPercent = null,
    int TrialDays = 0) : ICommand;
