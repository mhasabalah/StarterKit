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
    public static ScimException InvalidPath(string detail) => new(400, detail, "invalidPath");
    public static ScimException NoTarget(string detail) => new(400, detail, "noTarget");
}

public static class ScimSchemas
{
    public const string User = "urn:ietf:params:scim:schemas:core:2.0:User";
    public const string Group = "urn:ietf:params:scim:schemas:core:2.0:Group";
    public const string EnterpriseUser = "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User";
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

    /// <param name="managers">
    /// Display names of the managers that still exist in the directory. When given, a manager that is not in it is
    /// omitted (the referenced user was deleted); null = do not verify.
    /// </param>
    public static JsonObject User(ScimUser scim, ApplicationUser user, string baseUrl,
        IReadOnlyDictionary<Guid, string?>? managers = null)
    {
        var resource = UserCore(scim, user, baseUrl);
        var enterprise = EnterpriseJson(scim.Enterprise, baseUrl, managers);
        if (enterprise.Count > 0)
        {
            ((JsonArray)resource["schemas"]!).Add(ScimSchemas.EnterpriseUser);
            resource[ScimSchemas.EnterpriseUser] = enterprise;
        }

        return resource;
    }

    private static JsonObject EnterpriseJson(ScimEnterprise e, string baseUrl, IReadOnlyDictionary<Guid, string?>? managers)
    {
        var json = new JsonObject();
        if (e.EmployeeNumber is not null)
            json["employeeNumber"] = e.EmployeeNumber;
        if (e.CostCenter is not null)
            json["costCenter"] = e.CostCenter;
        if (e.Organization is not null)
            json["organization"] = e.Organization;
        if (e.Division is not null)
            json["division"] = e.Division;
        if (e.Department is not null)
            json["department"] = e.Department;
        if (e.ManagerId is { } manager && (managers is null || managers.ContainsKey(manager)))
        {
            var reference = new JsonObject { ["value"] = manager.ToString(), ["$ref"] = $"{baseUrl}/Users/{manager}" };
            if (managers?[manager] is { } displayName)
                reference["displayName"] = displayName;
            json["manager"] = reference;
        }

        return json;
    }

    private static JsonObject UserCore(ScimUser scim, ApplicationUser user, string baseUrl) => new()
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
/// Which attribute names a User PATCH may address (RFC 7644 §3.5.2: an unknown path is a 400 <c>invalidPath</c>, not
/// silently ignored). Attributes of RFC 7643 that this server does not store (phone numbers, addresses, ...) are
/// KNOWN and accepted-and-ignored, because Entra ID and Okta send them whenever an admin maps them.
/// </summary>
public static class ScimUserPaths
{
    private static readonly HashSet<string> CoreAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "schemas", "id", "externalId", "meta", "userName", "name", "displayName", "nickName", "profileUrl", "title",
        "userType", "preferredLanguage", "locale", "timezone", "active", "password", "emails", "phoneNumbers", "ims",
        "photos", "addresses", "groups", "entitlements", "roles", "x509Certificates"
    };

    private static readonly HashSet<string> EnterpriseAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "employeeNumber", "costCenter", "organization", "division", "department", "manager"
    };

    /// <summary>Removes a leading core-schema URN (<c>urn:...:core:2.0:User:userName</c> is a valid path).</summary>
    public static string StripCoreSchema(string path) =>
        path.StartsWith(ScimSchemas.User + ":", StringComparison.OrdinalIgnoreCase) ? path[(ScimSchemas.User.Length + 1)..] : path;

    /// <summary>
    /// For a path inside the enterprise extension: "" = the extension itself (value is an object), otherwise the
    /// attribute path after the URN (<c>department</c>, <c>manager.value</c>). Null = not an enterprise path.
    /// </summary>
    public static string? EnterpriseRemainder(string path)
    {
        if (!path.StartsWith(ScimSchemas.EnterpriseUser, StringComparison.OrdinalIgnoreCase))
            return null;
        var rest = path[ScimSchemas.EnterpriseUser.Length..];
        return rest.Length == 0 ? string.Empty : rest[0] == ':' ? rest[1..] : null;
    }

    /// <summary>
    /// First segment of an attribute path: <c>emails[type eq "work"].value</c> → <c>emails</c>. A URN path is returned
    /// whole (its "2.0" is not a sub-attribute separator).
    /// </summary>
    public static string Head(string path)
    {
        if (path.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
            return path;

        var end = path.IndexOfAny(['.', '[']);
        return end < 0 ? path : path[..end];
    }

    /// <summary>
    /// True for an attribute of the User schema AND for a path inside any OTHER schema URN (a custom extension an IdP
    /// maps): those are accepted and ignored, because rejecting them would break the whole sync for the directory.
    /// Only a bare name that is not an attribute of the User schema is unknown.
    /// </summary>
    public static bool IsKnownCore(string path)
    {
        var stripped = StripCoreSchema(path);
        return stripped.StartsWith("urn:", StringComparison.OrdinalIgnoreCase) || CoreAttributes.Contains(Head(stripped));
    }

    public static bool IsKnownEnterprise(string attributePath) => EnterpriseAttributes.Contains(Head(attributePath));
}

/// <summary>Reads and patches the enterprise user extension (RFC 7643 §4.3).</summary>
public static class ScimEnterpriseJson
{
    /// <summary>The extension object of a resource (POST/PUT), or null when the resource does not carry it.</summary>
    public static ScimEnterprise? FromResource(JsonObject resource)
    {
        if (ScimJson.Get(resource, ScimSchemas.EnterpriseUser) is not JsonObject extension)
            return null;

        var result = ScimEnterprise.Empty;
        foreach (var (name, value) in extension)
        {
            // A body is not a path: attributes this server does not know are ignored, as in the core schema.
            if (ScimUserPaths.IsKnownEnterprise(name))
                result = Apply(result, name, value);
        }

        return result;
    }

    /// <summary>
    /// Sets (or, with a null/empty value, clears) one extension attribute. <paramref name="attribute"/> is relative to
    /// the extension: <c>department</c>, <c>manager</c>, <c>manager.value</c>. Unknown names are an <c>invalidPath</c>.
    /// </summary>
    public static ScimEnterprise Apply(ScimEnterprise current, string attribute, JsonNode? value)
    {
        if (!ScimUserPaths.IsKnownEnterprise(attribute))
            throw ScimException.InvalidPath($"'{attribute}' is not an attribute of the enterprise user extension.");

        var name = attribute.ToLowerInvariant();
        switch (ScimUserPaths.Head(name))
        {
            case "employeenumber":
                return current with { EmployeeNumber = ScimJson.Text(value) };
            case "costcenter":
                return current with { CostCenter = ScimJson.Text(value) };
            case "organization":
                return current with { Organization = ScimJson.Text(value) };
            case "division":
                return current with { Division = ScimJson.Text(value) };
            case "department":
                return current with { Department = ScimJson.Text(value) };
        }

        // manager: {"value": "<id>"} | manager.value = "<id>" | manager.displayName / $ref (read-only, ignored)
        if (name is "manager.displayname" or "manager.$ref")
            return current;

        var text = value is JsonObject obj ? ScimJson.Text(ScimJson.Get(obj, "value")) : ScimJson.Text(value);
        return current with { ManagerId = Guid.TryParse(text, out var id) ? id : null };
    }
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
