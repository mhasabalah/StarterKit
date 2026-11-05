namespace Subscriptions.Domains.OnlinePayments.Exceptions;

public class PaymentGatewayNotFoundException(string name)
    : NotFoundException($"Payment gateway '{name}' is not available.");

/// <summary>Bad webhook signature: 401. Nothing is stored.</summary>
public class InvalidWebhookSignatureException()
    : UnauthorizedException("Invalid webhook signature.");

/// <summary>Verified but unusable webhook body: 400. Nothing is stored.</summary>
public class InvalidWebhookPayloadException()
    : BadRequestException("Invalid webhook payload.");

/// <summary>
/// A verified event that must NOT be retried (unknown reference, amount mismatch, already settled): the inbox row is
/// kept as failed for an admin to look at.
/// </summary>
public class WebhookRejectedException(string reason) : Exception(reason);

/// <summary>The provider refused a call (refund / checkout) or could not be reached: 502, nothing was booked.</summary>
public class PaymentGatewayException : CustomException
{
    public PaymentGatewayException(string message, Exception? inner = null)
        : base(message, inner!, System.Net.HttpStatusCode.BadGateway)
    {
        ErrorCode = "payment_gateway_error";
    }
}
