namespace Subscriptions.Domains.Subscriptions.Features.GetPaymentProof;

public record GetPaymentProofQuery(Guid PaymentId, Guid ActorId, bool IsAdmin)
    : IQuery<(string FileUrl, string ContentType, string FileName)>;

public class GetPaymentProofHandler(
    SubscriptionsDbContext dbContext,
    ISender sender)
    : IQueryHandler<GetPaymentProofQuery, (string FileUrl, string ContentType, string FileName)>
{
    public async Task<(string FileUrl, string ContentType, string FileName)> Handle(
        GetPaymentProofQuery query,
        CancellationToken cancellationToken)
    {
        var paymentData = await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.Id == query.PaymentId)
            .Select(p => new
            {
                p.ProofAttachmentId,
                p.Subscription.SubscriberId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (paymentData is null || !paymentData.ProofAttachmentId.HasValue)
            throw new NotFoundException("Payment proof not found.");

        if (!query.IsAdmin && paymentData.SubscriberId != query.ActorId)
            throw new ForbiddenException("You are not allowed to access this proof.");

        // Ownership/admin checked above, so ask Attachment for a fresh short-lived signed URL (trusted query).
        // Never redirect to a stored URL: proofs are private and stored URLs may be public or expired.
        var readUrl = await sender.Send(
            new GetAttachmentReadUrlQuery(paymentData.ProofAttachmentId.Value), cancellationToken);

        return (readUrl.Url, readUrl.ContentType, readUrl.FileName);
    }
}
