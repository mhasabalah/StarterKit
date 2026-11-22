using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy.Authorization;

namespace Agentic.Domains.AgentRuns.Services;

/// <summary>
/// Who the HTTP caller is and what they may do with agents and runs.
/// <list type="bullet">
/// <item>Starting a run needs <see cref="TenantPermissions.AgentRunsStart"/> (endpoint policy) AND the agent's own
/// <see cref="AgentDefinition.RequiredPermission"/> (checked here through the same tenant policies, so API-key scopes
/// and single-tenant platform permissions apply exactly as on endpoints).</item>
/// <item>Runs are private to their initiator; holders of <see cref="TenantPermissions.AgentRunsRead"/> may read every
/// run of the tenant. A run the caller may not read is reported as not found.</item>
/// <item>An API key is narrower than its owner: without the read scope it sees only the runs that SAME key started,
/// so a narrowly scoped key cannot read transcripts its owner (or another key) produced with wider permissions.</item>
/// <item>Only the initiator (same rule) or a holder of <see cref="TenantPermissions.AgentsManage"/> may cancel.</item>
/// </list>
/// </summary>
public sealed class AgentAccess(
    IHttpContextAccessor httpContextAccessor,
    IHttpContextService httpContext,
    IAuthorizationService authorization)
{
    public Guid UserId => httpContext.GetUserIdAsGuid()
                          ?? throw new UnauthorizedException(AgenticText.Get("Agent.Error.authentication_required"));

    /// <summary>An API key started the request: its scopes become the run's permission ceiling.</summary>
    public bool IsApiKey => ApiKeyPrincipal.IsApiKey(httpContextAccessor.HttpContext?.User);

    /// <summary>The scopes of the calling API key, or null for an interactive user (no extra restriction).</summary>
    public IReadOnlyCollection<string>? PermissionCeiling =>
        httpContextAccessor.HttpContext?.User is { } user && ApiKeyPrincipal.IsApiKey(user)
            ? ApiKeyPrincipal.Scopes(user)
            : null;

    /// <summary>Id of the calling API key, or null for an interactive user.</summary>
    public Guid? ApiKeyId =>
        httpContextAccessor.HttpContext?.User is { } user && ApiKeyPrincipal.IsApiKey(user) &&
        Guid.TryParse(user.FindFirst(ApiKeyClaimTypes.KeyId)?.Value, out var id)
            ? id
            : null;

    public async Task<bool> HasAsync(string tenantPermission)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
            return false;
        var result = await authorization.AuthorizeAsync(user, TenantPermissions.PolicyName(tenantPermission));
        return result.Succeeded;
    }

    /// <summary>The caller may start <paramref name="agent"/> (the endpoint already required <c>agentic.runs.start</c>).</summary>
    public async Task<bool> CanStartAsync(AgentDefinition agent) =>
        agent.RequiredPermission is null || await HasAsync(agent.RequiredPermission);

    /// <summary>Read filter: the caller's own runs, or every run for holders of <c>agentic.runs.read</c>.</summary>
    public async Task<Expression<Func<AgentRun, bool>>> ReadableAsync()
    {
        if (await HasAsync(TenantPermissions.AgentRunsRead))
            return _ => true;
        var userId = UserId;
        if (!IsApiKey)
            return run => run.InitiatedBy == userId;

        var keyId = ApiKeyId;
        return run => run.InitiatedBy == userId && run.ApiKeyId == keyId;
    }

    /// <summary>The caller started this run (an API key: that same key did).</summary>
    public bool Owns(AgentRun run) => run.InitiatedBy == UserId && (!IsApiKey || run.ApiKeyId == ApiKeyId);

    public async Task<bool> CanCancelAsync(AgentRun run) => Owns(run) || await HasAsync(TenantPermissions.AgentsManage);

    /// <summary>
    /// The caller started this conversation (an API key: that same key did). Only the owner continues, renames or
    /// deletes it: a conversation is one person's, whoever else may read runs.
    /// </summary>
    public bool Owns(AgentConversation conversation) =>
        conversation.OwnerId == UserId && (!IsApiKey || conversation.ApiKeyId == ApiKeyId);

    /// <summary>Filter of the caller's own conversations (an API key: the ones it started).</summary>
    public Expression<Func<AgentConversation, bool>> OwnConversations()
    {
        var userId = UserId;
        if (!IsApiKey)
            return c => c.OwnerId == userId;
        var keyId = ApiKeyId;
        return c => c.OwnerId == userId && c.ApiKeyId == keyId;
    }

    /// <summary>Read filter: the caller's own conversations, or every one for holders of <c>agentic.runs.read</c>.</summary>
    public async Task<Expression<Func<AgentConversation, bool>>> ReadableConversationsAsync() =>
        await HasAsync(TenantPermissions.AgentRunsRead) ? _ => true : OwnConversations();

    /// <summary>
    /// Read filter of approval requests: every request for holders of <c>agentic.approvals.decide</c>, otherwise the
    /// ones of the caller's own runs. An API key without that scope sees none (a decision is for a person).
    /// </summary>
    public async Task<Expression<Func<AgentApproval, bool>>> ReadableApprovalsAsync()
    {
        if (await HasAsync(TenantPermissions.AgentApprovalsDecide))
            return _ => true;
        if (IsApiKey)
            return _ => false;
        var userId = UserId;
        return approval => approval.RequestedBy == userId;
    }
}

/// <summary>SQL projections of runs and steps.</summary>
public static class AgentRunProjections
{
    public static readonly Expression<Func<AgentRun, AgentRunDto>> ToDto = r => new AgentRunDto(
        r.Id, r.AgentKey, r.AgentVersion, r.Status, r.TriggerKind, r.InitiatedBy, r.ModelTurns, r.ToolCalls,
        r.InputTokens, r.OutputTokens, r.EstimatedCost, r.CancelRequested, r.FailureCode, r.FailureMessage,
        r.CreatedAt ?? DateTime.MinValue, r.StartedAt, r.FinishedAt, r.Version, r.ConversationId);

    public static readonly Expression<Func<AgentRunStep, AgentRunStepDto>> StepToDto = s => new AgentRunStepDto(
        s.Id, s.Sequence, s.Kind, s.Status, s.ToolName, s.ToolVersion, s.Text, s.ArgumentsJson, s.ResultJson,
        s.ErrorCode, s.InputTokens, s.OutputTokens, s.DurationMs, s.StartedAt, s.FinishedAt);

    public static readonly Expression<Func<AgentApproval, Contracts.Approvals.AgentApprovalDto>> ApprovalToDto = a =>
        new Contracts.Approvals.AgentApprovalDto(a.Id, a.RunId, a.StepId, a.AgentKey, a.ToolName, a.ToolVersion, a.Risk,
            a.RequiredPermission, a.ArgumentsJson, a.PayloadDigest, a.ModelRationale, a.Status, a.RequestedBy,
            a.RequestedAt, a.ExpiresAt, a.DecidedBy, a.DecidedAt, a.DecisionNote, a.Version);

    private static readonly Func<AgentApproval, Contracts.Approvals.AgentApprovalDto> CompiledApproval = ApprovalToDto.Compile();

    public static Contracts.Approvals.AgentApprovalDto Map(AgentApproval approval) => CompiledApproval(approval);

    private static readonly Func<AgentRun, AgentRunDto> Compiled = ToDto.Compile();

    public static AgentRunDto Map(AgentRun run) => Compiled(run);

    public static AgentDto Map(AgentDefinition agent) => new(agent.Key, agent.Version, agent.Name, agent.Description,
        agent.Autonomy, agent.OutputType is not null, agent.Conversational);
}
