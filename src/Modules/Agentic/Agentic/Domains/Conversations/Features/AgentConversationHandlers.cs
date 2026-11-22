using System.Linq.Expressions;
using System.Text;
using Agentic.Domains.AgentRuns.Services;
using Microsoft.Extensions.Options;

namespace Agentic.Domains.Conversations.Features;

// ----- validators -----------------------------------------------------------------------------------------------

public class StartAgentConversationCommandValidator : AbstractValidator<StartAgentConversationCommand>
{
    public StartAgentConversationCommandValidator(IStringLocalizer<AgenticResource> localizer, IOptions<AgenticOptions> options)
    {
        RuleFor(x => x.AgentKey)
            .NotEmpty().WithMessage(_ => localizer["Validation.AgentKey.Required"])
            .MaximumLength(AgentRun.AgentKeyMaxLength).WithMessage(_ => localizer["Validation.AgentKey.MaxLength"]);
        RuleFor(x => x.Title)
            .MaximumLength(AgentConversation.TitleMaxLength)
            .WithMessage(_ => localizer["Validation.Conversation.Title.MaxLength", AgentConversation.TitleMaxLength]);
        ConversationMessageRules.Apply(this, x => x.Message, x => x.InputJson, localizer, options.Value.Limits);
    }
}

public class SendAgentConversationMessageCommandValidator : AbstractValidator<SendAgentConversationMessageCommand>
{
    public SendAgentConversationMessageCommandValidator(IStringLocalizer<AgenticResource> localizer,
        IOptions<AgenticOptions> options)
    {
        RuleFor(x => x.ConversationId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
        ConversationMessageRules.Apply(this, x => x.Message, x => x.InputJson, localizer, options.Value.Limits);
    }
}

public class RenameAgentConversationCommandValidator : AbstractValidator<RenameAgentConversationCommand>
{
    public RenameAgentConversationCommandValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.ConversationId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Title)
            .MaximumLength(AgentConversation.TitleMaxLength)
            .WithMessage(_ => localizer["Validation.Conversation.Title.MaxLength", AgentConversation.TitleMaxLength]);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
    }
}

public class DeleteAgentConversationCommandValidator : AbstractValidator<DeleteAgentConversationCommand>
{
    public DeleteAgentConversationCommandValidator(IStringLocalizer<AgenticResource> localizer) =>
        RuleFor(x => x.ConversationId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

public class GetAgentConversationsQueryValidator : AbstractValidator<GetAgentConversationsQuery>
{
    public GetAgentConversationsQueryValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
        RuleFor(x => x.AgentKey).MaximumLength(AgentRun.AgentKeyMaxLength)
            .WithMessage(_ => localizer["Validation.AgentKey.MaxLength"]);
    }
}

/// <summary>A message is the prompt of a run: the same limits apply.</summary>
internal static class ConversationMessageRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Expression<Func<T, string>> message,
        Expression<Func<T, string?>> inputJson, IStringLocalizer<AgenticResource> localizer, AgentRunLimitOptions limits)
    {
        validator.RuleFor(message)
            .NotEmpty().WithMessage(_ => localizer["Validation.Message.Required"])
            .MaximumLength(limits.MaxPromptLength).WithMessage(_ => localizer["Validation.Message.MaxLength", limits.MaxPromptLength]);
        var input = inputJson.Compile();
        validator.RuleFor(inputJson)
            .Must(json => Encoding.UTF8.GetByteCount(json!) <= limits.MaxInputBytes)
            .WithMessage(_ => localizer["Validation.Input.TooLarge", limits.MaxInputBytes])
            .Must(AgentJson.IsJsonObject)
            .WithMessage(_ => localizer["Validation.Input.NotJsonObject"])
            .When(x => !string.IsNullOrWhiteSpace(input(x)));
    }
}

// ----- start / send ---------------------------------------------------------------------------------------------

/// <summary>
/// Starts a conversation and its first turn. Who may start is the same as for a run of the agent (its permission, its
/// allowed triggers); the agent must be conversational. The conversation and the run are saved together.
/// </summary>
public class StartAgentConversationHandler(
    AgentRegistry registry,
    AgentAccess access,
    AgentRunStarter starter,
    IAgentConversationRepository conversations,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options) : ICommandHandler<StartAgentConversationCommand, AgentConversationTurnDto>
{
    public async Task<AgentConversationTurnDto> Handle(StartAgentConversationCommand command, CancellationToken cancellationToken)
    {
        var (agent, trigger) = await ConversationTurns.AuthorizeAsync(registry, access, command.AgentKey);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var conversation = AgentConversation.Start(agent.Definition.Key, access.UserId, access.ApiKeyId, command.Title, now);
        conversation.RecordMessage(now, options.Value.Conversations.MaxMessages);
        conversations.Add(conversation);

        // The starter saves the run; the conversation is in the same unit of work (same DbContext), so both are
        // committed by that one save or neither is.
        var run = await starter.StartAsync(ConversationTurns.Start(agent, trigger, access, conversation, command.Message,
            command.InputJson, startTainted: false), cancellationToken);
        return new AgentConversationTurnDto(conversation.Id, conversation.Version, AgentRunProjections.Map(run));
    }
}

/// <summary>
/// Continues the caller's conversation with a new turn. One turn at a time: refused with 409 <c>conversation_busy</c>
/// while the previous one is queued, running or waiting for an approval (checked here, and guaranteed by the filtered
/// unique index for two messages sent at the same moment). The new turn starts tainted when an earlier one read
/// untrusted content.
/// </summary>
public class SendAgentConversationMessageHandler(
    AgentRegistry registry,
    AgentAccess access,
    AgentRunStarter starter,
    IAgentConversationRepository conversations,
    AgenticDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options) : ICommandHandler<SendAgentConversationMessageCommand, AgentConversationTurnDto>
{
    public async Task<AgentConversationTurnDto> Handle(SendAgentConversationMessageCommand command,
        CancellationToken cancellationToken)
    {
        var conversation = await ConversationTurns.OwnActiveAsync(conversations, access, command.ConversationId, cancellationToken);
        dbContext.ExpectVersion(conversation, command.Version);

        // Re-checked for every turn: the agent may have been removed or stopped being conversational, and the caller
        // may have lost the permission it needs since the conversation started.
        var (agent, trigger) = await ConversationTurns.AuthorizeAsync(registry, access, conversation.AgentKey);

        if (await conversations.HasActiveTurnAsync(conversation.Id, cancellationToken))
            throw new AgentConversationBusyException();
        var tainted = await conversations.IsTaintedAsync(conversation.Id, cancellationToken);

        conversation.RecordMessage(timeProvider.GetUtcNow().UtcDateTime, options.Value.Conversations.MaxMessages);

        AgentRun run;
        try
        {
            run = await starter.StartAsync(ConversationTurns.Start(agent, trigger, access, conversation, command.Message,
                command.InputJson, tainted), cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.IsUniqueViolation(Data.Configurations.AgentRunConfiguration.ActiveTurnIndexName) ||
            ex.IsUniqueViolation(Data.Configurations.AgentRunConfiguration.ConversationTurnIndexName))
        {
            // Another message of the same conversation started its turn between the check above and this save.
            throw new AgentConversationBusyException();
        }
        catch (DbUpdateConcurrencyException) when (command.Version is null)
        {
            // The conversation row changed meanwhile. Only a turn that started is "busy"; anything else (a rename) is
            // an ordinary conflict for ConcurrencyHandlingBehavior.
            if (await conversations.HasActiveTurnAsync(conversation.Id, cancellationToken))
                throw new AgentConversationBusyException();
            throw;
        }

        return new AgentConversationTurnDto(conversation.Id, conversation.Version, AgentRunProjections.Map(run));
    }
}

internal static class ConversationTurns
{
    /// <summary>The agent exists for the caller, holds conversations, and may be started by this kind of caller.</summary>
    public static async Task<(RegisteredAgent Agent, AgentTriggerKind Trigger)> AuthorizeAsync(AgentRegistry registry,
        AgentAccess access, string agentKey)
    {
        var agent = registry.GetAgent(agentKey);
        var definition = agent.Definition;
        // An agent the caller may not start does not exist for them (no probing of agent keys).
        if (!await access.CanStartAsync(definition))
            throw new AgentNotFoundException(agentKey);
        if (!definition.Conversational)
            throw new AgentNotConversationalException(definition.Key);

        var trigger = access.IsApiKey ? AgentTriggerKind.Api : AgentTriggerKind.User;
        if (!definition.AllowedTriggers.Contains(trigger))
            throw new AgentAccessDeniedException("trigger_not_allowed", definition.Key, trigger);
        return (agent, trigger);
    }

    public static AgentRunStart Start(RegisteredAgent agent, AgentTriggerKind trigger, AgentAccess access,
        AgentConversation conversation, string message, string? inputJson, bool startTainted) =>
        new(agent, trigger, access.UserId, message, inputJson, AgenticText.CurrentCulture, access.PermissionCeiling,
            access.ApiKeyId, ConversationId: conversation.Id, ConversationTurn: conversation.MessageCount,
            StartTainted: startTainted);

    /// <summary>The caller's own, not deleted conversation (tracked); anything else does not exist for them.</summary>
    public static async Task<AgentConversation> OwnActiveAsync(IAgentConversationRepository conversations,
        AgentAccess access, Guid id, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetByIdAsync(id, cancellationToken);
        if (conversation is null || conversation.Status != AgentConversationStatus.Active || !access.Owns(conversation))
            throw new AgentConversationNotFoundException(id);
        return conversation;
    }
}

// ----- rename / delete ------------------------------------------------------------------------------------------

public class RenameAgentConversationHandler(
    IAgentConversationRepository conversations,
    AgenticDbContext dbContext,
    AgentAccess access) : ICommandHandler<RenameAgentConversationCommand, AgentConversationDto>
{
    public async Task<AgentConversationDto> Handle(RenameAgentConversationCommand command, CancellationToken cancellationToken)
    {
        var conversation = await ConversationTurns.OwnActiveAsync(conversations, access, command.ConversationId, cancellationToken);
        dbContext.ExpectVersion(conversation, command.Version);

        conversation.Rename(command.Title);
        await conversations.SaveChangesAsync(cancellationToken);

        var busy = await conversations.HasActiveTurnAsync(conversation.Id, cancellationToken);
        return AgentConversationProjections.Map(conversation, busy);
    }
}

/// <summary>
/// Deletes the caller's conversation. A turn in progress is asked to stop; the content of every finished turn is
/// erased now, and a turn that finishes later is erased when it does
/// (<c>EraseDeletedConversationTurnEventHandler</c>, and the daily <c>PurgeAgentContentJob</c> as a backstop).
/// </summary>
public class DeleteAgentConversationHandler(
    IAgentConversationRepository conversations,
    AgenticDbContext dbContext,
    AgentAccess access,
    TimeProvider timeProvider,
    ILogger<DeleteAgentConversationHandler> logger) : ICommandHandler<DeleteAgentConversationCommand, bool>
{
    private const int Attempts = 3;

    public async Task<bool> Handle(DeleteAgentConversationCommand command, CancellationToken cancellationToken)
    {
        var conversation = await ConversationTurns.OwnActiveAsync(conversations, access, command.ConversationId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        int stopping;
        for (var attempt = 1; ; attempt++)
        {
            conversation.Delete(now);
            var active = await conversations.GetActiveTurnsAsync(conversation.Id, cancellationToken);
            foreach (var run in active)
                run.RequestCancel(now);
            try
            {
                await conversations.SaveChangesAsync(cancellationToken);
                stopping = active.Count(r => !r.IsTerminal);
                break;
            }
            catch (DbUpdateConcurrencyException) when (attempt < Attempts)
            {
                // A message, a rename or a turn's worker saved meanwhile: read everything again and decide again (a
                // turn started in between is found by the next query and stopped too).
                foreach (var entry in dbContext.ChangeTracker.Entries<AgentRun>().ToList())
                {
                    entry.Entity.ClearDomainEvents();
                    await entry.ReloadAsync(cancellationToken);
                }

                await dbContext.Entry(conversation).ReloadAsync(cancellationToken);
            }
        }

        var runIds = await conversations.GetRunIdsAsync(conversation.Id, cancellationToken);
        var erased = await AgentRunContentEraser.EraseAsync(dbContext, runIds, now, cancellationToken);
        logger.LogInformation("Agent conversation {ConversationId} deleted by {UserId}: {Erased} turns erased, {Stopping} stopping",
            conversation.Id, access.UserId, erased, stopping);
        return true;
    }
}

// ----- queries --------------------------------------------------------------------------------------------------

public class GetAgentConversationsHandler(AgenticDbContext dbContext, AgentAccess access)
    : IQueryHandler<GetAgentConversationsQuery, PaginatedResult<AgentConversationDto>>
{
    public async Task<PaginatedResult<AgentConversationDto>> Handle(GetAgentConversationsQuery query,
        CancellationToken cancellationToken)
    {
        var conversations = dbContext.Conversations.AsNoTracking()
            .Where(access.OwnConversations())
            .Where(c => c.Status == AgentConversationStatus.Active);
        if (!string.IsNullOrWhiteSpace(query.AgentKey))
            conversations = conversations.Where(c => c.AgentKey == query.AgentKey);

        return await conversations
            .OrderByDescending(c => c.LastMessageAt).ThenByDescending(c => c.Id)
            .Select(AgentConversationProjections.ToDto(dbContext.Runs))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}

/// <summary>
/// A conversation with its turns, oldest first: the owner's, or any conversation for holders of
/// <c>agentic.runs.read</c>. The number of turns is bounded by <c>Agentic:Conversations:MaxMessages</c> (at least 1).
/// </summary>
public class GetAgentConversationByIdHandler(AgenticDbContext dbContext, AgentAccess access)
    : IQueryHandler<GetAgentConversationByIdQuery, AgentConversationDetailDto>
{
    public async Task<AgentConversationDetailDto> Handle(GetAgentConversationByIdQuery query, CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations.AsNoTracking()
                               .Where(await access.ReadableConversationsAsync())
                               .Where(c => c.Id == query.ConversationId && c.Status == AgentConversationStatus.Active)
                               .Select(AgentConversationProjections.ToDto(dbContext.Runs))
                               .FirstOrDefaultAsync(cancellationToken)
                           ?? throw new AgentConversationNotFoundException(query.ConversationId);

        var messages = await dbContext.Runs.AsNoTracking()
            .Where(r => r.ConversationId == conversation.Id)
            .OrderBy(r => r.ConversationTurn)
            .Select(r => new AgentConversationMessageDto(r.Id, r.Prompt, r.InputJson, r.OutputText, r.OutputJson, r.Status,
                r.FailureCode, r.InputTokens, r.OutputTokens, r.CreatedAt ?? DateTime.MinValue, r.FinishedAt))
            .ToListAsync(cancellationToken);
        var summary = await dbContext.Conversations.AsNoTracking()
            .Where(c => c.Id == conversation.Id)
            .Select(c => new { c.Summary, c.SummaryThroughTurn })
            .FirstAsync(cancellationToken);
        return new AgentConversationDetailDto(conversation, messages, summary.Summary,
            summary.Summary is null ? 0 : summary.SummaryThroughTurn);
    }
}

/// <summary>SQL projections of conversations.</summary>
public static class AgentConversationProjections
{
    private static readonly AgentRunStatus[] ActiveStatuses =
        [AgentRunStatus.Queued, AgentRunStatus.Running, AgentRunStatus.AwaitingApproval];

    /// <summary>The DTO, with <c>Busy</c> computed in the same query.</summary>
    public static Expression<Func<AgentConversation, AgentConversationDto>> ToDto(IQueryable<AgentRun> runs) => c =>
        new AgentConversationDto(c.Id, c.AgentKey, c.Title, c.Status, c.MessageCount,
            runs.Any(r => r.ConversationId == c.Id && ActiveStatuses.Contains(r.Status)),
            c.CreatedAt ?? DateTime.MinValue, c.LastMessageAt, c.Version);

    public static AgentConversationDto Map(AgentConversation c, bool busy) =>
        new(c.Id, c.AgentKey, c.Title, c.Status, c.MessageCount, busy, c.CreatedAt ?? DateTime.MinValue, c.LastMessageAt,
            c.Version);
}
