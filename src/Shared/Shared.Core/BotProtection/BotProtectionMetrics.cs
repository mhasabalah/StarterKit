using System.Diagnostics.Metrics;

namespace Shared.Core.BotProtection;

/// <summary>Counter <c>bot_protection.verifications</c> tagged <c>outcome</c> (success|failure|provider_error) and <c>action</c>.</summary>
public sealed class BotProtectionMetrics
{
    public const string MeterName = "StarterKit.BotProtection";

    private readonly Counter<long> _verifications;

    public BotProtectionMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _verifications = meter.CreateCounter<long>("bot_protection.verifications", description: "Human verification attempts by outcome.");
    }

    public void Record(string outcome, string action) =>
        _verifications.Add(1, new KeyValuePair<string, object?>("outcome", outcome), new KeyValuePair<string, object?>("action", action));
}
