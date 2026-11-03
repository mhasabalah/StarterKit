namespace Notification.Dispatch;

/// <summary>
/// Serialization of <see cref="NotificationMessage"/> for <c>notification_log.message_json</c>.
/// Attachment BYTES are never persisted (invoice PDFs are large and contain PII); only a reference
/// (file name + content type) is kept. A manual re-enqueue therefore sends without attachments.
/// </summary>
public static class NotificationMessageSerializer
{
    public static string SerializeForLog(NotificationMessage message)
    {
        var persisted = message.Email?.Attachments is { Length: > 0 } attachments
            ? message with
            {
                Email = message.Email with
                {
                    Attachments = attachments
                        .Select(a => new EmailAttachment(a.FileName, [], a.ContentType))
                        .ToArray()
                }
            }
            : message;

        return JsonSerializer.Serialize(persisted);
    }

    /// <summary>Deserializes a logged message and drops attachment stubs (they carry no content).</summary>
    public static NotificationMessage? DeserializeFromLog(string json)
    {
        var message = JsonSerializer.Deserialize<NotificationMessage>(json);
        if (message?.Email?.Attachments is { Length: > 0 } attachments)
        {
            var usable = attachments.Where(a => a.Content is { Length: > 0 }).ToArray();
            message = message with { Email = message.Email with { Attachments = usable.Length > 0 ? usable : null } };
        }

        return message;
    }
}
