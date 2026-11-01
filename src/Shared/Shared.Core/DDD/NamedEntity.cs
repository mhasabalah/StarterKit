namespace Shared.Core.DDD;

public abstract class NamedEntity<T> : Entity<T>, INamedEntity<T>
{
    public string ArabicName { get; set; } = default!;
    public string EnglishName { get; set; } = default!;
}

