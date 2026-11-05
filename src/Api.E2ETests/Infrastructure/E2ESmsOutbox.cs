using System.Collections.Concurrent;
using Identity.Services.Sms;

namespace Api.E2ETests.Infrastructure;

/// <summary>
/// Replaces the log-only Mock SMS provider in the E2E host and keeps every code it was asked to send, so tests can
/// complete real OTP flows (phone password reset, phone verification) over HTTP. Codes are keyed by E.164 number.
/// </summary>
public sealed class E2ESmsOutbox : ISmsService
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _sent = new(StringComparer.Ordinal);

    public Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        _sent.GetOrAdd(phoneNumber, _ => new ConcurrentQueue<string>()).Enqueue(code);
        return Task.CompletedTask;
    }

    /// <summary>The most recent code sent to <paramref name="phoneNumber"/>, or null.</summary>
    public string? LastCodeFor(string phoneNumber) =>
        _sent.TryGetValue(phoneNumber, out var codes) ? codes.LastOrDefault() : null;

    public int CountFor(string phoneNumber) => _sent.TryGetValue(phoneNumber, out var codes) ? codes.Count : 0;
}
