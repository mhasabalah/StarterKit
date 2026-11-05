namespace Notification.Features.Preferences;

/// <summary>Validation shared by the "my" and admin preference update endpoints.</summary>
public static class ChannelOrderRules
{
    /// <summary>Returns an error message, or null when <paramref name="channelOrder"/> is acceptable.</summary>
    public static string? Validate(
        NotificationChannel[]? channelOrder,
        IEnumerable<INotificationChannelAdapter> adapters)
    {
        if (channelOrder is not { Length: > 0 })
            return "channelOrder must not be empty.";

        if (channelOrder.Any(c => !Enum.IsDefined(c)))
            return "channelOrder contains an unknown channel.";

        // In-app and push are delivered IN ADDITION to the chain (inbox always, push per PushEnabled), never instead of it.
        var additive = channelOrder.Where(c => c.IsAdditive()).ToArray();
        if (additive.Length > 0)
            return $"{string.Join(", ", additive)} cannot be part of channelOrder: in-app and push are always delivered " +
                   "alongside it (use pushEnabled to turn push off).";

        if (channelOrder.Distinct().Count() != channelOrder.Length)
            return "channelOrder must not contain duplicates.";

        // A channel without a registered adapter (e.g. Sms today) would be silently skipped at dispatch.
        var supported = adapters.Select(a => a.Channel).ToHashSet();
        var unsupported = channelOrder.Where(c => !supported.Contains(c)).ToArray();
        if (unsupported.Length > 0)
            return $"Unsupported channel(s): {string.Join(", ", unsupported)}. " +
                   $"Supported: {string.Join(", ", supported.OrderBy(c => c))}.";

        return null;
    }
}
