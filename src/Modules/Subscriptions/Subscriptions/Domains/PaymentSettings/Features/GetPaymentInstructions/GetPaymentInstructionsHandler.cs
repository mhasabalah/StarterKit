using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.PaymentSettings.Features.GetPaymentInstructions;

public class GetPaymentInstructionsHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetPaymentInstructionsQuery, PaymentInstructionsDto?>
{
    public async Task<PaymentInstructionsDto?> Handle(
        GetPaymentInstructionsQuery query,
        CancellationToken cancellationToken)
    {
        var instructions = await dbContext.PaymentInstructions
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (instructions is null)
            return null;

        return new PaymentInstructionsDto(
            instructions.SupportWhatsApp,
            instructions.SupportEmail);
    }
}
