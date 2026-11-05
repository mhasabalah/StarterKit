namespace Shared.Contracts.CQRS;

// No `notnull` constraint: "not found" lookups legitimately return a nullable DTO (the endpoint maps null to 404).
public interface IQuery<out T> : IRequest<T>
{
}
