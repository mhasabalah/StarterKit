using System.Text.Json.Nodes;
using Identity.Contracts.Scim;
using Identity.Domains.Scim.Models;
using Identity.Domains.Sso.Models;
using Identity.Services.Sessions;
using Identity.Services.Sso;
using Npgsql;
using Tenancy.Contracts.Members;

namespace Identity.Services.Scim;

/// <summary>
/// SCIM semantics on top of Identity accounts and Tenancy memberships. Rules that matter for security and for
/// how real IdPs behave:
/// <list type="bullet">
/// <item><b>Domain proof.</b> A directory only provisions emails on domains its tenant VERIFIED (DNS) on an SSO
/// connection. Otherwise any tenant could link (and then sign in as) someone else's account.</item>
/// <item><b>Idempotent.</b> userName/externalId are unique per directory (409 <c>uniqueness</c>, also enforced by
/// unique indexes for concurrent retries). An existing account with the email is linked, never duplicated.</item>
/// <item><b>Deprovisioning never deletes.</b> <c>active=false</c> and DELETE suspend the membership in this tenant
/// and revoke every session of the account; data stays (GDPR erasure is a separate, explicit flow).</item>
/// <item><b>Roles from groups.</b> When any group of the directory is mapped to a tenant role, the directory owns
/// its users' roles: roles = mapped roles of their groups. Unmapped directories never touch roles.</item>
/// </list>
/// </summary>
public sealed class ScimProvisioningService(
    ApplicationIdentityDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ISender sender,
    ISessionRevoker sessionRevoker,
    SsoUrls urls,
    TimeProvider timeProvider,
    ILogger<ScimProvisioningService> logger)
{
    public const int MaxPageSize = 200;

    public string BaseUrl => urls.BaseUrl + "/scim/v2";

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ================================================================ users

    public async Task<ScimResult> ListUsersAsync(Guid directoryId, string? filter, int? startIndex, int? count, CancellationToken ct)
    {
        var query = dbContext.ScimUsers.AsNoTracking().Where(u => u.DirectoryId == directoryId);
        foreach (var clause in ScimFilter.Parse(filter))
        {
            var value = clause.Value;
            var lowered = value.ToLowerInvariant();   // userName is stored lower-case
            var normalized = value.ToUpperInvariant(); // ASP.NET Identity NormalizedEmail
            query = clause.Attribute.ToLowerInvariant() switch
            {
                "username" => query.Where(u => u.UserName == lowered),
                "externalid" => query.Where(u => u.ExternalId == value),
                "id" => Guid.TryParse(value, out var id) ? query.Where(u => u.UserId == id) : query.Where(_ => false),
                "emails.value" => query.Where(u => dbContext.Users.Any(a => a.Id == u.UserId && a.NormalizedEmail == normalized)),
                _ => throw ScimException.InvalidFilter($"Filtering users on '{clause.Attribute}' is not supported.")
            };
        }

        var (start, size) = Page(startIndex, count);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(u => u.Created).ThenBy(u => u.UserId).Skip(start - 1).Take(size)
            .Join(dbContext.Users, s => s.UserId, a => a.Id, (s, a) => new { Scim = s, User = a })
            .ToListAsync(ct);

        return new ScimResult(200, ScimJson.List(rows.Select(r => ScimJson.User(r.Scim, r.User, BaseUrl)), total, start));
    }

    public async Task<ScimResult> GetUserAsync(Guid directoryId, Guid id, CancellationToken ct)
    {
        var (scim, user) = await LoadUserAsync(directoryId, id, tracking: false, ct);
        return UserResult(200, scim, user);
    }

    public async Task<ScimResult> CreateUserAsync(ScimDirectory directory, JsonObject resource, CancellationToken ct)
    {
        var userName = Required(ScimJson.Text(ScimJson.Get(resource, "userName")), "userName").ToLowerInvariant();
        var externalId = ScimJson.Text(ScimJson.Get(resource, "externalId"));
        var active = ScimJson.Bool(ScimJson.Get(resource, "active")) ?? true;
        var email = ScimJson.PrimaryEmail(resource) ?? (userName.Contains('@') ? userName : null)
                    ?? throw ScimException.InvalidValue("An email (emails[].value or an email userName) is required.");

        if (await dbContext.ScimUsers.AnyAsync(u => u.DirectoryId == directory.Id && u.UserName == userName, ct))
            throw ScimException.Uniqueness($"userName '{userName}' already exists.");
        if (externalId is not null &&
            await dbContext.ScimUsers.AnyAsync(u => u.DirectoryId == directory.Id && u.ExternalId == externalId, ct))
            throw ScimException.Uniqueness($"externalId '{externalId}' already exists.");

        await EnsureVerifiedDomainAsync(directory.TenantId, email, ct);

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                EmailConfirmed = true, // the tenant proved it owns the domain
                IsActive = true,
                IsProfileComplete = true
            };
            ApplyName(user, resource);
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
                throw ScimException.InvalidValue(string.Join(" ", created.Errors.Select(e => e.Description)));
            await userManager.AddToRoleAsync(user, RoleConstants.User);
        }
        else if (await dbContext.ScimUsers.AnyAsync(u => u.DirectoryId == directory.Id && u.UserId == user.Id, ct))
        {
            throw ScimException.Uniqueness($"The account '{email}' is already provisioned by this directory.");
        }

        var enterprise = await KnownManagerAsync(directory.Id, ScimEnterpriseJson.FromResource(resource), ct);
        var scim = ScimUser.Create(directory.Id, user.Id, userName, externalId, active, enterprise, Now);
        dbContext.ScimUsers.Add(scim);
        await SaveUniqueAsync(ct);

        await ApplyMembershipAsync(directory.Id, user.Id, active, ct);
        logger.LogInformation("SCIM directory {DirectoryId} provisioned user {UserId}", directory.Id, user.Id);
        return UserResult(201, scim, user);
    }

    public async Task<ScimResult> ReplaceUserAsync(ScimDirectory directory, Guid id, JsonObject resource, CancellationToken ct)
    {
        var (scim, user) = await LoadUserAsync(directory.Id, id, tracking: true, ct);
        var userName = Required(ScimJson.Text(ScimJson.Get(resource, "userName")), "userName").ToLowerInvariant();
        var active = ScimJson.Bool(ScimJson.Get(resource, "active")) ?? true;

        ApplyName(user, resource);
        await userManager.UpdateAsync(user);
        var wasActive = scim.Active;
        // PUT replaces the extension when the resource carries it; a resource without it leaves it untouched.
        var enterprise = await KnownManagerAsync(directory.Id, ScimEnterpriseJson.FromResource(resource), ct) ?? scim.Enterprise;
        scim.Update(userName, ScimJson.Text(ScimJson.Get(resource, "externalId")), active, enterprise, Now);
        await SaveUniqueAsync(ct);

        if (wasActive != active)
            await ApplyMembershipAsync(directory.Id, user.Id, active, ct);
        return UserResult(200, scim, user);
    }

    /// <summary>
    /// RFC 7644 §3.5.2. Accepts what Entra ID and Okta send: op names in any case, path-less "replace" with an
    /// object, <c>active</c> as a string, <c>name.givenName</c>-style paths, enterprise extension paths
    /// (<c>urn:...:enterprise:2.0:User:department</c>, <c>manager.value</c>). A path that is not an attribute of
    /// the User (or enterprise) schema is a 400 <c>invalidPath</c>; known attributes this server does not store
    /// (phone numbers, addresses, ...) are accepted and ignored.
    /// </summary>
    public async Task<ScimResult> PatchUserAsync(ScimDirectory directory, Guid id, JsonObject patch, CancellationToken ct)
    {
        var (scim, user) = await LoadUserAsync(directory.Id, id, tracking: true, ct);
        var userName = scim.UserName;
        var externalId = scim.ExternalId;
        var active = scim.Active;
        var enterprise = scim.Enterprise;
        var profileChanged = false;

        foreach (var (op, rawPath, value) in Operations(patch))
        {
            var path = rawPath is null ? null : ScimUserPaths.StripCoreSchema(rawPath);
            if (op == "remove")
            {
                if (path is null)
                    throw ScimException.InvalidPath("A remove operation needs a path.");

                if (ScimUserPaths.EnterpriseRemainder(path) is { } removedExtension)
                {
                    enterprise = removedExtension.Length == 0
                        ? ScimEnterprise.Empty
                        : ScimEnterpriseJson.Apply(enterprise, removedExtension, null);
                }
                else if (string.Equals(path, "externalId", StringComparison.OrdinalIgnoreCase))
                {
                    externalId = null;
                }
                else if (!ScimUserPaths.IsKnownCore(path))
                {
                    throw ScimException.InvalidPath($"'{path}' is not an attribute of the User schema.");
                }

                continue;
            }

            // A path-less replace/add carries an object of attributes.
            var assignments = path is null && value is JsonObject obj
                ? obj.Select(p => (Path: p.Key, Value: p.Value))
                : [(Path: path ?? string.Empty, Value: value)];

            foreach (var (rawAttribute, attributeValue) in assignments)
            {
                var attribute = ScimUserPaths.StripCoreSchema(rawAttribute);
                if (ScimUserPaths.EnterpriseRemainder(attribute) is { } extension)
                {
                    if (extension.Length == 0)
                    {
                        if (attributeValue is not JsonObject extensionValues)
                            throw ScimException.InvalidValue("The enterprise extension needs an object value.");
                        foreach (var (name, extensionValue) in extensionValues)
                            enterprise = ScimEnterpriseJson.Apply(enterprise, name, extensionValue);
                    }
                    else
                    {
                        enterprise = ScimEnterpriseJson.Apply(enterprise, extension, attributeValue);
                    }

                    continue;
                }

                switch (attribute.ToLowerInvariant())
                {
                    case "active":
                        active = ScimJson.Bool(attributeValue) ?? active;
                        break;
                    case "username":
                        userName = ScimJson.Text(attributeValue)?.ToLowerInvariant() ?? userName;
                        break;
                    case "externalid":
                        externalId = ScimJson.Text(attributeValue);
                        break;
                    case "name.givenname":
                        user.FirstName = Truncate(ScimJson.Text(attributeValue)) ?? user.FirstName;
                        profileChanged = true;
                        break;
                    case "name.familyname":
                        user.LastName = Truncate(ScimJson.Text(attributeValue)) ?? user.LastName;
                        profileChanged = true;
                        break;
                    case "name" when attributeValue is JsonObject name:
                        ApplyName(user, new JsonObject { ["name"] = name.DeepClone() });
                        profileChanged = true;
                        break;
                    default:
                        if (string.IsNullOrEmpty(attribute))
                            throw ScimException.InvalidPath("An add or replace operation without a path needs an object value.");
                        if (!ScimUserPaths.IsKnownCore(attribute))
                            throw ScimException.InvalidPath($"'{attribute}' is not an attribute of the User schema.");
                        break; // a known attribute this server does not store
                }
            }
        }

        enterprise = (await KnownManagerAsync(directory.Id, enterprise, ct))!;

        if (profileChanged)
            await userManager.UpdateAsync(user);

        var wasActive = scim.Active;
        scim.Update(userName, externalId, active, enterprise, Now);
        await SaveUniqueAsync(ct);

        if (wasActive != active)
            await ApplyMembershipAsync(directory.Id, user.Id, active, ct);
        return UserResult(200, scim, user);
    }

    public async Task<ScimResult> DeleteUserAsync(ScimDirectory directory, Guid id, CancellationToken ct)
    {
        var (scim, _) = await LoadUserAsync(directory.Id, id, tracking: true, ct);
        dbContext.ScimUsers.Remove(scim);

        foreach (var group in await dbContext.ScimGroups.Where(g => g.DirectoryId == directory.Id && g.Members.Contains(id)).ToListAsync(ct))
            group.RemoveMembers([id], Now);

        await dbContext.SaveChangesAsync(ct);
        await ApplyMembershipAsync(directory.Id, id, active: false, ct);
        logger.LogInformation("SCIM directory {DirectoryId} deprovisioned user {UserId}", directory.Id, id);
        return new ScimResult(204, null);
    }

    // ================================================================ groups

    public async Task<ScimResult> ListGroupsAsync(Guid directoryId, string? filter, int? startIndex, int? count,
        bool excludeMembers, CancellationToken ct)
    {
        var query = dbContext.ScimGroups.AsNoTracking().Where(g => g.DirectoryId == directoryId);
        foreach (var clause in ScimFilter.Parse(filter))
        {
            var value = clause.Value;
            query = clause.Attribute.ToLowerInvariant() switch
            {
                "displayname" => query.Where(g => g.DisplayName == value),
                "externalid" => query.Where(g => g.ExternalId == value),
                "id" => Guid.TryParse(value, out var id) ? query.Where(g => g.Id == id) : query.Where(_ => false),
                _ => throw ScimException.InvalidFilter($"Filtering groups on '{clause.Attribute}' is not supported.")
            };
        }

        var (start, size) = Page(startIndex, count);
        var total = await query.CountAsync(ct);
        var groups = await query.OrderBy(g => g.Created).ThenBy(g => g.Id).Skip(start - 1).Take(size).ToListAsync(ct);
        return new ScimResult(200, ScimJson.List(groups.Select(g => ScimJson.Group(g, BaseUrl, excludeMembers)), total, start));
    }

    public async Task<ScimResult> GetGroupAsync(Guid directoryId, Guid id, bool excludeMembers, CancellationToken ct)
    {
        var group = await dbContext.ScimGroups.AsNoTracking().FirstOrDefaultAsync(g => g.DirectoryId == directoryId && g.Id == id, ct)
                    ?? throw ScimException.NotFound("Group");
        return GroupResult(200, group, excludeMembers);
    }

    public async Task<ScimResult> CreateGroupAsync(ScimDirectory directory, JsonObject resource, CancellationToken ct)
    {
        var displayName = Required(ScimJson.Text(ScimJson.Get(resource, "displayName")), "displayName");
        if (await dbContext.ScimGroups.AnyAsync(g => g.DirectoryId == directory.Id && g.DisplayName == displayName, ct))
            throw ScimException.Uniqueness($"Group '{displayName}' already exists.");

        var group = ScimGroup.Create(directory.Id, displayName, ScimJson.Text(ScimJson.Get(resource, "externalId")), Now);
        group.SetMembers(await KnownMembersAsync(directory.Id, MemberIds(ScimJson.Get(resource, "members")), ct), Now);
        dbContext.ScimGroups.Add(group);
        await SaveUniqueAsync(ct);

        await SyncRolesAsync(directory.Id, group.Members, ct);
        return GroupResult(201, group, excludeMembers: false);
    }

    public async Task<ScimResult> ReplaceGroupAsync(ScimDirectory directory, Guid id, JsonObject resource, CancellationToken ct)
    {
        var group = await LoadGroupAsync(directory.Id, id, ct);
        var before = group.Members.ToList();

        group.Rename(Required(ScimJson.Text(ScimJson.Get(resource, "displayName")), "displayName"),
            ScimJson.Text(ScimJson.Get(resource, "externalId")), Now);
        group.SetMembers(await KnownMembersAsync(directory.Id, MemberIds(ScimJson.Get(resource, "members")), ct), Now);
        await SaveUniqueAsync(ct);

        await SyncRolesAsync(directory.Id, before.Union(group.Members), ct);
        return GroupResult(200, group, excludeMembers: false);
    }

    /// <summary>Member add/remove are incremental (large groups arrive as many small patches).</summary>
    public async Task<ScimResult> PatchGroupAsync(ScimDirectory directory, Guid id, JsonObject patch, CancellationToken ct)
    {
        var group = await LoadGroupAsync(directory.Id, id, ct);
        var before = group.Members.ToList();

        foreach (var (op, path, value) in Operations(patch))
        {
            var target = path?.Trim() ?? string.Empty;
            if (target.StartsWith("members", StringComparison.OrdinalIgnoreCase))
            {
                // Okta / Entra remove form: members[value eq "id"]
                var filtered = MemberFilter(target);
                var ids = filtered is not null ? [filtered.Value] : MemberIds(value);
                switch (op)
                {
                    case "add":
                        group.AddMembers(await KnownMembersAsync(directory.Id, ids, ct), Now);
                        break;
                    case "remove" when filtered is null && value is null:
                        group.SetMembers([], Now);
                        break;
                    case "remove":
                        group.RemoveMembers(ids, Now);
                        break;
                    case "replace":
                        group.SetMembers(await KnownMembersAsync(directory.Id, ids, ct), Now);
                        break;
                }
            }
            else if (op is "replace" or "add")
            {
                var displayName = target.Equals("displayName", StringComparison.OrdinalIgnoreCase)
                    ? ScimJson.Text(value)
                    : value is JsonObject obj ? ScimJson.Text(ScimJson.Get(obj, "displayName")) : null;
                if (displayName is not null)
                    group.Rename(displayName, group.ExternalId, Now);
            }
        }

        await SaveUniqueAsync(ct);
        await SyncRolesAsync(directory.Id, before.Union(group.Members), ct);
        return GroupResult(200, group, excludeMembers: false);
    }

    public async Task<ScimResult> DeleteGroupAsync(ScimDirectory directory, Guid id, CancellationToken ct)
    {
        var group = await LoadGroupAsync(directory.Id, id, ct);
        var members = group.Members.ToList();
        dbContext.ScimGroups.Remove(group);
        await dbContext.SaveChangesAsync(ct);
        await SyncRolesAsync(directory.Id, members, ct);
        return new ScimResult(204, null);
    }

    /// <summary>Re-applies group-derived roles to the given users (after a mapping or membership change).</summary>
    public async Task SyncRolesAsync(Guid directoryId, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var groups = await dbContext.ScimGroups.AsNoTracking().Where(g => g.DirectoryId == directoryId).ToListAsync(ct);
        if (!groups.Any(g => g.TenantRoleId is not null))
            return; // unmapped directory: roles are managed in the app, never overwritten

        var active = await dbContext.ScimUsers.AsNoTracking()
            .Where(u => u.DirectoryId == directoryId && u.Active)
            .Select(u => u.UserId).ToListAsync(ct);
        var activeSet = active.ToHashSet();

        foreach (var userId in userIds.Distinct().Where(activeSet.Contains))
        {
            var roles = groups.Where(g => g.TenantRoleId is not null && g.Members.Contains(userId))
                .Select(g => g.TenantRoleId!.Value).Distinct().ToList();
            await sender.Send(new ProvisionMemberCommand(userId, MemberProvisioningSource.Scim, roles), ct);
        }
    }

    // ================================================================ helpers

    private async Task ApplyMembershipAsync(Guid directoryId, Guid userId, bool active, CancellationToken ct)
    {
        if (active)
        {
            var groups = await dbContext.ScimGroups.AsNoTracking().Where(g => g.DirectoryId == directoryId).ToListAsync(ct);
            IReadOnlyList<Guid>? roles = groups.Any(g => g.TenantRoleId is not null)
                ? groups.Where(g => g.TenantRoleId is not null && g.Members.Contains(userId))
                    .Select(g => g.TenantRoleId!.Value).Distinct().ToList()
                : null;
            await sender.Send(new ProvisionMemberCommand(userId, MemberProvisioningSource.Scim, roles, Active: true), ct);
            return;
        }

        await sender.Send(new ProvisionMemberCommand(userId, MemberProvisioningSource.Scim, Active: false), ct);

        // Offboarding must be immediate: every session of the account ends now (tokens are blacklisted).
        await sessionRevoker.RevokeAllAsync(userId, "Deprovisioned by SCIM directory", ct);
    }

    private async Task EnsureVerifiedDomainAsync(Guid tenantId, string email, CancellationToken ct)
    {
        var domain = SsoDomain.FromEmail(email) ?? throw ScimException.InvalidValue($"'{email}' is not an email address.");
        var verified = await dbContext.SsoDomains.AnyAsync(d => d.Domain == domain && d.VerifiedAt != null &&
            dbContext.SsoConnections.Any(c => c.Id == d.ConnectionId && c.TenantId == tenantId), ct);
        if (!verified)
            throw ScimException.InvalidValue(
                $"The domain '{domain}' is not verified for this organization. Verify it under SSO settings first.");
    }

    private async Task<(ScimUser Scim, ApplicationUser User)> LoadUserAsync(Guid directoryId, Guid id, bool tracking, CancellationToken ct)
    {
        var users = tracking ? dbContext.ScimUsers : dbContext.ScimUsers.AsNoTracking();
        var scim = await users.FirstOrDefaultAsync(u => u.DirectoryId == directoryId && u.UserId == id, ct)
                   ?? throw ScimException.NotFound("User");
        var user = await userManager.FindByIdAsync(id.ToString()) ?? throw ScimException.NotFound("User");
        return (scim, user);
    }

    private async Task<ScimGroup> LoadGroupAsync(Guid directoryId, Guid id, CancellationToken ct) =>
        await dbContext.ScimGroups.FirstOrDefaultAsync(g => g.DirectoryId == directoryId && g.Id == id, ct)
        ?? throw ScimException.NotFound("Group");

    /// <summary>
    /// A manager must be a user of the same directory (the SCIM id this server returned). Anything else is dropped
    /// rather than rejected: IdPs often send a manager before the manager has been provisioned.
    /// </summary>
    private async Task<ScimEnterprise?> KnownManagerAsync(Guid directoryId, ScimEnterprise? enterprise, CancellationToken ct)
    {
        if (enterprise?.ManagerId is not { } managerId)
            return enterprise;

        if (await dbContext.ScimUsers.AsNoTracking().AnyAsync(u => u.DirectoryId == directoryId && u.UserId == managerId, ct))
            return enterprise;

        logger.LogInformation("SCIM directory {DirectoryId}: manager {ManagerId} is not provisioned yet; ignored", directoryId, managerId);
        return enterprise with { ManagerId = null };
    }

    /// <summary>Only users this directory provisioned can be group members (unknown ids are dropped).</summary>
    private async Task<List<Guid>> KnownMembersAsync(Guid directoryId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];
        return await dbContext.ScimUsers.AsNoTracking()
            .Where(u => u.DirectoryId == directoryId && ids.Contains(u.UserId))
            .Select(u => u.UserId).ToListAsync(ct);
    }

    private async Task SaveUniqueAsync(CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two concurrent creates (IdP retry): the loser gets the SCIM-standard conflict.
            throw ScimException.Uniqueness("The resource already exists.");
        }
    }

    private static IEnumerable<(string Op, string? Path, JsonNode? Value)> Operations(JsonObject patch)
    {
        if (ScimJson.Get(patch, "Operations") is not JsonArray operations)
            throw ScimException.InvalidSyntax("A PatchOp needs an Operations array.");
        if (operations.Count > 1000)
            throw ScimException.InvalidValue("Too many operations in one request.");

        foreach (var node in operations.OfType<JsonObject>())
        {
            var op = ScimJson.Text(ScimJson.Get(node, "op"))?.ToLowerInvariant()
                     ?? throw ScimException.InvalidSyntax("Every operation needs an op.");
            if (op is not ("add" or "remove" or "replace"))
                throw ScimException.InvalidSyntax($"Unsupported op '{op}'.");
            yield return (op, ScimJson.Text(ScimJson.Get(node, "path")), ScimJson.Get(node, "value"));
        }
    }

    private static List<Guid> MemberIds(JsonNode? members) =>
        members switch
        {
            JsonArray array => array.OfType<JsonObject>()
                .Select(m => Guid.TryParse(ScimJson.Text(ScimJson.Get(m, "value")), out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty).Distinct().ToList(),
            JsonObject single => MemberIds(new JsonArray(single.DeepClone())),
            _ => []
        };

    private static Guid? MemberFilter(string path)
    {
        var start = path.IndexOf('"', StringComparison.Ordinal);
        var end = path.LastIndexOf('"');
        return start >= 0 && end > start && Guid.TryParse(path[(start + 1)..end], out var id) ? id : null;
    }

    private static void ApplyName(ApplicationUser user, JsonObject resource)
    {
        if (ScimJson.Get(resource, "name") is not JsonObject name)
            return;
        user.FirstName = Truncate(ScimJson.Text(ScimJson.Get(name, "givenName"))) ?? user.FirstName;
        user.LastName = Truncate(ScimJson.Text(ScimJson.Get(name, "familyName"))) ?? user.LastName;
    }

    private static string Required(string? value, string attribute) =>
        value ?? throw ScimException.InvalidValue($"'{attribute}' is required.");

    private static string? Truncate(string? value) => value is null ? null : value.Length > 100 ? value[..100] : value;

    private static (int Start, int Size) Page(int? startIndex, int? count) =>
        (Math.Max(1, startIndex ?? 1), Math.Clamp(count ?? 100, 0, MaxPageSize));

    private ScimResult UserResult(int status, ScimUser scim, ApplicationUser user) =>
        new(status, ScimJson.User(scim, user, BaseUrl), $"{BaseUrl}/Users/{scim.UserId}", ScimJson.ETag(scim.Revision));

    private ScimResult GroupResult(int status, ScimGroup group, bool excludeMembers) =>
        new(status, ScimJson.Group(group, BaseUrl, excludeMembers), $"{BaseUrl}/Groups/{group.Id}", ScimJson.ETag(group.Revision));
}
