using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Notification.Tests.Dispatch;

/// <summary>Runs only when a real Firebase project is configured; skipped everywhere else (CI included).</summary>
public sealed class FcmLiveFactAttribute : FactAttribute
{
    public const string CredentialsVariable = "FCM_CREDENTIALS_FILE";
    public const string TokenVariable = "FCM_TEST_TOKEN";

    public FcmLiveFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CredentialsVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TokenVariable)))
            Skip = $"Live FCM test: set {CredentialsVariable} (service-account JSON path) and {TokenVariable} (a device token).";
    }
}

/// <summary>
/// The real <see cref="FcmPushProvider"/> against Firebase: credentials load, a real device token is accepted (a
/// notification appears on that device), and a token FCM does not know is never reported as delivered. Manual:
/// <c>wiki/Modules/Notification-Realtime-and-Push.md</c> ("FCM / APNs setup").
/// </summary>
public sealed class FcmLiveTests
{
    private static FcmPushProvider Provider() => new(
        MsOptions.Create(new PushOptions
        {
            Provider = PushProvider.Fcm,
            CredentialsFile = Environment.GetEnvironmentVariable(FcmLiveFactAttribute.CredentialsVariable)
        }),
        NullLogger<FcmPushProvider>.Instance);

    private static PushMessage Message(string title) => new(
        title,
        $"StarterKit live FCM test at {DateTime.UtcNow:HH:mm:ss} UTC",
        new Dictionary<string, string> { ["category"] = "test.live", ["correlationId"] = Guid.NewGuid().ToString("D") },
        Guid.NewGuid().ToString("N"),
        TimeSpan.FromMinutes(10));

    [FcmLiveFact]
    public async Task A_real_device_token_is_delivered_and_an_unknown_one_never_is()
    {
        using var provider = Provider();
        var device = new PushTarget(Guid.NewGuid(), Environment.GetEnvironmentVariable(FcmLiveFactAttribute.TokenVariable)!,
            PushPlatform.Web);
        // Well-formed but never issued by FCM.
        var unknown = new PushTarget(Guid.NewGuid(), "dQw4w9WgXcQ:APA91b" + new string('x', 140), PushPlatform.Android);

        var results = await provider.SendAsync(Message("StarterKit push works"), [device, unknown], CancellationToken.None);

        results.Single(r => r.DeviceId == device.DeviceId).Should()
            .Match<PushSendResult>(r => r.Outcome == PushSendOutcome.Delivered, "the real device token must be accepted");
        results.Single(r => r.DeviceId == unknown.DeviceId).Outcome.Should()
            .BeOneOf(PushSendOutcome.InvalidToken, PushSendOutcome.Rejected);
    }
}
