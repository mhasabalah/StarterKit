using Microsoft.Extensions.Localization;

namespace Shared.Core.Localization.Services;

public interface ILocalizationService
{
    string this[string key] { get; }
    string this[string key, params object[] arguments] { get; }
    LocalizedString GetString(string key);
}
