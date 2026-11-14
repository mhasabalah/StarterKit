using System.Security.Cryptography;
using System.Text;
using Agentic.Contracts.Approvals;
using Agentic.Domains.Approvals.Events;
using Shared.Core.Auditing;

namespace Agentic.Domains.Approvals.Models;

/// <summary>
/// A request for a person to approve ONE action an agent proposed: one tool, one tool version, one set of arguments.
/// <list type="bullet">
/// <item><b>Bound to the payload.</b> <see cref="ArgumentsJson"/> is copied from the step when the request is made and
/// never changes; <see cref="PayloadDigest"/> covers the tool name, its version and those arguments. The approver sends
/// the hash they were shown, and the executor compares it again before running the tool, so what is executed is what
/// was approved.</item>
/// <item><b>Single use.</b> Starting the tool moves the approval to <c>Consumed</c> in the same save that marks the
/// step as executing. A replayed or duplicated job finds nothing left to use.</item>
/// <item><b>Not a permission.</b> Approval adds a person's consent; it replaces no check. The initiator's permission,
/// the agent's allowlist and its autonomy are decided again when the run resumes.</item>
/// </list>
/// Audited (<see cref="IAuditable"/>): who approved or rejected what, and when, is part of the audit trail.
/// </summary>
public class AgentApproval : Aggregate<Guid>, IMustHaveTenant, IAuditable
{
    public const int AgentKeyMaxLength = AgentRun.AgentKeyMaxLength;
    public const int ToolNameMaxLength = AgentRunStep.ToolNameMaxLength;
    public const int PermissionMaxLength = 150;
    public const int PayloadDigestLength = 64;
    public const int NoteMaxLength = 1000;
    public const int RationaleMaxLength = 2000;

    public Guid TenantId { get; set; }

    public Guid RunId { get; private set; }
    public Guid StepId { get; private set; }
    public string AgentKey { get; private set; } = default!;

    public string ToolName { get; private set; } = default!;
    public int ToolVersion { get; private set; }
    public AgentToolRisk Risk { get; private set; }

    /// <summary>The tool's own tenant permission: the approver must hold it too.</summary>
    public string? RequiredPermission { get; private set; }

    /// <summary>
    /// The exact arguments that will be executed. Kept out of the audit trail: they are business content, and the
    /// audit trail records <see cref="PayloadDigest"/>, which identifies them without copying them.
    /// </summary>
    [AuditIgnore]
    public string ArgumentsJson { get; private set; } = default!;

    /// <summary>
    /// SHA-256 (hex) of tool name, tool version and arguments: see <see cref="HashOf"/>. Named "digest" on purpose: the
    /// audit trail skips properties whose name contains "Hash" (they are usually secrets), and this value is exactly
    /// the evidence an audit entry of a decision must carry.
    /// </summary>
    public string PayloadDigest { get; private set; } = default!;

    /// <summary>What the model wrote in the turn that proposed the action. Context for the approver; untrusted.</summary>
    [AuditIgnore]
    public string? ModelRationale { get; private set; }

    public AgentApprovalStatus Status { get; private set; }

    /// <summary>The user the run acts for.</summary>
    public Guid RequestedBy { get; private set; }

    public DateTime RequestedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public Guid? DecidedBy { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }

    /// <summary>UI culture of the requester (notifications).</summary>
    public string Culture { get; private set; } = "en";

    private AgentApproval()
    {
    }

    public static AgentApproval Request(AgentRun run, AgentRunStep step, string toolName, int toolVersion,
        AgentToolRisk risk, string? requiredPermission, string argumentsJson, string? modelRationale, DateTime now,
        TimeSpan timeToLive)
    {
        var approval = new AgentApproval
        {
            Id = Guid.CreateVersion7(),
            RunId = run.Id,
            StepId = step.Id,
            AgentKey = run.AgentKey,
            RequestedBy = run.InitiatedBy,
            Culture = run.Culture
        };
        approval.Propose(toolName, toolVersion, risk, requiredPermission, argumentsJson, modelRationale, now, timeToLive);
        return approval;
    }

    /// <summary>
    /// The tool changed (a new version was deployed) after a decision was made for the old one: the decision is void
    /// and the action is proposed again for the tool as it is now.
    /// </summary>
    public void Repropose(int toolVersion, AgentToolRisk risk, string? requiredPermission, DateTime now, TimeSpan timeToLive)
    {
        if (Status is AgentApprovalStatus.Consumed)
            throw new InvalidAgentApprovalStateException("already_used");
        Propose(ToolName, toolVersion, risk, requiredPermission, ArgumentsJson, ModelRationale, now, timeToLive);
    }

    private void Propose(string toolName, int toolVersion, AgentToolRisk risk, string? requiredPermission,
        string argumentsJson, string? modelRationale, DateTime now, TimeSpan timeToLive)
    {
        ToolName = TextLimits.Clip(toolName, ToolNameMaxLength);
        ToolVersion = toolVersion;
        Risk = risk;
        RequiredPermission = requiredPermission;
        ArgumentsJson = argumentsJson;
        PayloadDigest = HashOf(ToolName, toolVersion, argumentsJson);
        ModelRationale = TextLimits.ClipOrNull(modelRationale, RationaleMaxLength);
        Status = AgentApprovalStatus.Pending;
        RequestedAt = now;
        ExpiresAt = now + timeToLive;
        DecidedBy = null;
        DecidedAt = null;
        DecisionNote = null;
        AddDomainEvent(new AgentApprovalRequestedEvent(Id, RunId, AgentKey, ToolName, RequestedBy, Culture));
    }

    public bool IsExpired(DateTime now) => Status == AgentApprovalStatus.Pending && ExpiresAt <= now;

    /// <summary>The decision covers exactly this tool version and these arguments.</summary>
    public bool Covers(string toolName, int toolVersion, string argumentsJson) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(PayloadDigest), Encoding.ASCII.GetBytes(HashOf(toolName, toolVersion, argumentsJson)));

    public void Approve(Guid approver, string? note, DateTime now)
    {
        EnsureDecidable(now);
        Decide(AgentApprovalStatus.Approved, approver, note, now);
    }

    public void Reject(Guid approver, string? reason, DateTime now)
    {
        EnsureDecidable(now);
        Decide(AgentApprovalStatus.Rejected, approver, reason, now);
    }

    /// <summary>The tool is about to start under this approval: it cannot be used again.</summary>
    public void Consume()
    {
        if (Status != AgentApprovalStatus.Approved)
            throw new InvalidAgentApprovalStateException("not_approved");
        Status = AgentApprovalStatus.Consumed;
    }

    /// <summary>Nobody decided in time.</summary>
    public void Expire()
    {
        if (Status != AgentApprovalStatus.Pending)
            throw new InvalidAgentApprovalStateException("not_pending");
        Status = AgentApprovalStatus.Expired;
    }

    /// <summary>The run finished (cancelled, failed) while the request was open or approved but not used yet.</summary>
    public void Cancel()
    {
        if (Status is AgentApprovalStatus.Pending or AgentApprovalStatus.Approved)
            Status = AgentApprovalStatus.Cancelled;
    }

    private void Decide(AgentApprovalStatus status, Guid by, string? note, DateTime now)
    {
        if (by == Guid.Empty)
            throw new InvalidAgentApprovalStateException("approver_required");
        Status = status;
        DecidedBy = by;
        DecidedAt = now;
        DecisionNote = TextLimits.ClipOrNull(note?.Trim(), NoteMaxLength);
        AddDomainEvent(new AgentApprovalDecidedEvent(Id, RunId, status, by));
    }

    private void EnsureDecidable(DateTime now)
    {
        if (Status != AgentApprovalStatus.Pending)
            throw new AgentApprovalConflictException("approval_not_pending");
        if (ExpiresAt <= now)
            throw new AgentApprovalConflictException("approval_expired");
    }

    /// <summary>
    /// SHA-256 over the tool name, its version and the arguments exactly as stored (length-prefixed, so no two
    /// different triples produce the same input).
    /// </summary>
    public static string HashOf(string toolName, int toolVersion, string argumentsJson)
    {
        var text = $"{toolName.Length}:{toolName}|{toolVersion}|{argumentsJson.Length}:{argumentsJson}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
