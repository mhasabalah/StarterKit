namespace Shared.Core.Exceptions;

public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("The resource was modified by another request. Reload and try again.")
    {
    }

    public ConcurrencyConflictException(Exception innerException)
        : base("The resource was modified by another request. Reload and try again.", innerException)
    {
    }
}
