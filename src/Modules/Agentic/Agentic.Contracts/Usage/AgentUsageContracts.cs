namespace Agentic.Contracts.Usage;

/// <summary>
/// Counters of a set of runs, plus the model calls made for them outside a run (conversation summaries). Tokens and
/// cost include those calls, so they add up to what the plan's <c>agent_tokens</c> paid for; <see cref="Summaries"/>,
/// <see cref="SummaryTokens"/> and <see cref="SummaryCost"/> are their share. Costs are estimates from the configured
/// model prices.
/// </summary>
public sealed record AgentUsageTotalsDto(
    int Runs,
    int Completed,
    int Failed,
    int Cancelled,
    int Active,
    long ModelTurns,
    long ToolCalls,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCost,
    int Summaries = 0,
    long SummaryTokens = 0,
    decimal SummaryCost = 0);

public sealed record AgentUsageByAgentDto(string AgentKey, AgentUsageTotalsDto Totals);

public sealed record AgentUsageDto(
    DateTime From,
    DateTime To,
    AgentUsageTotalsDto Totals,
    IReadOnlyList<AgentUsageByAgentDto> ByAgent);

/// <summary>
/// The tenant's agent usage for runs created, and conversation summaries written, in [<paramref name="From"/>,
/// <paramref name="To"/>) (UTC). Defaults to the current calendar month. Read from the run rows and the summary usage
/// rows, which are the record of what was used.
/// </summary>
public sealed record GetAgentUsageQuery(DateTime? From = null, DateTime? To = null) : IQuery<AgentUsageDto>;
