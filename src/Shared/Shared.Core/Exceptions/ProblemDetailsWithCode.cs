using System.Text.Json.Serialization;

namespace Shared.Core.Exceptions;

// Fully qualified: a Shared.Core.Exceptions.ProblemDetails namespace would otherwise shadow the type name.
public class ProblemDetailsWithCode : Microsoft.AspNetCore.Mvc.ProblemDetails
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }
}
