using MailKit.Net.Smtp;
using Polly.Timeout;

namespace Notification.Dispatch;

public enum DeliveryFailureKind
{
    /// <summary>The request can never succeed (bad template, invalid recipient, 4xx). Do not retry; fall back.</summary>
    Permanent = 0,

    /// <summary>Provider definitely did not deliver (5xx / 429 / refused connection). Retry; fall back after N attempts.</summary>
    Transient = 1,

    /// <summary>Outcome unknown (timeout) - the message may have been delivered. Retry the SAME channel, never fall back.</summary>
    Ambiguous = 2
}

/// <summary>Maps a channel exception to a retry/fallback decision for <see cref="NotificationDispatchJob"/>.</summary>
public static class DeliveryFailureClassifier
{
    public static DeliveryFailureKind Classify(Exception ex)
    {
        switch (ex)
        {
            case WhatsAppApiException wa:
                return wa.IsPermanent ? DeliveryFailureKind.Permanent : DeliveryFailureKind.Transient;

            case MissingWhatsAppTemplateException:
            case ArgumentException:
            case FormatException:
                return DeliveryFailureKind.Permanent;

            case SmtpCommandException smtp:
                // 5xx SMTP replies (unknown mailbox, rejected sender, policy) are permanent; 4xx are transient.
                return (int)smtp.StatusCode >= 500 ? DeliveryFailureKind.Permanent : DeliveryFailureKind.Transient;

            case TimeoutRejectedException:
            case TimeoutException:
                return DeliveryFailureKind.Ambiguous;

            // HttpClient timeouts surface as TaskCanceledException; a shutdown-cancelled send is equally
            // unknown. Either way the request may already have reached the provider.
            case OperationCanceledException:
                return DeliveryFailureKind.Ambiguous;

            case HttpRequestException { StatusCode: { } status }:
                return (int)status is >= 400 and < 500 && (int)status is not (408 or 429)
                    ? DeliveryFailureKind.Permanent
                    : DeliveryFailureKind.Transient;

            case HttpRequestException:
            case SmtpProtocolException:
            case System.Net.Sockets.SocketException:
            case IOException:
                return DeliveryFailureKind.Transient;

            default:
                return DeliveryFailureKind.Transient;
        }
    }
}
