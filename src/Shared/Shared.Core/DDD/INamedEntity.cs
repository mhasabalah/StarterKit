namespace Shared.Core.DDD;

public interface INamedEntity<T> : IEntity<T>, INamedEntity
{
}

public interface INamedEntity : IEntity
{
    string ArabicName { get; set; }
    string EnglishName { get; set; }
}

