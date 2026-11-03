namespace Notification.Tests.Fakes;

/// <summary>Programmable channel: records every send; <see cref="Failures"/> are thrown in order, then it succeeds.</summary>
internal sealed class FakeChannelAdapter(NotificationChannel channel) : INotificationChannelAdapter
{
    private int _sends;

    public NotificationChannel Channel { get; } = channel;
    public Queue<Exception> Failures { get; } = new();
    public Exception? AlwaysThrow { get; set; }
    public int Attempts { get; private set; }
    public int Sends => _sends;
    public string? LastCode { get; private set; }
    public bool RequiresContact { get; set; } = true;

    public bool CanSend(string? email, string? phone)
        => !RequiresContact || (Channel == NotificationChannel.Email
            ? !string.IsNullOrWhiteSpace(email)
            : !string.IsNullOrWhiteSpace(phone));

    public Task SendAsync(NotificationMessage message, string? email, string? phone, CancellationToken ct)
    {
        lock (this) Attempts++;

        if (AlwaysThrow is not null)
            throw AlwaysThrow;

        lock (Failures)
        {
            if (Failures.Count > 0)
                throw Failures.Dequeue();
        }

        Interlocked.Increment(ref _sends);
        if (message.WhatsApp?.Model?.TryGetValue("code", out var code) == true)
            LastCode = code.ToString();

        return Task.CompletedTask;
    }
}
