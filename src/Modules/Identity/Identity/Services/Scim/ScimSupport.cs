using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Identity.Contracts.Scim;
using Identity.Domains.Scim.Models;

namespace Identity.Services.Scim;

/// <summary>An error in SCIM's own format (RFC 7644 §3.12), never the platform envelope: IdPs parse it.</summary>
public sealed class ScimException(int status, string detail, string? scimType = null) : Exception(detail)
{
    public int Status { get; } = status;
    public string? ScimType { get; } = scimType;

    public static ScimException NotFound(string what) => new(404, $"{what} not found.");
    public static ScimException Uniqueness(string detail) => new(409, detail, "uniqueness");
    public static ScimException InvalidValue(string detail) => new(400, detail, "invalidValue");
    public static ScimException InvalidFilter(string detail) => new(400, detail, "invalidFilter");
    public static ScimException InvalidSyntax(string detail) => new(400, detail, "invalidSyntax");
}

public static class ScimSchemas
{
    public const string User = "urn:ietf:params:scim:schemas:core:2.0:User";
    public const string Group = "urn:ietf:params:scim:schemas:core:2.0:Group";
    public const string ListResponse = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
    public const string PatchOp = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
    public const string Error = "urn:ietf:params:scim:api:messages:2.0:Error";
    public const string ServiceProviderConfig = "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig";
    public const string ResourceType = "urn:ietf:params:scim:schemas:core:2.0:ResourceType";
    public const string ContentType = "application/scim+json";
}

/// <summary>Resource ⇄ JSON. Attribute names follow RFC 7643; unknown attributes are ignored, as IdPs expect.</summary>
public static class ScimJson
{
    public static JsonObject Error(int status, string detail, string? scimType)
    {
        var error = new JsonObject
        {
            ["schemas"] = new JsonArray(ScimSchemas.Error),
            ["status"] = status.ToString(CultureInfo.InvariantCulture),
            ["detail"] = detail
        };
        if (scimType is not null)
            error["scimType"] = scimType;
        return error;
    }

    public static JsonObject User(ScimUser scim, ApplicationUser user, string baseUrl) => new()
    {
        ["schemas"] = new JsonArray(ScimSchemas.User),
        ["id"] = scim.UserId.ToString(),
        ["externalId"] = scim.ExternalId,
        ["userName"] = scim.UserName,
        ["active"] = scim.Active,
        ["displayName"] = string.IsNullOrWhiteSpace(user.FullName) ? scim.UserName : user.FullName,
        ["name"] = new JsonObject { ["givenName"] = user.FirstName, ["familyName"] = user.LastName },
        ["emails"] = new JsonArray(new JsonObject { ["value"] = user.Email, ["type"] = "work", ["primary"] = true }),
        ["meta"] = Meta("User", scim.Created, scim.LastModified, scim.Revision, $"{baseUrl}/Users/{scim.UserId}")
    };

    public static JsonObject Group(ScimGroup group, string baseUrl, bool excludeMembers) => new()
    {
        ["schemas"] = new JsonArray(ScimSchemas.Group),
        ["id"] = group.Id.ToString(),
        ["externalId"] = group.ExternalId,
        ["displayName"] = group.DisplayName,
        ["members"] = excludeMembers
            ? null
            : new JsonArray(group.Members.Select(m => (JsonNode)new JsonObject
            {
                ["value"] = m.ToString(), ["$ref"] = $"{baseUrl}/Users/{m}", ["type"] = "User"
            }).ToArray()),
        ["meta"] = Meta("Group", group.Created, group.LastModified, group.Revision, $"{baseUrl}/Groups/{group.Id}")
    };

    public static JsonObject List(IEnumerable<JsonObject> resources, int total, int startIndex)
    {
        var items = resources.Cast<JsonNode>().ToArray();
        return new JsonObject
        {
            ["schemas"] = new JsonArray(ScimSchemas.ListResponse),
            ["totalResults"] = total,
            ["startIndex"] = startIndex,
            ["itemsPerPage"] = items.Length,
            ["Resources"] = new JsonArray(items)
        };
    }

    public static string ETag(long revision) => $"W/\"{revision.ToString(CultureInfo.InvariantCulture)}\"";

    private static JsonObject Meta(string type, DateTime created, DateTime modified, long revision, string location) => new()
    {
        ["resourceType"] = type,
        ["created"] = created.ToString("O", CultureInfo.InvariantCulture),
        ["lastModified"] = modified.ToString("O", CultureInfo.InvariantCulture),
        ["version"] = ETag(revision),
        ["location"] = location
    };

    // ----- reading -----

    public static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        _ => null
    };

    /// <summary>Booleans arrive as true/false or, from Entra ID, as the strings "True"/"False".</summary>
    public static bool? Bool(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed) => parsed,
        _ => null
    };

    /// <summary>The primary (else work, else first) email of a User resource.</summary>
    public static string? PrimaryEmail(JsonObject resource)
    {
        if (resource["emails"] is not JsonArray emails)
            return null;

        var objects = emails.OfType<JsonObject>().ToList();
        var chosen = objects.FirstOrDefault(e => Bool(e["primary"]) == true)
                     ?? objects.FirstOrDefault(e => string.Equals(Text(e["type"]), "work", StringComparison.OrdinalIgnoreCase))
                     ?? objects.FirstOrDefault();
        return chosen is null ? null : Text(chosen["value"]);
    }

    /// <summary>Case-insensitive lookup (SCIM attribute names are case-insensitive, IdPs vary).</summary>
    public static JsonNode? Get(JsonObject obj, string name) =>
        obj.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>
/// The subset of SCIM filters IdPs actually send when reconciling: <c>attr eq "value"</c>, joined by <c>and</c>.
/// Supported attributes: userName, externalId, id, displayName, emails.value / emails[type eq "work"].value.
/// Anything else is a 400 <c>invalidFilter</c> (never silently "match all").
/// </summary>
public static partial class ScimFilter
{
    public sealed record Clause(string Attribute, string Value);

    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "userName", "externalId", "id", "displayName", "emails.value"
    };

    public static IReadOnlyList<Clause> Parse(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return [];
        if (filter.Length > 1024)
            throw ScimException.InvalidFilter("The filter is too long.");

        var clauses = new List<Clause>();
        foreach (var part in AndSplitter().Split(filter.Trim()))
        {
            var match = ClausePattern().Match(part.Trim());
            if (!match.Success)
                throw ScimException.InvalidFilter($"Unsupported filter '{part.Trim()}'. Use attribute eq \"value\".");

            var attribute = match.Groups["attr"].Value;
            if (attribute.StartsWith("emails[", StringComparison.OrdinalIgnoreCase))
                attribute = "emails.value";

            if (!Supported.Contains(attribute))
                throw ScimException.InvalidFilter($"Filtering on '{attribute}' is not supported.");

            clauses.Add(new Clause(attribute, match.Groups["value"].Value.Replace("\\\"", "\"", StringComparison.Ordinal)));
        }

        return clauses;
    }

    [GeneratedRegex(@"\s+and\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex AndSplitter();

    [GeneratedRegex("""^(?<attr>[A-Za-z][A-Za-z0-9.]*|emails\[type eq "?\w+"?\]\.value)\s+eq\s+"(?<value>(?:[^"\\]|\\.)*)"$""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ClausePattern();
}
