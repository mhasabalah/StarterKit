using Agentic.Contracts.Memory;
using Agentic.Domains.AgentRuns.Services;
using Agentic.Domains.Memory.Models;
using Agentic.Runtime.Security;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.Authorization;

namespace Agentic.Domains.Memory.Features;

/// <summary>
/// A person writes a memory through the endpoint: a personal one belongs to the caller, a tenant one needs
/// <c>agentic.manage</c>. Internal to the module (not in Contracts): agents write memories only through the
/// approval-gated <c>memory.remember</c>.
/// </summary>
public sealed record CreateAgentMemoryCommand(string Content, AgentMemoryScope Scope = AgentMemoryScope.User)
    : ICommand<AgentMemoryDto>;

/// <summary>Deletes one of the caller's memories, or a tenant memory (<c>agentic.manage</c>). Internal, like create.</summary>
public sealed record DeleteAgentMemoryCommand(Guid Id) : ICommand<bool>;

/// <summary>What <c>memory.recall</c> returns: the newest personal AND the newest tenant memories, each capped separately.</summary>
public sealed record RecallAgentMemoriesQuery : IQuery<IReadOnlyList<AgentMemoryDto>>;

/// <summary>Also thrown for a memory the caller may not see or delete (no id probing).</summary>
public sealed class AgentMemoryNotFoundException(Guid id)
    : NotFoundException(AgenticText.Get("Agent.Memory.not_found", id));

/// <summary>The user or the tenant has as many memories as allowed (409 <c>memory_limit_reached</c>).</summary>
public sealed class AgentMemoryLimitException : ConflictException
{
    public AgentMemoryLimitException(int limit)
        : base(AgenticText.Get("Agent.Memory.limit_reached", limit)) => ErrorCode = "memory_limit_reached";
}

/// <summary>Limits and lifetime shared by the person and the agent paths.</summary>
public sealed class AgentMemoryWriter(IAgentMemoryRepository repository, TimeProvider timeProvider, IOptions<AgenticOptions> options)
{
    public async Task<AgentMemory> WriteAsync(AgentMemoryScope scope, Guid authorId, string content, Guid? sourceRunId,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        var settings = options.Value.Memory;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (limit, owner) = scope == AgentMemoryScope.User
            ? (settings.MaxPerUser, (Guid?)authorId)
            : (settings.MaxPerTenant, null);
        if (await repository.CountAsync(scope, owner, now, cancellationToken) >= limit)
            throw new AgentMemoryLimitException(limit);

        TimeSpan? ttl = settings.RetentionDays > 0 ? TimeSpan.FromDays(settings.RetentionDays) : null;
        var memory = AgentMemory.Write(scope, authorId, content, now, ttl, sourceRunId, idempotencyKey);
        repository.Add(memory);
        await repository.SaveChangesAsync(cancellationToken);
        return memory;
    }
}

public static class AgentMemoryProjections
{
    public static readonly System.Linq.Expressions.Expression<Func<AgentMemory, AgentMemoryDto>> ToDto = m => new AgentMemoryDto(
        m.Id, m.Scope, m.OwnerUserId, m.Content, m.Source, m.SourceRunId, m.AuthorId, m.CreatedAt, m.ExpiresAt);

    private static readonly Func<AgentMemory, AgentMemoryDto> Compiled = ToDto.Compile();

    public static AgentMemoryDto Map(AgentMemory memory) => Compiled(memory);
}

public class GetAgentMemoriesQueryValidator : AbstractValidator<GetAgentMemoriesQuery>
{
    public GetAgentMemoriesQueryValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.Scope).IsInEnum().WithMessage(_ => localizer["Validation.Memory.Scope.Invalid"]).When(x => x.Scope.HasValue);
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
    }
}

/// <summary>The caller's memories that have not expired (personal and tenant), newest first, paged: every one can be seen and deleted.</summary>
public class GetAgentMemoriesHandler(AgenticDbContext dbContext, IHttpContextService httpContext, TimeProvider timeProvider)
    : IQueryHandler<GetAgentMemoriesQuery, PaginatedResult<AgentMemoryDto>>
{
    public async Task<PaginatedResult<AgentMemoryDto>> Handle(GetAgentMemoriesQuery query, CancellationToken cancellationToken)
    {
        var memories = AgentMemoryVisibility.Live(dbContext, httpContext, timeProvider);
        if (query.Scope is { } scope)
            memories = memories.Where(m => m.Scope == scope);

        return await memories
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(AgentMemoryProjections.ToDto)
            .PagedResult(query.PageNumber ?? 1, query.PageSize ?? 20, cancellationToken);
    }
}

/// <summary>
/// For <c>memory.recall</c>: the newest personal memories of the user the run acts for and the newest tenant memories,
/// each up to <c>Agentic:Memory:MaxRecalled</c>, so many tenant memories never push the personal ones out.
/// </summary>
public class RecallAgentMemoriesHandler(
    AgenticDbContext dbContext,
    IHttpContextService httpContext,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options) : IQueryHandler<RecallAgentMemoriesQuery, IReadOnlyList<AgentMemoryDto>>
{
    public async Task<IReadOnlyList<AgentMemoryDto>> Handle(RecallAgentMemoriesQuery query, CancellationToken cancellationToken)
    {
        var limit = options.Value.Memory.MaxRecalled;
        var memories = AgentMemoryVisibility.Live(dbContext, httpContext, timeProvider);

        var personal = await Newest(memories.Where(m => m.Scope == AgentMemoryScope.User), limit, cancellationToken);
        var tenant = await Newest(memories.Where(m => m.Scope == AgentMemoryScope.Tenant), limit, cancellationToken);
        return personal.Concat(tenant).OrderByDescending(m => m.CreatedAt).ToList();
    }

    private static Task<List<AgentMemoryDto>> Newest(IQueryable<AgentMemory> memories, int limit, CancellationToken cancellationToken) =>
        memories.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(limit)
            .Select(AgentMemoryProjections.ToDto).ToListAsync(cancellationToken);
}

/// <summary>What the current user may see: their personal memories and the tenant's, not expired.</summary>
internal static class AgentMemoryVisibility
{
    public static IQueryable<AgentMemory> Live(AgenticDbContext dbContext, IHttpContextService httpContext, TimeProvider timeProvider)
    {
        var userId = httpContext.GetUserIdAsGuid()
                     ?? throw new UnauthorizedException(AgenticText.Get("Agent.Error.authentication_required"));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return dbContext.Memories.AsNoTracking()
            .Where(m => m.Scope == AgentMemoryScope.Tenant || m.OwnerUserId == userId)
            .Where(m => m.ExpiresAt == null || m.ExpiresAt > now);
    }
}

public class CreateAgentMemoryCommandValidator : AbstractValidator<CreateAgentMemoryCommand>
{
    public CreateAgentMemoryCommandValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage(_ => localizer["Validation.Memory.Content.Required"])
            .MaximumLength(AgentMemory.ContentMaxLength)
            .WithMessage(_ => localizer["Validation.Memory.Content.MaxLength", AgentMemory.ContentMaxLength]);
        RuleFor(x => x.Scope).IsInEnum().WithMessage(_ => localizer["Validation.Memory.Scope.Invalid"]);
    }
}

/// <summary>A person writes a memory: their own, or (with <c>agentic.manage</c>) one for the whole tenant.</summary>
public class CreateAgentMemoryHandler(AgentMemoryWriter writer, AgentAccess access, AgentCallScope call)
    : ICommandHandler<CreateAgentMemoryCommand, AgentMemoryDto>
{
    public async Task<AgentMemoryDto> Handle(CreateAgentMemoryCommand command, CancellationToken cancellationToken)
    {
        // Defence in depth: never from inside a tool call, where the "person" would really be a model.
        if (call.Principal is not null)
            throw new AgentAccessDeniedException("memory_person_only");

        if (command.Scope == AgentMemoryScope.Tenant && !await access.HasAsync(TenantPermissions.AgentsManage))
            throw new AgentAccessDeniedException("memory_tenant_scope_forbidden");

        var memory = await writer.WriteAsync(command.Scope, access.UserId, command.Content, null, null, cancellationToken);
        return AgentMemoryProjections.Map(memory);
    }
}

public class DeleteAgentMemoryCommandValidator : AbstractValidator<DeleteAgentMemoryCommand>
{
    public DeleteAgentMemoryCommandValidator(IStringLocalizer<AgenticResource> localizer) =>
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

/// <summary>Deletes one of the caller's memories, or a tenant memory for holders of <c>agentic.manage</c>.</summary>
public class DeleteAgentMemoryHandler(IAgentMemoryRepository repository, AgentAccess access)
    : ICommandHandler<DeleteAgentMemoryCommand, bool>
{
    public async Task<bool> Handle(DeleteAgentMemoryCommand command, CancellationToken cancellationToken)
    {
        var memory = await repository.GetByIdAsync(command.Id, cancellationToken)
                     ?? throw new AgentMemoryNotFoundException(command.Id);
        var allowed = memory.Scope == AgentMemoryScope.User
            ? memory.OwnerUserId == access.UserId
            : await access.HasAsync(TenantPermissions.AgentsManage);
        if (!allowed)
            throw new AgentMemoryNotFoundException(command.Id);

        repository.Remove(memory);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

/// <summary>
/// What <c>memory.remember</c> writes once a person approved the exact text: a PERSONAL memory of the user the run
/// acts for (never a tenant one). Internal: only the tool sends it. Idempotent per tool call.
/// </summary>
public sealed record RememberAgentMemoryCommand(string Content, Guid RunId, string IdempotencyKey) : ICommand<AgentMemoryDto>;

public class RememberAgentMemoryHandler(AgentMemoryWriter writer, IAgentMemoryRepository repository, IHttpContextService httpContext)
    : ICommandHandler<RememberAgentMemoryCommand, AgentMemoryDto>
{
    public async Task<AgentMemoryDto> Handle(RememberAgentMemoryCommand command, CancellationToken cancellationToken)
    {
        var userId = httpContext.GetUserIdAsGuid()
                     ?? throw new UnauthorizedException(AgenticText.Get("Agent.Error.authentication_required"));
        if (await repository.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken) is { } existing)
            return AgentMemoryProjections.Map(existing);

        var memory = await writer.WriteAsync(AgentMemoryScope.User, userId, command.Content, command.RunId,
            command.IdempotencyKey, cancellationToken);
        return AgentMemoryProjections.Map(memory);
    }
}
