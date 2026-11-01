using System.Text.Json.Serialization;

namespace Shared.Contracts.Localization;

public record LocalizedText
{
    public string En { get; init; } = default!;
    public string Ar { get; init; } = default!;

    private LocalizedText() { }

    [JsonConstructor]
    public LocalizedText(string en, string ar)
    {
        En = en;
        Ar = ar;
    }

    public static LocalizedText Create(string en, string ar) =>
        new(en, ar);

    public string Resolve() =>
        System.Threading.Thread.CurrentThread.CurrentUICulture.Name
            .StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? Ar : En;
}
