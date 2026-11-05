using System.Text.Json.Nodes;

namespace Identity.Contracts.Scim;

// SCIM 2.0 (RFC 7643 / 7644) provisioning from the customer's directory (Entra ID, Okta, Google Workspace,
// JumpCloud, ...). A tenant admin creates a "directory" and pastes its base URL + bearer token into the IdP.

// ----- tenant admin -----

public sealed record ScimDirectoryDto(
    Guid Id,
    string Name,
    bool Enabled,
    string TokenPrefix,
    string BaseUrl,
    DateTime? CreatedAt,
    DateTime? LastRequestAt,
    int Users,
    int Groups,
    long Version);

/// <param name="Token">The bearer token - returned ONCE (only its hash is stored).</param>
public sealed record ScimDirectoryCreatedDto(ScimDirectoryDto Directory, string Token);

public sealed record GetScimDirectoriesQuery : IQuery<List<ScimDirectoryDto>>;

public sealed record CreateScimDirectoryCommand(string Name) : ICommand<ScimDirectoryCreatedDto>;

/// <summary>Issues a new token; the old one stops working immediately.</summary>
public sealed record RotateScimDirectoryTokenCommand(Guid Id) : ICommand<ScimDirectoryCreatedDto>;

public sealed record SetScimDirectoryEnabledCommand(Guid Id, bool Enabled, long? Version = null) : ICommand<ScimDirectoryDto>;

/// <summary>Deletes the directory and its user/group mirror. Accounts and memberships stay as they are.</summary>
public sealed record DeleteScimDirectoryCommand(Guid Id) : ICommand<DeleteScimDirectoryResult>;

public sealed record DeleteScimDirectoryResult(Guid Id);

public sealed record ScimGroupDto(Guid Id, string DisplayName, string? ExternalId, Guid? TenantRoleId, int Members);

public sealed record GetScimGroupsQuery(Guid DirectoryId) : IQuery<List<ScimGroupDto>>;

/// <summary>
/// Maps a directory group to a tenant role (null = unmapped). Once any group of a directory is mapped, the directory
/// OWNS the roles of its users: roles = the mapped roles of the groups they belong to.
/// </summary>
public sealed record MapScimGroupRoleCommand(Guid DirectoryId, Guid GroupId, Guid? TenantRoleId) : ICommand<ScimGroupDto>;

// ----- SCIM protocol (called by the IdP; the directory comes from the bearer token) -----

/// <summary>A SCIM response: status, body (application/scim+json) and optional Location / ETag.</summary>
public sealed record ScimResult(int Status, JsonNode? Body, string? Location = null, string? ETag = null);

public sealed record GetScimUsersQuery(Guid DirectoryId, string? Filter, int? StartIndex, int? Count) : IQuery<ScimResult>;

public sealed record GetScimUserQuery(Guid DirectoryId, Guid Id) : IQuery<ScimResult>;

public sealed record CreateScimUserCommand(Guid DirectoryId, JsonObject Resource) : ICommand<ScimResult>;

public sealed record ReplaceScimUserCommand(Guid DirectoryId, Guid Id, JsonObject Resource) : ICommand<ScimResult>;

public sealed record PatchScimUserCommand(Guid DirectoryId, Guid Id, JsonObject Patch) : ICommand<ScimResult>;

public sealed record DeleteScimUserCommand(Guid DirectoryId, Guid Id) : ICommand<ScimResult>;

public sealed record GetScimGroupsResourceQuery(Guid DirectoryId, string? Filter, int? StartIndex, int? Count, bool ExcludeMembers)
    : IQuery<ScimResult>;

public sealed record GetScimGroupQuery(Guid DirectoryId, Guid Id, bool ExcludeMembers) : IQuery<ScimResult>;

public sealed record CreateScimGroupCommand(Guid DirectoryId, JsonObject Resource) : ICommand<ScimResult>;

public sealed record ReplaceScimGroupCommand(Guid DirectoryId, Guid Id, JsonObject Resource) : ICommand<ScimResult>;

public sealed record PatchScimGroupCommand(Guid DirectoryId, Guid Id, JsonObject Patch) : ICommand<ScimResult>;

public sealed record DeleteScimGroupCommand(Guid DirectoryId, Guid Id) : ICommand<ScimResult>;
