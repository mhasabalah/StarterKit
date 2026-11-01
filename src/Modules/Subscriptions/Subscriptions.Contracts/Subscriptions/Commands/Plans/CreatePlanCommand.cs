using Shared.Contracts.Localization;

namespace Subscriptions.Contracts.Subscriptions.Commands.Plans;

public record CreatePlanCommand(
    LocalizedText Name,
    LocalizedText? Description,
    decimal Price,
    string Currency,
    int BillingCycleInMonths,
    int? MaxItems,
    int? MaxStorageGb,
    int? MaxUsers,
    bool IsPublic,
    decimal? DiscountPercent = null) : ICommand<Guid>;
