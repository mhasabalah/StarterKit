using System.Text.Json.Serialization;

namespace Shared.Email.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmailCulture
{
    En = 0,
    Ar = 1
}
