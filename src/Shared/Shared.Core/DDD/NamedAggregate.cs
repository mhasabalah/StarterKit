namespace Shared.Core.DDD;

public abstract class NamedAggregate<TId> : Aggregate<TId>, INamedAggregate<TId>
{
    public string ArabicName { get; set; } = default!;
    public string EnglishName { get; set; } = default!;
}

