using System.Text.Json.Serialization;
using Shared.Contracts.Attributes;

namespace Notification.Contracts.Enums;

/// <summary>Platform of a registered push device. All three are delivered through FCM (iOS via the APNs key in Firebase).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PushPlatform
{
    [LocalizedDescription("Android", "أندرويد")]
    Android = 1,

    [LocalizedDescription("iOS", "آي أو إس")]
    Ios = 2,

    [LocalizedDescription("Web", "الويب")]
    Web = 3,
}
