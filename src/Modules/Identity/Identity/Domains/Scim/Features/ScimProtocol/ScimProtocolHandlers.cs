using Identity.Contracts.Scim;
using Identity.Domains.Scim.Models;
using Identity.Services.Scim;

namespace Identity.Domains.Scim.Features.ScimProtocol;

// SCIM protocol operations. The directory id comes from the authenticated SCIM token (never from the URL), and the
// endpoint has already entered the directory's tenant scope. All semantics live in ScimProvisioningService.

public sealed class GetScimUsersHandler(ScimProvisioningService scim) : IQueryHandler<GetScimUsersQuery, ScimResult>
{
    public Task<ScimResult> Handle(GetScimUsersQuery query, CancellationToken cancellationToken) =>
        scim.ListUsersAsync(query.DirectoryId, query.Filter, query.StartIndex, query.Count, cancellationToken);
}

public sealed class GetScimUserHandler(ScimProvisioningService scim) : IQueryHandler<GetScimUserQuery, ScimResult>
{
    public Task<ScimResult> Handle(GetScimUserQuery query, CancellationToken cancellationToken) =>
        scim.GetUserAsync(query.DirectoryId, query.Id, cancellationToken);
}

public sealed class CreateScimUserHandler(ScimProvisioningService scim, ScimDirectoryLoader directories)
    : ICommandHandler<CreateScimUserCommand, ScimResult>
{
    public async Task<ScimResult> Handle(CreateScimUserCommand command, CancellationToken cancellationToken) =>
        await scim.CreateUserAsync(await directories.LoadAsync(command.DirectoryId, cancellationToken), command.Resource,
            cancellationToken);
}

public sealed class ReplaceScimUserHandler(ScimProvisioningService scim, ScimDirectoryLoader directories, ScimConflictRetry retry)
    : ICommandHandler<ReplaceScimUserCommand, ScimResult>
{
    public async Task<ScimResult> Handle(ReplaceScimUserCommand command, CancellationToken cancellationToken) =>
        await retry.RunAsync(async () => await scim.ReplaceUserAsync(await directories.LoadAsync(command.DirectoryId, cancellationToken), command.Id,
            command.Resource, cancellationToken), cancellationToken);
}

public sealed class PatchScimUserHandler(ScimProvisioningService scim, ScimDirectoryLoader directories, ScimConflictRetry retry)
    : ICommandHandler<PatchScimUserCommand, ScimResult>
{
    public async Task<ScimResult> Handle(PatchScimUserCommand command, CancellationToken cancellationToken) =>
        await retry.RunAsync(async () => await scim.PatchUserAsync(await directories.LoadAsync(command.DirectoryId, cancellationToken), command.Id,
            command.Patch, cancellationToken), cancellationToken);
}

public sealed class DeleteScimUserHandler(ScimProvisioningService scim, ScimDirectoryLoader directories)
    : ICommandHandler<DeleteScimUserCommand, ScimResult>
{
    public async Task<ScimResult> Handle(DeleteScimUserCommand command, CancellationToken cancellationToken) =>
        await scim.DeleteUserAsync(await directories.LoadAsync(command.DirectoryId, cancellationToken), command.Id,
            cancellationToken);
}

public sealed class GetScimGroupsResourceHandler(ScimProvisioningService scim)
    : IQueryHandler<GetScimGroupsResourceQuery, ScimResult>
{
    public Task<ScimResult> Handle(GetScimGroupsResourceQuery query, CancellationToken cancellationToken) =>
        scim.ListGroupsAsync(query.DirectoryId, query.Filter, query.StartIndex, query.Count, query.ExcludeMembers,
            cancellationToken);
}

public sealed class GetScimGroupHandler(ScimProvisioningService scim) : IQueryHandler<GetScimGroupQuery, ScimResult>
{
    public Task<ScimResult> Handle(GetScimGroupQuery query, CancellationToken cancellationToken) =>
        scim.GetGroupAsync(query.DirectoryId, query.Id, query.ExcludeMembers, cancellationToken);
}

public sealed class CreateScimGroupHandler(ScimProvisioningService scim, ScimDirectoryLoader directories)
    : ICommandHandler<CreateScimGroupCommand, ScimResult>
{
    public async Task<ScimResult> Handle(CreateScimGroupCommand command, CancellationToken cancellationToken) =>
        await scim.CreateGroupAsync(await directories.LoadAsync(command.DirectoryId, cancellationToken), command.Resource,
            cancellationToken);
}

public sealed class ReplaceScimGroupHandler(ScimProvisioningService scim, ScimDirectoryLoader directories, ScimConflictRetry retry)
    : ICommandHandler<ReplaceScimGroupCommand, ScimResult>
{
    public async Task<ScimResult> Handle(ReplaceScimGroupCommand command, CancellationToken cancellationToken) =>
        await retry.RunAsync(async () => await scim.ReplaceGroupAsync(await directories.LoadAsync(command.DirectoryId, cancellationToken), command.Id,
            command.Resource, cancellationToken), cancellationToken);
}

public sealed class PatchScimGroupHandler(ScimProvisioningService scim, ScimDirectoryLoader directories, ScimConflictRetry retry)
    : ICommandHandler<PatchScimGroupCommand, ScimResult>
{
    public async Task<ScimResult> Handle(PatchScimGroupCommand command, CancellationToken cancellationToken) =>
        await retry.RunAsync(async () => await scim.PatchGroupAsync(await directories.LoadAsync(command.DirectoryId, cancellationToken), command.Id,
            command.Patch, cancellationToken), cancellationToken);
}

public sealed class DeleteScimGroupHandler(ScimProvisioningService scim, ScimDirectoryLoader directories)
    : ICommandHandler<DeleteScimGroupCommand, ScimResult>
{
    public async Task<ScimResult> Handle(DeleteScimGroupCommand command, CancellationToken cancellationToken) =>
        await scim.DeleteGroupAsync(await directories.LoadAsync(command.DirectoryId, cancellationToken), command.Id,
            cancellationToken);
}

public sealed class ScimDirectoryLoader(ApplicationIdentityDbContext dbContext)
{
    public async Task<ScimDirectory> LoadAsync(Guid id, CancellationToken ct) =>
        await dbContext.ScimDirectories.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.Enabled, ct)
        ?? throw new ScimException(401, "The directory is disabled or no longer exists.");
}

/// <summary>
/// IdPs send many small parallel PATCHes for one group or user. Each write is guarded by the SCIM revision
/// (concurrency token); on a conflict the operation is simply re-run on fresh data (up to 3 times), so every
/// add/remove lands instead of the last writer silently winning.
/// </summary>
public sealed class ScimConflictRetry(ApplicationIdentityDbContext dbContext)
{
    private const int MaxAttempts = 3;

    public async Task<ScimResult> RunAsync(Func<Task<ScimResult>> operation, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await operation();
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), ct);
            }
        }
    }
}
