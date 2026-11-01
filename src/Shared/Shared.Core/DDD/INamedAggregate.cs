namespace Shared.Core.DDD;

public interface INamedAggregate<T> : IAggregate<T>, INamedAggregate
{
}

public interface INamedAggregate : IAggregate, INamedEntity
{
}

