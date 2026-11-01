using System.Text.Json.Serialization;

namespace Shared.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortBy
{
    Descending = 1,
    Ascending = 2
}
