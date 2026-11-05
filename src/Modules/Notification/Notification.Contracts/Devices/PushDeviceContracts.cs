namespace Notification.Contracts.Devices;

/// <summary>A registered push device of the caller. The FCM token is never returned in full.</summary>
public sealed record PushDeviceDto(
    Guid Id,
    PushPlatform Platform,
    string TokenHint,
    string? AppVersion,
    string? Locale,
    bool IsCurrentSession,
    DateTime CreatedAt,
    DateTime LastSeenAt);

/// <summary>Body of <c>PUT /notifications/devices</c>.</summary>
public sealed record RegisterPushDeviceRequest(string Token, PushPlatform Platform, string? AppVersion, string? Locale);

/// <summary>
/// Registers (or refreshes) the caller's push device. Upsert by token: the app calls it on every start and whenever FCM
/// rotates the token. A token registered by another account moves to the caller (shared device, sign-out/sign-in).
/// The device is bound to the caller's session (<c>sid</c>): when that session ends, the device stops receiving pushes.
/// </summary>
public sealed record RegisterPushDeviceCommand(string Token, PushPlatform Platform, string? AppVersion, string? Locale)
    : ICommand<RegisterPushDeviceResult>
{
    public const int TokenMaxLength = 1024;
    public const int AppVersionMaxLength = 50;
    public const int LocaleMaxLength = 20;
}

public sealed record RegisterPushDeviceResult(Guid DeviceId, bool Created);

/// <summary>Removes one of the caller's devices (call it on sign-out). A foreign or unknown id is 404.</summary>
public sealed record UnregisterPushDeviceCommand(Guid DeviceId) : ICommand<UnregisterPushDeviceResult>;

public sealed record UnregisterPushDeviceResult(Guid DeviceId);

/// <summary>The caller's registered devices, most recently seen first.</summary>
public sealed record GetMyPushDevicesQuery : IQuery<List<PushDeviceDto>>;
