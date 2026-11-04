using Shared.Contracts.Mediation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Behaviors;
using Shared.Core.Exceptions;

namespace Shared.Core.Tests.Concurrency;

/// <summary>
/// The mediator pipeline turns EF's <see cref="DbUpdateConcurrencyException"/> into the domain
/// <see cref="ConcurrencyConflictException"/>, which <c>GlobalExceptionHandler</c> maps to HTTP 409.
/// </summary>
public sealed class ConcurrencyHandlingBehaviorTests
{
    private sealed record TestCommand : IRequest<string>;

    private readonly ConcurrencyHandlingBehavior<TestCommand, string> _behavior =
        new(NullLogger<ConcurrencyHandlingBehavior<TestCommand, string>>.Instance);

    [Fact]
    public async Task Maps_DbUpdateConcurrencyException_to_ConcurrencyConflictException()
    {
        var efException = new DbUpdateConcurrencyException("row changed");

        var act = () => _behavior.Handle(new TestCommand(), _ => throw efException, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ConcurrencyConflictException>();
        thrown.Which.InnerException.Should().BeSameAs(efException);
    }

    [Fact]
    public async Task Leaves_an_already_translated_conflict_untouched()
    {
        // Nested ISender.Send: the inner behavior already translated it; the outer one must not wrap it again.
        var inner = new ConcurrencyConflictException(new DbUpdateConcurrencyException("row changed"));

        var act = () => _behavior.Handle(new TestCommand(), _ => throw inner, CancellationToken.None);

        (await act.Should().ThrowAsync<ConcurrencyConflictException>()).Which.Should().BeSameAs(inner);
    }

    [Theory]
    [InlineData("ef", true)]
    [InlineData("domain", true)]
    [InlineData("conflict", false)]
    [InlineData("other", false)]
    public void IsConcurrencyConflict_recognizes_both_forms(string kind, bool expected)
    {
        Exception exception = kind switch
        {
            "ef" => new DbUpdateConcurrencyException("row changed"),
            "domain" => new ConcurrencyConflictException(),
            "conflict" => new ConflictException("duplicate"),
            _ => new InvalidOperationException()
        };

        exception.IsConcurrencyConflict().Should().Be(expected);
    }

    [Fact]
    public async Task Passes_through_successful_responses()
    {
        var result = await _behavior.Handle(new TestCommand(), _ => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Does_not_swallow_other_exceptions()
    {
        var act = () => _behavior.Handle(new TestCommand(),
            _ => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
