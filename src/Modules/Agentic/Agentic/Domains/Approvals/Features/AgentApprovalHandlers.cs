using Agentic.Contracts.Approvals;
using Agentic.Domains.AgentRuns.Jobs;
using Agentic.Domains.AgentRuns.Services;
using Hangfire;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.Authorization;

namespace Agentic.Domains.Approvals.Features;

// ----- decide ---------------------------------------------------------------------------------------------------

public class ApproveAgentActionCommandValidator : AbstractValidator<ApproveAgentActionCommand>
{
    public ApproveAgentActionCommandValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.ApprovalId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.PayloadHash).NotEmpty().WithMessage(_ => localizer["Validation.PayloadHash.Required"]);
        RuleFor(x => x.Note).MaximumLength(AgentApproval.NoteMaxLength)
            .WithMessage(_ => localizer["Validation.Note.MaxLength", AgentApproval.NoteMaxLength]);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
    }
}

public class RejectAgentActionCommandValidator : AbstractValidator<RejectAgentActionCommand>
{
    public RejectAgentActionCommandValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.ApprovalId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Reason).MaximumLength(AgentApproval.NoteMaxLength)
            .WithMessage(_ => localizer["Validation.Note.MaxLength", AgentApproval.NoteMaxLength]);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
    }
}

/// <summary>
/// The checks every decision passes, in one place. A decision is a person's consent to ONE payload:
/// <list type="bullet">
/// <item>the decider holds <c>agentic.approvals.decide</c> AND the proposed tool's own permission (nobody approves what
/// they could not do themselves);</item>
/// <item>with <c>Agentic:Approvals:RequireDifferentApprover</c>, not the user the run acts for;</item>
/// <item>the request is still pending and not expired (the aggregate checks it again when it changes state);</item>
/// <item>the optional <c>Version</c> matches, so a decision made on a stale screen is refused.</item>
/// </list>
/// The run is then queued again; whether the action may still run (the initiator's permission, the agent's allowlist,
/// its autonomy) is decided again by the executor, not assumed here.
/// </summary>
public sealed class AgentApprovalDecision(
    IAgentApprovalRepository approvals,
    IAgentRunRepository runs,
    AgenticDbContext dbContext,
    AgentAccess access,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options)
{
    public async Task<AgentApprovalDto> DecideAsync(Guid approvalId, long? version, Action<AgentApproval, Guid, DateTime> decide,
        CancellationToken cancellationToken)
    {
        if (!await access.HasAsync(TenantPermissions.AgentApprovalsDecide))
            throw new AgentApprovalDeniedException("decide_permission_required");

        var approval = await approvals.GetByIdAsync(approvalId, cancellationToken)
                       ?? throw new AgentApprovalNotFoundException(approvalId);

        if (approval.RequiredPermission is { } permission && !await access.HasAsync(permission))
            throw new AgentApprovalDeniedException("approver_lacks_permission", permission);

        var decider = access.UserId;
        if (options.Value.Approvals.RequireDifferentApprover && approval.RequestedBy == decider)
            throw new AgentApprovalDeniedException("approver_must_differ");

        dbContext.ExpectVersion(approval, version);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        decide(approval, decider, now);

        // The run waits for exactly this decision. If it is no longer waiting (it was cancelled or failed a moment
        // ago and its request is about to be closed), there is nothing to decide: nothing is saved.
        var run = await runs.GetByIdAsync(approval.RunId, cancellationToken)
                  ?? throw new AgentApprovalNotFoundException(approvalId);
        if (run.Status != AgentRunStatus.AwaitingApproval)
            throw new AgentApprovalConflictException("run_not_waiting");
        run.Resume(now);

        await approvals.SaveChangesAsync(cancellationToken); // the approval and the run, one transaction
        AgentRunQueue.Enqueue(jobs, run.Id);
        return AgentRunProjections.Map(approval);
    }
}

public class ApproveAgentActionHandler(AgentApprovalDecision decision, ILogger<ApproveAgentActionHandler> logger)
    : ICommandHandler<ApproveAgentActionCommand, AgentApprovalDto>
{
    public async Task<AgentApprovalDto> Handle(ApproveAgentActionCommand command, CancellationToken cancellationToken)
    {
        var result = await decision.DecideAsync(command.ApprovalId, command.Version, (approval, approver, now) =>
        {
            // The approver confirms the payload they were shown. A different payload on record is a conflict, never
            // a silent approval of something else.
            if (!string.Equals(command.PayloadHash.Trim(), approval.PayloadDigest, StringComparison.OrdinalIgnoreCase))
                throw new AgentApprovalConflictException("approval_payload_mismatch");
            approval.Approve(approver, command.Note, now);
        }, cancellationToken);

        logger.LogInformation("Approval {ApprovalId} of agent run {RunId} approved by {UserId}", result.Id, result.RunId,
            result.DecidedBy);
        return result;
    }
}

public class RejectAgentActionHandler(AgentApprovalDecision decision, ILogger<RejectAgentActionHandler> logger)
    : ICommandHandler<RejectAgentActionCommand, AgentApprovalDto>
{
    public async Task<AgentApprovalDto> Handle(RejectAgentActionCommand command, CancellationToken cancellationToken)
    {
        var result = await decision.DecideAsync(command.ApprovalId, command.Version,
            (approval, approver, now) => approval.Reject(approver, command.Reason, now), cancellationToken);

        logger.LogInformation("Approval {ApprovalId} of agent run {RunId} rejected by {UserId}", result.Id, result.RunId,
            result.DecidedBy);
        return result;
    }
}

// ----- read -----------------------------------------------------------------------------------------------------

public class GetAgentApprovalsQueryValidator : AbstractValidator<GetAgentApprovalsQuery>
{
    public GetAgentApprovalsQueryValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
        RuleFor(x => x.Status).IsInEnum().WithMessage(_ => localizer["Validation.Status.Invalid"])
            .When(x => x.Status.HasValue);
    }
}

public class GetAgentApprovalsHandler(AgenticDbContext dbContext, AgentAccess access)
    : IQueryHandler<GetAgentApprovalsQuery, PaginatedResult<AgentApprovalDto>>
{
    public async Task<PaginatedResult<AgentApprovalDto>> Handle(GetAgentApprovalsQuery query, CancellationToken cancellationToken)
    {
        var approvals = dbContext.Approvals.AsNoTracking().Where(await access.ReadableApprovalsAsync());
        if (query.Status is { } status)
            approvals = approvals.Where(a => a.Status == status);
        if (query.RunId is { } runId)
            approvals = approvals.Where(a => a.RunId == runId);

        return await approvals
            .OrderByDescending(a => a.RequestedAt).ThenByDescending(a => a.Id)
            .Select(AgentRunProjections.ApprovalToDto)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}

public class GetAgentApprovalByIdHandler(AgenticDbContext dbContext, AgentAccess access)
    : IQueryHandler<GetAgentApprovalByIdQuery, AgentApprovalDto>
{
    public async Task<AgentApprovalDto> Handle(GetAgentApprovalByIdQuery query, CancellationToken cancellationToken) =>
        await dbContext.Approvals.AsNoTracking()
            .Where(await access.ReadableApprovalsAsync())
            .Where(a => a.Id == query.ApprovalId)
            .Select(AgentRunProjections.ApprovalToDto)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new AgentApprovalNotFoundException(query.ApprovalId);
}
