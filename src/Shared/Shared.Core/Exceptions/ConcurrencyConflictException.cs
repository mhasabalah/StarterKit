namespace Shared.Core.Exceptions;

/// <summary>
/// The domain form of an optimistic-concurrency conflict (HTTP 409).
/// <para>
/// Translation rule (exactly one place): DbContexts throw EF's <see cref="DbUpdateConcurrencyException"/> unchanged;
/// <c>ConcurrencyHandlingBehavior</c> (mediator) wraps it into this exception, and <c>ExceptionStatusMapper</c> maps
/// both to 409. Background code that may see either form should use <see cref="ConcurrencyExceptionExtensions.IsConcurrencyConflict"/>.
/// </para>
/// </summary>
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

public static class ConcurrencyExceptionExtensions
{
    /// <summary>
    /// True for an optimistic-concurrency conflict in either form: EF's <see cref="DbUpdateConcurrencyException"/>
    /// (direct DbContext/repository save) or <see cref="ConcurrencyConflictException"/> (through the mediator).
    /// Use it in jobs to log-and-skip one item: <c>catch (Exception ex) when (ex.IsConcurrencyConflict())</c>.
    /// </summary>
    public static bool IsConcurrencyConflict(this Exception exception) =>
        exception is DbUpdateConcurrencyException or ConcurrencyConflictException;
}
