namespace Notification.Contracts.Enums;

/// <summary>
/// Maps a <see cref="WhatsAppTemplate"/> enum member to its key in
/// <c>WhatsApp:Templates</c> configuration (appsettings / secrets).
/// Every enum member must carry this attribute — startup validation will throw if any are missing.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class WhatsAppKeyAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}
