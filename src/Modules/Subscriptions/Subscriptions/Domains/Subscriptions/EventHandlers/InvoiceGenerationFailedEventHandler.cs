namespace Subscriptions.Domains.Subscriptions.EventHandlers;

public class InvoiceGenerationFailedEventHandler(
    ILogger<InvoiceGenerationFailedEventHandler> logger)
    : INotificationHandler<InvoiceGenerationFailedEvent>
{
    public Task Handle(InvoiceGenerationFailedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Invoice generation failed. InvoiceId={InvoiceId} SubscriptionId={SubscriptionId} " +
            "SubscriberId={SubscriberId} RetryCount={RetryCount} Error={ErrorMessage}",
            notification.InvoiceId,
            notification.SubscriptionId,
            notification.SubscriberId,
            notification.RetryCount,
            notification.ErrorMessage);

        return Task.CompletedTask;
    }
}
