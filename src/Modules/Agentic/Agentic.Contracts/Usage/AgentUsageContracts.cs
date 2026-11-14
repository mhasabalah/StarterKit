namespace Agentic.Contracts.Usage;

/// <summary>Counters of a set of runs. Costs are estimates from the configured model prices.</summary>
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
    decimal EstimatedCost);

public sealed record AgentUsageByAgentDto(string AgentKey, AgentUsageTotalsDto Totals);

public sealed record AgentUsageDto(
    DateTime From,
    DateTime To,
    AgentUsageTotalsDto Totals,
    IReadOnlyList<AgentUsageByAgentDto> ByAgent);

/// <summary>
/// The tenant's agent usage for runs created in [<paramref name="From"/>, <paramref name="To"/>) (UTC). Defaults to
/// the current calendar month. Read from the run rows, which are the record of what was used.
/// </summary>
public sealed record GetAgentUsageQuery(DateTime? From = null, DateTime? To = null) : IQuery<AgentUsageDto>;
