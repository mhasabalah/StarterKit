using System.Text.Json.Serialization;

namespace Catalog.Contracts.AttributeTypes.Enums;

/// <summary>How a product attribute value is validated (values are always stored as text).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AttributeDataType
{
    [LocalizedDescription("Text", "نص")]
    Text = 1,

    [LocalizedDescription("Number", "رقم")]
    Number = 2,

    [LocalizedDescription("Yes / No", "نعم / لا")]
    Boolean = 3
}
