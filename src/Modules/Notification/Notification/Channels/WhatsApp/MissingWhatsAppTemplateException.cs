namespace Notification.Channels.WhatsApp;

internal sealed class MissingWhatsAppTemplateException(string templateName)
    : Exception($"No WhatsApp template configured for '{templateName}'. Register it under WhatsApp:Templates.")
{
}
