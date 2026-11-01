using Microsoft.Extensions.Localization;
using Shared.Core.Resources;

namespace Shared.Core.Localization.Services;

public class LocalizationService(IStringLocalizer<SharedResource> localizer) : ILocalizationService
{
    public string this[string key] => localizer[key].Value;

    public string this[string key, params object[] arguments] => localizer[key, arguments].Value;

    public LocalizedString GetString(string key) => localizer[key];
}
