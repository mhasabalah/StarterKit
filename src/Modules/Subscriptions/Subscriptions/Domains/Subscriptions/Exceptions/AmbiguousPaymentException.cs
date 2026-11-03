namespace Subscriptions.Domains.Subscriptions.Exceptions;

/// <summary>Several payments match and the request did not say which one (send PaymentId).</summary>
public class AmbiguousPaymentException()
    : BadRequestException("More than one payment is awaiting review for this subscription; specify PaymentId.");
