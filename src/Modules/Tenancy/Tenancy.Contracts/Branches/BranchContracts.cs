namespace Tenancy.Contracts.Branches;

public sealed record BranchDto(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    int Depth,
    bool IsHeadOffice,
    bool IsActive,
    string? Address,
    string? Phone,
    string? TimeZone,
    long Version);

public sealed record BranchTreeNodeDto(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    bool IsHeadOffice,
    bool IsActive,
    List<BranchTreeNodeDto> Children);

/// <summary>
/// The branch tree visible to the caller: everything for unrestricted members; otherwise the caller's branches
/// (each shown as a root of its own subtree).
/// </summary>
public sealed record GetBranchesQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<BranchTreeNodeDto>>;

public sealed record GetBranchByIdQuery(Guid Id) : IQuery<BranchDto>;

public sealed record CreateBranchCommand(
    Guid? ParentId,
    string Code,
    string Name,
    string? Address = null,
    string? Phone = null,
    string? TimeZone = null) : ICommand<CreateBranchResult>;

public sealed record CreateBranchResult(Guid Id);

public sealed record UpdateBranchCommand(
    Guid Id,
    string Name,
    string? Address,
    string? Phone,
    string? TimeZone,
    long? Version = null) : ICommand<UpdateBranchResult>;

public sealed record UpdateBranchResult(Guid Id, long Version);

/// <summary>Re-parents a branch (null = make it a root); its whole subtree moves with it.</summary>
public sealed record MoveBranchCommand(Guid Id, Guid? NewParentId, long? Version = null) : ICommand<MoveBranchResult>;

public sealed record MoveBranchResult(Guid Id, int MovedDescendants);

public sealed record SetBranchActiveCommand(Guid Id, bool IsActive) : ICommand<SetBranchActiveResult>;

public sealed record SetBranchActiveResult(Guid Id, bool IsActive);
