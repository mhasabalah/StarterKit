using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Agentic.Contracts.Approvals;
using Agentic.Domains.AgentRuns.Jobs;
using Agentic.Domains.AgentRuns.Services;
using Agentic.Runtime.Models;
using Agentic.Runtime.Security;
using Agentic.Telemetry;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Entitlements;

namespace Agentic.Runtime.Execution;

/// <summary>
/// Runs one attempt of an agent run: claim the lease, then loop "handle the next open tool call, else ask the model"
/// until the run finishes, with a saved checkpoint after every model answer and every tool call.
/// <para>
/// <b>Durability</b> (ADR-0026, the pattern of ADR-0016). The run row and its steps are the only state: a worker that
/// stops anywhere leaves a row another attempt continues from. Delivery is at-least-once:
/// </para>
/// <list type="bullet">
/// <item>A crash after a model answered but before the checkpoint repeats the model request (cost only).</item>
/// <item>A step is marked <c>Executing</c> BEFORE its tool runs. A crash in between re-runs an idempotent tool with
/// the same idempotency key; for a tool declared non-idempotent the run stops with <c>tool_outcome_unknown</c>.</item>
/// </list>
/// <para>
/// <b>Retry model</b> (single layer; Hangfire's own retry is off): a failed attempt gives the run back to the queue and
/// schedules the next one with backoff, up to <see cref="AgenticOptions.MaxAttempts"/> consecutive failures without a
/// checkpoint in between; then the run is <c>Failed</c>. Server shutdown releases the run without counting a failure.
/// </para>
/// <para>
/// <b>Authority.</b> The run acts as its initiating user and never as the job server: who they are and what they may
/// do is read again at the start of every attempt and before every tool call (<see cref="AgentPrincipalResolver"/>),
/// and each tool runs inside their tenant and branch scope and an <see cref="ActingUser"/> scope.
/// </para>
/// </summary>
public sealed class AgentRunExecutor(
    IUnitOfWorkRunner unitOfWork,
    ITenantContext tenantContext,
    IBackgroundJobClient jobs,
    AgentRegistry registry,
    IAgentModelGateway models,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    AgenticMetrics metrics,
    AgentRunProgressPublisher progress,
    AgentRunTextStreams textStreams,
    ILogger<AgentRunExecutor> logger)
{
    private const int UpdateRetries = 3;

    /// <summary>Added to the longest time one operation may take when the lease is sized to cover it.</summary>
    private static readonly TimeSpan LeaseMargin = TimeSpan.FromSeconds(30);

    private AgenticOptions Settings => options.Value;
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;
    private TimeSpan Lease => TimeSpan.FromSeconds(Settings.LeaseSeconds);

    private sealed record Snapshot(AgentRun Run, IReadOnlyList<AgentRunStep> Steps);

    private static readonly AgentRunStatus[] FinishedStatuses =
        [AgentRunStatus.Completed, AgentRunStatus.Failed, AgentRunStatus.Cancelled];

    /// <summary>Entry point of the Hangfire job.</summary>
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var claimed = await TryClaimAsync(runId, owner);
        if (claimed is null)
            return;

        using var culture = AgenticText.UseCulture(claimed.Culture);
        using var activity = AgenticTelemetry.StartRun(claimed, claimed.TraceParent);
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = claimed.CorrelationId,
            ["AgentRunId"] = claimed.Id
        });
        // Audit columns and audit entries of everything this attempt changes name the initiating user and this run.
        using var actingUser = ActingUser.Begin(claimed.InitiatedBy, claimed.CorrelationId);

        try
        {
            if (!Settings.Enabled)
            {
                await FailAsync(runId, owner, "agents_disabled");
                return;
            }

            // An MCP call runs inside its request (ADR-0034). Found by a worker, it was interrupted: never run it as an
            // agent run, whose model would see a prompt nobody wrote.
            if (claimed.TriggerKind == AgentTriggerKind.Mcp)
            {
                await FailAsync(runId, owner, "mcp_call_interrupted");
                return;
            }

            var agent = registry.FindAgent(claimed.AgentKey);
            if (agent is null)
            {
                await FailAsync(runId, owner, "agent_not_found", claimed.AgentKey);
                return;
            }

            if (claimed.AgentVersion < agent.Definition.MinResumableVersion)
            {
                await FailAsync(runId, owner, "agent_version_retired");
                return;
            }

            if (await ResolvePrincipalAsync(claimed) is null)
            {
                logger.LogWarning("Agent run {RunId}: initiator {UserId} lost access; the run is stopped", runId,
                    claimed.InitiatedBy);
                await FailAsync(runId, owner, "initiator_no_longer_authorized");
                return;
            }

            await RunLoopAsync(runId, owner, agent, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            await HandleFailureAsync(runId, owner, ex, cancellationToken);
        }
    }

    // ----- the loop ---------------------------------------------------------------------------------------------

    private async Task RunLoopAsync(Guid runId, string owner, RegisteredAgent agent, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await LoadAsync(runId, owner);

            if (snapshot.Run.CancelRequested)
            {
                await UpdateUnderLeaseAsync(runId, owner, run => run.ConfirmCancelled(Now));
                return;
            }

            var open = snapshot.Steps.FirstOrDefault(s => s.Kind == AgentRunStepKind.ToolCall && s.IsOpen);
            var goOn = open is not null
                ? await HandleToolCallAsync(snapshot, open, agent, owner, cancellationToken)
                : await ModelTurnAsync(snapshot, agent, owner, cancellationToken);
            if (!goOn)
                return;
        }
    }

    /// <summary>Asks the model for its next move and checkpoints the answer. False when the run finished.</summary>
    private async Task<bool> ModelTurnAsync(Snapshot snapshot, RegisteredAgent agent, string owner,
        CancellationToken cancellationToken)
    {
        var run = snapshot.Run;
        if (run.ExhaustedBudget() is { } budget)
        {
            await FailAsync(run.Id, owner, "budget_exceeded", budget);
            return false;
        }

        var history = run.ConversationId is { } conversationId ? await LoadHistoryAsync(run, conversationId) : null;
        var request = TranscriptBuilder.Build(agent, run, snapshot.Steps, history);

        // The lease is renewed at checkpoints only, and there is none during a model request: make sure it outlives
        // the longest the provider client may take (its timeout times its retries), or a second worker would take the
        // run over while this one is still waiting for the answer.
        if (LeaseFor(ModelRequestBound(run.ModelKey)) is var needed && needed > Lease)
            await UpdateUnderLeaseAsync(run.Id, owner, _ => { }, needed);

        ModelResponse response;
        long elapsedMs;
        using (var span = AgenticTelemetry.StartModelTurn(run))
        {
            var started = timeProvider.GetTimestamp();
            // The answer's text goes to the initiator while it is written (a preview: the checkpoint below is the answer).
            await using (var textStream = textStreams.Open(run))
                response = await models.CompleteAsync(request, textStream, cancellationToken);
            elapsedMs = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
            span?.SetTag("agent.model.input_tokens", response.InputTokens);
            span?.SetTag("agent.model.output_tokens", response.OutputTokens);
            span?.SetTag("agent.model.tool_calls", response.ToolCalls.Count);
        }

        // More calls in one turn than the limit: the rest is ignored (never stored, so the model is not told a result
        // for a call that did not happen and simply asks again if it still wants it).
        var calls = response.ToolCalls.Take(Settings.Limits.MaxToolCallsPerTurn).ToList();
        var typed = agent.OutputSchema is not null;

        // Model output is untrusted, its size included: an oversized answer is never stored (it stops the run below),
        // and a call with oversized arguments is saved without them, already refused.
        var textTooLarge = response.Text is { } text && Encoding.UTF8.GetByteCount(text) > Settings.Limits.MaxModelTextBytes;
        if (textTooLarge)
            calls.Clear();
        var oversized = calls
            .Where(call => Encoding.UTF8.GetByteCount(call.ArgumentsJson) > Settings.Limits.MaxToolArgumentsBytes)
            .ToHashSet();
        var tooLargeMessage = AgenticText.GetOrDefault("Agent.Tool.arguments_too_large", "Agent.Tool.refused");

        var goOn = await UpdateUnderLeaseAsync(run.Id, owner, (current, repository) =>
        {
            var turn = current.RecordModelTurn(textTooLarge ? null : response.Text, response.ModelId, response.FinishReason,
                response.InputTokens, response.OutputTokens, response.EstimatedCost, elapsedMs, Now);
            var added = new List<AgentRunStep>(calls.Count + 1) { turn };
            added.AddRange(calls.Select(call => oversized.Contains(call)
                ? current.RefuseToolCall(turn, call.CallId, call.ToolName, "arguments_too_large",
                    AgentJson.Error("arguments_too_large", tooLargeMessage), Now)
                : current.PlanToolCall(turn, call.CallId, call.ToolName, call.ArgumentsJson)));
            repository.AddSteps(added);

            // Stopped in the same save as the turn: no attempt ever replays an answer whose text was not kept.
            if (textTooLarge)
            {
                current.Fail("output_too_large",
                    AgenticText.GetOrDefault("Agent.Failure.output_too_large", "Agent.Failure.internal_error"), Now);
                return false;
            }

            // A free-text agent is finished when the model answers without asking for a tool.
            if (calls.Count == 0 && !typed)
            {
                current.Complete(null, response.Text ?? string.Empty, Now);
                return false;
            }

            return true;
        });

        metrics.RecordModelTurn(run, response.InputTokens, response.OutputTokens);
        foreach (var call in oversized)
        {
            var tool = call.ToolName == AgentToolNames.SubmitResult
                ? AgentToolNames.SubmitResult
                : registry.FindTool(call.ToolName)?.Name ?? "unknown";
            metrics.RecordToolCall(tool, "arguments_too_large");
        }

        // Plan quota "agent_tokens": every saved turn is counted, the last one of a run included. When the plan cannot
        // cover a turn, nothing is deducted for it (the quota service consumes all or nothing) and the run stops
        // before its next step; a run that just finished stays finished.
        var tokens = response.InputTokens + response.OutputTokens;
        if (tokens > 0 && !await ConsumeTokensAsync(tokens) && goOn)
        {
            await FailAsync(run.Id, owner, QuotaExceededException.ErrorCodeValue);
            return false;
        }

        if (textTooLarge)
        {
            logger.LogWarning("Agent run {RunId}: the model answered with more than {Limit} bytes; the run is stopped",
                run.Id, Settings.Limits.MaxModelTextBytes);
        }

        return goOn;
    }

    /// <summary>Authorizes and runs one tool call, and checkpoints its outcome. False when the run finished.</summary>
    private async Task<bool> HandleToolCallAsync(Snapshot snapshot, AgentRunStep step, RegisteredAgent agent, string owner,
        CancellationToken cancellationToken)
    {
        var run = snapshot.Run;
        if (step.ToolName == AgentToolNames.SubmitResult)
        {
            // Handing in the result ends the run in the initiator's name too: not for someone who lost access.
            if (await ResolvePrincipalAsync(run) is null)
            {
                await FailAsync(run.Id, owner, "initiator_no_longer_authorized");
                return false;
            }

            return await HandleSubmitResultAsync(snapshot, step, agent, owner, cancellationToken);
        }

        var tool = step.ToolName is null ? null : registry.FindTool(step.ToolName);

        // Found in "Executing": a worker stopped while this tool ran, so whether it took effect is unknown.
        if (step.Status == AgentRunStepStatus.Executing && tool is { Idempotent: false })
        {
            logger.LogError("Agent run {RunId}: the non-idempotent tool {Tool} was interrupted (step {StepId}); its outcome is unknown",
                run.Id, tool.Name, step.Id);
            await FailAsync(run.Id, owner, "tool_outcome_unknown", tool.Name);
            return false;
        }

        if (run.ToolCallBudgetExhausted)
        {
            await FailAsync(run.Id, owner, "budget_exceeded", "max_tool_calls");
            return false;
        }

        // The initiator's permissions as of NOW (not as of the start of the run or of this attempt).
        var principal = await ResolvePrincipalAsync(run);
        if (principal is null)
        {
            logger.LogWarning("Agent run {RunId}: initiator {UserId} lost access; the run is stopped", run.Id, run.InitiatedBy);
            await FailAsync(run.Id, owner, "initiator_no_longer_authorized");
            return false;
        }

        if (step.ToolName == AgentToolNames.Delegate)
            return await HandleDelegateAsync(run, step, agent, principal, owner);

        var arguments = step.ArgumentsJson ?? "{}";
        var decision = ToolPolicy.Decide(agent, tool, run.Autonomy, run.ChangesNeedApproval, principal.Permissions);
        if (decision.Kind == ToolDecisionKind.Refuse || tool is null)
        {
            await RefuseAsync(run, step, owner, tool, decision.Code ?? ToolPolicy.ToolNotAllowed);
            return true;
        }

        if (Encoding.UTF8.GetByteCount(arguments) > Settings.Limits.MaxToolArgumentsBytes)
        {
            await RefuseAsync(run, step, owner, tool, "arguments_too_large");
            return true;
        }

        // Shape and validators only for a caller who may use the tool at all (validators may read data).
        var check = await InPrincipalScope(principal, run,
            (services, ct) => tool.Invoker.CheckAsync(services, arguments, ct), CancellationToken.None);
        if (!check.IsValid)
        {
            await RefuseAsync(run, step, owner, tool, "invalid_arguments", check.Message);
            return true;
        }

        var executing = false;
        if (decision.Kind == ToolDecisionKind.RequireApproval)
        {
            // An action that needs a person is never executed without one: the gate suspends the run until a decision
            // exists, and lets the tool start only under an approval that covers exactly this payload.
            switch (await ApprovalGateAsync(snapshot, step, tool, arguments, owner))
            {
                case ApprovalGate.Suspended:
                    logger.LogInformation("Agent run {RunId}: waiting for approval of {Tool} (step {StepId})", run.Id,
                        tool.Name, step.Id);
                    return false;
                case ApprovalGate.Cancelled:
                    return false;
                case ApprovalGate.Closed:
                    return true;
            }

            executing = true; // the gate already saved the step as Executing, together with using up the approval
        }

        return await ExecuteToolAsync(run, step, tool, principal, arguments, owner, executing, cancellationToken);
    }

    private enum ApprovalGate
    {
        /// <summary>A valid approval was used up and the step is saved as Executing: run the tool now.</summary>
        Approved,

        /// <summary>No decision yet: the run is suspended and this attempt ends.</summary>
        Suspended,

        /// <summary>The step was closed without running the tool (rejected, or the approval is gone).</summary>
        Closed,

        /// <summary>A cancel request arrived meanwhile: the run was cancelled instead of being suspended or executed.</summary>
        Cancelled
    }

    /// <summary>
    /// One save decides what the approval of this step allows, so there is no window between checking an approval and
    /// using it: the same transaction that marks the step as executing moves the approval to <c>Consumed</c>, under
    /// the run's lease and the approval's own version.
    /// </summary>
    private async Task<ApprovalGate> ApprovalGateAsync(Snapshot snapshot, AgentRunStep step, AgentToolDescriptor tool,
        string arguments, string owner)
    {
        var rationale = snapshot.Steps
            .FirstOrDefault(s => s.Kind == AgentRunStepKind.ModelTurn && s.Sequence == step.TurnSequence)?.Text;
        var timeToLive = TimeSpan.FromHours(Settings.Approvals.ExpireAfterHours);

        var gate = await SaveUnderLeaseAsync(snapshot.Run.Id, owner, async (run, repository) =>
        {
            var now = Now;

            // The run row is read again here, under the lease. A cancel that arrived since the loop last looked must
            // win: otherwise the run would be parked waiting for a person, with nobody left to confirm the cancel.
            if (run.CancelRequested)
            {
                run.ConfirmCancelled(now);
                return ApprovalGate.Cancelled;
            }

            var current = await repository.GetStepAsync(run.Id, step.Id)
                          ?? throw new InvalidOperationException($"Agent run {run.Id} has no step {step.Id}.");
            var approval = await repository.FindApprovalAsync(step.Id);

            switch (approval?.Status)
            {
                case null:
                    repository.AddApproval(AgentApproval.Request(run, current, tool.Name, tool.Version, tool.Risk,
                        tool.Permission, arguments, rationale, now, timeToLive));
                    current.AwaitApproval(tool.Version);
                    run.Suspend();
                    return ApprovalGate.Suspended;

                case AgentApprovalStatus.Pending:
                    run.Suspend();
                    return ApprovalGate.Suspended;

                case AgentApprovalStatus.Approved when approval.Covers(tool.Name, tool.Version, arguments):
                    approval.Consume();
                    current.MarkExecuting(tool.Version, now);
                    return ApprovalGate.Approved;

                case AgentApprovalStatus.Approved:
                    // The tool was changed after the decision: consent given for the old version does not carry over.
                    approval.Repropose(tool.Version, tool.Risk, tool.Permission, now, timeToLive);
                    current.AwaitApproval(tool.Version);
                    run.Suspend();
                    return ApprovalGate.Suspended;

                // The approved action had started when a worker stopped. Only an idempotent tool gets here (an
                // interrupted non-idempotent one already stopped the run): it runs again under the SAME approval, for
                // the same payload and with the same idempotency key.
                case AgentApprovalStatus.Consumed when current.Status == AgentRunStepStatus.Executing &&
                                                       approval.Covers(tool.Name, tool.Version, arguments):
                    current.MarkExecuting(tool.Version, now);
                    return ApprovalGate.Approved;

                case AgentApprovalStatus.Rejected:
                    var message = AgenticText.Get("Agent.Tool.rejected");
                    if (!string.IsNullOrWhiteSpace(approval.DecisionNote))
                        message = $"{message} {approval.DecisionNote}";
                    current.Reject(AgentJson.Error("rejected", message), now);
                    run.RecordToolCall(current, executed: false, untrustedContent: false);
                    return ApprovalGate.Closed;

                default:
                    current.FailWith("approval_unavailable",
                        AgentJson.Error("approval_unavailable", AgenticText.Get("Agent.Tool.approval_unavailable")), 0, now,
                        tool.Version);
                    run.RecordToolCall(current, executed: false, untrustedContent: false);
                    return ApprovalGate.Closed;
            }
        }, LeaseFor(tool.Timeout));

        if (gate == ApprovalGate.Closed)
            metrics.RecordToolCall(tool.Name, "not_approved");
        return gate;
    }

    private async Task<bool> ExecuteToolAsync(AgentRun run, AgentRunStep step, AgentToolDescriptor tool,
        AgentPrincipal principal, string arguments, string owner, bool alreadyExecuting,
        CancellationToken cancellationToken)
    {
        // Checkpoint BEFORE the side effect, with a lease that outlives the tool's own timeout.
        if (!alreadyExecuting)
        {
            await UpdateStepUnderLeaseAsync(run.Id, step.Id, owner,
                (_, current) => current.MarkExecuting(tool.Version, Now), LeaseFor(tool.Timeout));
        }

        var context = new AgentToolContext(tenantContext.TenantId, run.InitiatedBy, run.Id, step.Id, run.AgentKey,
            run.CorrelationId, $"{run.Id:N}:{step.Id:N}", run.Culture);

        string? resultJson = null;
        string? errorCode = null;
        using var span = AgenticTelemetry.StartTool(run, step);
        var started = timeProvider.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(tool.Timeout);
        try
        {
            resultJson = await InPrincipalScope(principal, run,
                (services, ct) => tool.Invoker.InvokeAsync(services, arguments, context, ct), timeout.Token);
            if (Encoding.UTF8.GetByteCount(resultJson) > Settings.Limits.MaxToolResultBytes)
            {
                if (tool.IsMutation)
                {
                    // The change WAS made. Reporting an error would invite the model to do it again (a new step, so
                    // a new idempotency key): the call stays a success and only its payload is withheld.
                    resultJson = JsonSerializer.Serialize(
                        new { ok = true, note = AgenticText.Get("Agent.Tool.result_omitted") }, AgentJson.Output);
                }
                else
                {
                    errorCode = "result_too_large";
                    resultJson = AgentJson.Error(errorCode, AgenticText.Get("Agent.Tool.result_too_large"));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutdown: the step stays Executing and the next attempt deals with it
        }
        catch (OperationCanceledException) when (!tool.IsMutation)
        {
            errorCode = "timeout";
            resultJson = AgentJson.Error(errorCode, AgenticText.Get("Agent.Tool.timeout"));
        }
        catch (InvalidToolArgumentsException ex)
        {
            errorCode = "invalid_arguments";
            resultJson = AgentJson.Error(errorCode, ex.Message);
        }
        catch (ValidationException ex)
        {
            errorCode = "validation_failed";
            resultJson = AgentJson.Error(errorCode, string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)));
        }
        catch (CustomException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            // A business answer (not found, conflict, forbidden, bad request): the model is told and may react.
            errorCode = ex.ErrorCode ?? ToCode(ex.StatusCode);
            resultJson = AgentJson.Error(errorCode, ex.Message);
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            errorCode = "conflict";
            resultJson = AgentJson.Error(errorCode, AgenticText.Get("Agent.Tool.conflict"));
        }
        catch (Exception ex) when (!tool.Idempotent)
        {
            // An unexpected failure (or a timeout) of a tool that must not be repeated: nobody knows whether it took
            // effect, so the run stops here for a person instead of guessing.
            logger.LogError(ex, "Agent run {RunId}: the non-idempotent tool {Tool} failed unexpectedly (step {StepId})",
                run.Id, tool.Name, step.Id);
            span?.SetStatus(ActivityStatusCode.Error, "outcome_unknown");
            metrics.RecordToolCall(tool.Name, "outcome_unknown");
            await FailAsync(run.Id, owner, "tool_outcome_unknown", tool.Name);
            return false;
        }
        // Anything else is an infrastructure failure of an idempotent tool: the attempt fails and is retried with
        // backoff; the same step runs again with the same idempotency key.

        var elapsedMs = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        span?.SetTag("agent.tool.outcome", errorCode ?? "ok");
        if (errorCode is not null)
            span?.SetStatus(ActivityStatusCode.Error, errorCode);

        await UpdateStepUnderLeaseAsync(run.Id, step.Id, owner, (current, currentStep) =>
        {
            if (errorCode is null)
                currentStep.Succeed(resultJson, elapsedMs, Now);
            else
                currentStep.FailWith(errorCode, resultJson, elapsedMs, Now);
            current.RecordToolCall(currentStep, executed: true, untrustedContent: tool.ReturnsUntrustedContent);
        });

        metrics.RecordToolCall(tool.Name, errorCode ?? "ok");
        return true;
    }

    /// <summary>The model handed in the typed result: validate it against the agent's output type, then finish.</summary>
    private async Task<bool> HandleSubmitResultAsync(Snapshot snapshot, AgentRunStep step, RegisteredAgent agent,
        string owner, CancellationToken cancellationToken)
    {
        var run = snapshot.Run;
        if (agent.Definition.OutputType is not { } outputType)
        {
            await RefuseAsync(run, step, owner, null, ToolPolicy.ToolNotAllowed);
            return true;
        }

        var (normalized, error) = await unitOfWork.RunAsync(
            (services, ct) => TypedResult.ParseAsync(services, outputType, step.ArgumentsJson, ct), cancellationToken);
        if (normalized is null)
        {
            await UpdateStepUnderLeaseAsync(run.Id, step.Id, owner, (current, currentStep) =>
            {
                currentStep.FailWith("invalid_result", AgentJson.Error("invalid_result", error ?? string.Empty), 0, Now);
                current.RecordToolCall(currentStep, executed: false, untrustedContent: false);
            });
            metrics.RecordToolCall(AgentToolNames.SubmitResult, "invalid_result");
            return true;
        }

        var lastText = snapshot.Steps.LastOrDefault(s => s.Kind == AgentRunStepKind.ModelTurn)?.Text;
        await SaveUnderLeaseAsync(run.Id, owner, async (current, repository) =>
        {
            var now = Now;
            foreach (var other in await repository.GetStepsAsync(current.Id))
            {
                if (other.Kind != AgentRunStepKind.ToolCall || !other.IsOpen)
                    continue;
                if (other.Id == step.Id)
                    other.Succeed("""{"accepted":true}""", 0, now);
                else
                    other.Skip(AgentJson.Error("skipped", AgenticText.Get("Agent.Tool.skipped")), now);
            }

            current.Complete(normalized, lastText, now);
            return true;
        });

        metrics.RecordToolCall(AgentToolNames.SubmitResult, "ok");
        return false;
    }

    /// <summary>
    /// <c>agent.delegate</c> (ADR-0033): starts a run of another agent for the same person and suspends this run until
    /// it finishes. The delegated run acts at the lower of the two autonomies, inherits the permission ceiling and the
    /// taint, may use at most this run's remaining tokens, and goes through <see cref="AgentRunStarter"/> (policy,
    /// capacity, quota). A retried attempt finds the run it already started (one per step, a unique index) instead of
    /// starting another. False when this attempt ends (suspended).
    /// </summary>
    private async Task<bool> HandleDelegateAsync(AgentRun run, AgentRunStep step, RegisteredAgent agent,
        AgentPrincipal principal, string owner)
    {
        if (agent.Delegation is null)
        {
            await RefuseAsync(run, step, owner, null, ToolPolicy.ToolNotAllowed);
            return true;
        }

        if (!AgentDelegation.TryParse(step.ArgumentsJson, Settings.Limits.MaxPromptLength, out var arguments, out var error))
        {
            await RefuseAsync(run, step, owner, null, "invalid_arguments", error);
            return true;
        }

        var child = agent.MayDelegateTo(arguments!.AgentKey) ? registry.FindAgent(arguments.AgentKey) : null;
        if (child is null)
        {
            await RefuseAsync(run, step, owner, null, ToolPolicy.ToolNotAllowed);
            return true;
        }

        if (run.Depth + 1 > Settings.Delegation.MaxDepth)
        {
            await RefuseAsync(run, step, owner, null, "delegation_too_deep");
            return true;
        }

        // The person may start the delegated agent NOW (as for any start); its own tools are checked by its own run.
        if (child.Definition.RequiredPermission is { } permission && !principal.Permissions.Contains(permission))
        {
            await RefuseAsync(run, step, owner, null, ToolPolicy.Forbidden);
            return true;
        }

        var remainingTokens = run.MaxTokens - run.TotalTokens;
        if (remainingTokens <= 0)
        {
            await FailAsync(run.Id, owner, "budget_exceeded", "max_tokens");
            return false;
        }

        var started = await unitOfWork.RunAsync((services, ct) =>
            services.GetRequiredService<IAgentRunRepository>().FindDelegateAsync(step.Id, ct), CancellationToken.None);
        if (started is null)
        {
            try
            {
                started = await InPrincipalScope(principal, run, (services, ct) => services.GetRequiredService<AgentRunStarter>()
                    .StartAsync(new AgentRunStart(child, AgentTriggerKind.Delegated, run.InitiatedBy, arguments.Task, null,
                        run.Culture, run.PermissionCeiling, run.ApiKeyId, StartTainted: run.Tainted, ParentRunId: run.Id,
                        ParentStepId: step.Id, Depth: run.Depth + 1, AutonomyCeiling: run.Autonomy,
                        TokenCeiling: remainingTokens), ct), CancellationToken.None);
            }
            catch (CustomException ex) when ((int)ex.StatusCode is >= 400 and < 500)
            {
                // Refused by the policy, the capacity or the plan: the model is told, as for a refused tool.
                await RefuseAsync(run, step, owner, null, ex.ErrorCode ?? "delegation_refused", ex.Message);
                return true;
            }

            logger.LogInformation("Agent run {RunId}: delegated to {Agent} (run {ChildId})", run.Id, child.Definition.Key,
                started.Id);
        }

        var delegated = started;
        var resumeNow = await SaveUnderLeaseAsync(run.Id, owner, async (current, repository) =>
        {
            var currentStep = await repository.GetStepAsync(run.Id, step.Id)
                              ?? throw new InvalidOperationException($"Agent run {run.Id} has no step {step.Id}.");
            current.AwaitDelegate(currentStep);

            // Finished already (a fast run, or a retried attempt): take its outcome now instead of waiting for it.
            var latest = await repository.FindDelegateAsync(step.Id);
            if (latest is not { IsTerminal: true })
                return false;
            return current.CompleteDelegation(currentStep, AgentDelegation.Result(latest),
                latest.Status == AgentRunStatus.Completed, latest.Tainted, latest.InputTokens, latest.OutputTokens,
                latest.EstimatedCost, AgentDelegation.DurationMs(latest), Now);
        });

        if (resumeNow)
            AgentRunQueue.Enqueue(jobs, run.Id);
        metrics.RecordToolCall(AgentToolNames.Delegate, resumeNow ? "ok" : "delegated");
        logger.LogInformation("Agent run {RunId}: waiting for delegated run {ChildId}", run.Id, delegated.Id);
        return false;
    }

    /// <summary>Not executed: the step is closed with the reason, which is also what the model is told.</summary>
    private async Task RefuseAsync(AgentRun run, AgentRunStep step, string owner, AgentToolDescriptor? tool, string code,
        string? detail = null)
    {
        var message = AgenticText.GetOrDefault("Agent.Tool." + code, "Agent.Tool.refused");
        if (!string.IsNullOrWhiteSpace(detail))
            message = $"{message} {detail}";

        await UpdateStepUnderLeaseAsync(run.Id, step.Id, owner, (current, currentStep) =>
        {
            currentStep.FailWith(code, AgentJson.Error(code, message), 0, Now, tool?.Version);
            current.RecordToolCall(currentStep, executed: false, untrustedContent: false);
        });

        logger.LogInformation("Agent run {RunId}: tool call {StepId} ({Tool}) refused: {Code}", run.Id, step.Id,
            tool?.Name ?? "unknown", code);
        metrics.RecordToolCall(tool?.Name ?? "unknown", code);
    }

    /// <summary>
    /// False when the subscriber's plan has no tokens left. The subscriber is the tenant (multi-tenant mode) or the
    /// acting user. Fails open when the counter store is down (<see cref="IQuotaService"/> contract).
    /// </summary>
    private async Task<bool> ConsumeTokensAsync(long tokens)
    {
        var result = await unitOfWork.RunAsync((services, ct) => services.GetRequiredService<IQuotaService>()
            .TryConsumeAsync(EntitlementKeys.AgentTokens, tokens, ct), CancellationToken.None);
        return result.Allowed;
    }

    // ----- trusted context --------------------------------------------------------------------------------------

    private Task<AgentPrincipal?> ResolvePrincipalAsync(AgentRun run) =>
        unitOfWork.RunAsync((services, ct) => services.GetRequiredService<AgentPrincipalResolver>()
            .ResolveAsync(run.InitiatedBy, run.PermissionCeiling, ct), CancellationToken.None);

    /// <summary>
    /// Runs <paramref name="work"/> exactly as a request of the initiating user would run: their tenant, their CURRENT
    /// membership and branch scope, them as the acting user, and one unit of work (domain events dispatched after it).
    /// </summary>
    private async Task<T> InPrincipalScope<T>(AgentPrincipal principal, AgentRun run,
        Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        using var scope = principal.Membership is { } membership && tenantContext.Tenant is { } tenant
            ? TenantScope.Begin(tenant, membership: membership)
            : null;
        using var actingUser = ActingUser.Begin(run.InitiatedBy, run.CorrelationId);
        return await unitOfWork.RunAsync((services, ct) =>
        {
            services.GetRequiredService<AgentCallScope>().Enter(principal, run.Id);
            return work(services, ct);
        }, cancellationToken);
    }

    /// <summary>The lease an operation of this length needs (never shorter than the configured lease).</summary>
    private TimeSpan LeaseFor(TimeSpan operation) => operation + LeaseMargin > Lease ? operation + LeaseMargin : Lease;

    /// <summary>The longest one model request may take inside the provider client: every attempt and its backoff.</summary>
    private TimeSpan ModelRequestBound(string modelKey) =>
        Settings.Models.TryGetValue(modelKey, out var model) && Settings.Providers.TryGetValue(model.Provider, out var provider)
            ? TimeSpan.FromSeconds(((long)provider.TimeoutSeconds * (provider.MaxRetries + 1)) + (15L * provider.MaxRetries))
            : TimeSpan.Zero;

    // ----- persistence under the lease --------------------------------------------------------------------------
    // Checkpoints run to completion (CancellationToken.None): a shutdown must not abort the save that records what
    // already happened.

    /// <summary>Claims the run; null when there is nothing to do (finished, suspended, gone, or leased by another worker).</summary>
    private async Task<AgentRun?> TryClaimAsync(Guid runId, string owner)
    {
        try
        {
            return await unitOfWork.RunAsync(async (services, ct) =>
            {
                var repository = services.GetRequiredService<IAgentRunRepository>();
                var run = await repository.GetByIdAsync(runId, ct);
                if (run is null || !run.TryClaim(owner, Now, Lease))
                    return null;

                await repository.SaveChangesAsync(ct);
                await progress.PublishAsync(run); // started, or resumed after an approval or a retry
                return run;
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            logger.LogInformation("Agent run {RunId} was claimed by another worker", runId);
            return null;
        }
    }

    private Task<Snapshot> LoadAsync(Guid runId, string owner) =>
        unitOfWork.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<AgenticDbContext>();
            var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run is null || !run.HoldsLease(owner))
                throw new AgentRunLeaseLostException(runId);
            var steps = await db.Steps.AsNoTracking().Where(s => s.RunId == runId).OrderBy(s => s.Sequence).ToListAsync(ct);
            return new Snapshot(run, steps);
        }, CancellationToken.None);

    /// <summary>
    /// The earlier turns of the run's conversation the model is given: finished turns numbered before this one, whose
    /// content still exists, the newest first up to <see cref="AgentConversationOptions.MaxHistoryTurns"/>; then
    /// bounded by characters (<see cref="TranscriptBuilder.History"/>). Read again for every model turn, so a turn
    /// whose content was erased meanwhile is no longer sent.
    /// </summary>
    private Task<IReadOnlyList<ModelMessage>> LoadHistoryAsync(AgentRun run, Guid conversationId) =>
        unitOfWork.RunAsync(async (services, ct) =>
        {
            var limits = Settings.Conversations;
            if (limits.MaxHistoryTurns == 0 || limits.MaxHistoryCharacters == 0)
                return (IReadOnlyList<ModelMessage>)[];

            var db = services.GetRequiredService<AgenticDbContext>();
            // A summary covers the oldest turns (ADR-0031): it is sent first, and only the turns after it follow.
            var summary = await db.Conversations.AsNoTracking()
                .Where(c => c.Id == conversationId && c.SummaryThroughTurn < run.ConversationTurn)
                .Select(c => new { c.Summary, c.SummaryThroughTurn })
                .FirstOrDefaultAsync(ct);
            var after = summary?.Summary is null ? 0 : summary.SummaryThroughTurn;
            var turns = await db.Runs.AsNoTracking()
                .Where(r => r.ConversationId == conversationId && r.ConversationTurn > after &&
                            r.ConversationTurn < run.ConversationTurn &&
                            FinishedStatuses.Contains(r.Status) && r.ContentErasedAt == null)
                .OrderByDescending(r => r.ConversationTurn)
                .Take(limits.MaxHistoryTurns)
                .Select(r => new ConversationTurn(r.Prompt, r.InputJson, r.OutputText, r.OutputJson, r.Status))
                .ToListAsync(ct);
            turns.Reverse();
            return TranscriptBuilder.History(turns, limits.MaxHistoryTurns, limits.MaxHistoryCharacters,
                after > 0 ? summary!.Summary : null);
        }, CancellationToken.None);

    /// <summary>
    /// Loads the run, checks this attempt still holds the lease, applies <paramref name="change"/>, renews the lease
    /// and saves. A concurrent change of the row (a cancel request) is reloaded and applied again.
    /// </summary>
    private async Task<T> SaveUnderLeaseAsync<T>(Guid runId, string owner,
        Func<AgentRun, IAgentRunRepository, Task<T>> change, TimeSpan? lease = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await unitOfWork.RunAsync(async (services, ct) =>
                {
                    var repository = services.GetRequiredService<IAgentRunRepository>();
                    var run = await repository.GetByIdAsync(runId, ct);
                    if (run is null || !run.HoldsLease(owner))
                        throw new AgentRunLeaseLostException(runId);

                    run.RenewLease(Now, lease ?? Lease);
                    var result = await change(run, repository);
                    await repository.SaveChangesAsync(ct);
                    if (run.IsTerminal)
                        metrics.RecordFinished(run); // the final state is pushed by AgentRunFinishedEventHandler
                    else
                        await progress.PublishAsync(run, repository.LastTouchedStep);
                    return result;
                }, CancellationToken.None);
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict() && attempt < UpdateRetries)
            {
                logger.LogDebug(ex, "Agent run {RunId} changed concurrently; applying again", runId);
            }
        }
    }

    private Task<T> UpdateUnderLeaseAsync<T>(Guid runId, string owner, Func<AgentRun, IAgentRunRepository, T> change) =>
        SaveUnderLeaseAsync(runId, owner, (run, repository) => Task.FromResult(change(run, repository)));

    private Task UpdateUnderLeaseAsync(Guid runId, string owner, Action<AgentRun> change, TimeSpan? lease = null) =>
        SaveUnderLeaseAsync(runId, owner, (run, _) =>
        {
            change(run);
            return Task.FromResult(true);
        }, lease);

    private Task UpdateStepUnderLeaseAsync(Guid runId, Guid stepId, string owner, Action<AgentRun, AgentRunStep> change,
        TimeSpan? lease = null) =>
        SaveUnderLeaseAsync(runId, owner, async (run, repository) =>
        {
            var step = await repository.GetStepAsync(runId, stepId)
                       ?? throw new InvalidOperationException($"Agent run {runId} has no step {stepId}.");
            change(run, step);
            return true;
        }, lease);

    // ----- failures ---------------------------------------------------------------------------------------------

    /// <summary>Fails the run with a stable code and a localized message that never contains exception details.</summary>
    private Task FailAsync(Guid runId, string owner, string code, params object?[] args) =>
        UpdateUnderLeaseAsync(runId, owner, run =>
            run.Fail(code, AgenticText.GetOrDefault("Agent.Failure." + code, "Agent.Failure.internal_error", args), Now));

    private async Task HandleFailureAsync(Guid runId, string owner, Exception ex, CancellationToken cancellationToken)
    {
        switch (ex)
        {
            case AgentRunLeaseLostException:
                logger.LogWarning("Agent run {RunId}: the lease was taken over by another worker; this attempt stops", runId);
                return;
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                logger.LogInformation("Agent run {RunId} interrupted by shutdown; it goes back to the queue", runId);
                await ReleaseAsync(runId, owner);
                throw new OperationCanceledException("The server is shutting down.", ex, cancellationToken);
            case ModelProviderException provider:
                // The inner exception may quote a provider response: it goes to the log, never to the run row.
                logger.LogError(ex, "Agent run {RunId}: the model {Model} did not answer", runId, provider.ModelKey);
                await RetryOrFailAsync(runId, owner, "model_unavailable");
                return;
            default:
                logger.LogError(ex, "Agent run {RunId}: the attempt failed", runId);
                await RetryOrFailAsync(runId, owner, "internal_error");
                return;
        }
    }

    private async Task ReleaseAsync(Guid runId, string owner)
    {
        try
        {
            await UpdateUnderLeaseAsync(runId, owner, run =>
            {
                run.ReleaseForShutdown(Now);
                run.MarkEnqueued(Now);
            });
            // Hangfire re-runs the aborted job when the server comes back; RequeueStalledAgentRunsJob is the safety net.
        }
        catch (AgentRunLeaseLostException)
        {
            // already someone else's
        }
    }

    private async Task RetryOrFailAsync(Guid runId, string owner, string code)
    {
        try
        {
            var failures = await UpdateUnderLeaseAsync(runId, owner, (run, _) =>
            {
                if (run.ConsecutiveFailures + 1 >= Settings.MaxAttempts)
                {
                    run.Fail(code, AgenticText.GetOrDefault("Agent.Failure." + code, "Agent.Failure.internal_error"), Now);
                    return 0;
                }

                var failures = run.ReleaseForRetry(Now);
                run.DelayReadiness(Now + RetryDelay(failures));
                return failures;
            });

            if (failures > 0)
                AgentRunQueue.Schedule(jobs, runId, RetryDelay(failures));
        }
        catch (AgentRunLeaseLostException)
        {
            logger.LogWarning("Agent run {RunId}: lease lost while recording a failure", runId);
        }
    }

    /// <summary>15 s, 1 min, 4 min, ... (capped at 15 min).</summary>
    public static TimeSpan RetryDelay(int failures) =>
        TimeSpan.FromSeconds(Math.Min(15 * Math.Pow(4, Math.Max(0, failures - 1)), 900));

    private static string ToCode(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.NotFound => "not_found",
        System.Net.HttpStatusCode.Forbidden => "forbidden",
        System.Net.HttpStatusCode.Conflict => "conflict",
        System.Net.HttpStatusCode.Unauthorized => "unauthorized",
        System.Net.HttpStatusCode.Gone => "gone",
        System.Net.HttpStatusCode.TooManyRequests => "rate_limited",
        _ => "bad_request"
    };
}

/// <summary>Parses and validates the typed result a model handed in (untrusted) against the agent's output type.</summary>
internal static class TypedResult
{
    /// <returns>The result re-serialized from the typed value (so nothing but declared members survives), or an error.</returns>
    public static async Task<(string? Json, string? Error)> ParseAsync(IServiceProvider services, Type outputType,
        string? json, CancellationToken cancellationToken)
    {
        object? value;
        try
        {
            value = JsonSerializer.Deserialize(string.IsNullOrWhiteSpace(json) ? "{}" : json, outputType, AgentJson.Strict);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return (null, ex.Message);
        }

        if (value is null)
            return (null, AgenticText.Get("Agent.Tool.result_not_object"));

        var validatorType = typeof(IValidator<>).MakeGenericType(outputType);
        foreach (var validator in services.GetServices(validatorType).OfType<IValidator>())
        {
            var result = await validator.ValidateAsync(new ValidationContext<object>(value), cancellationToken);
            if (!result.IsValid)
                return (null, string.Join(" ", result.Errors.Select(e => e.ErrorMessage)));
        }

        return (JsonSerializer.Serialize(value, outputType, AgentJson.Output), null);
    }
}
