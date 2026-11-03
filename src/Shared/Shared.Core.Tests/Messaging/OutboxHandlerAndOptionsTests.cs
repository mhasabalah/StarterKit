using Microsoft.Extensions.Logging;
using Moq;
using Shared.Core.Data;
using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy;
using Shared.Core.Tests.Infrastructure;

namespace Shared.Core.Tests.Messaging;

public sealed class OutboxMessagesSavedHandlerTests
{
    private static OutboxMessagesSaved Notification(params Guid[] ids) =>
        new(typeof(OutboxHostDbContext), ids, null, false);

    private static (OutboxMessagesSavedHandler Handler, CapturingLoggerProvider Logs) Create(IUnitOfWorkRunner runner)
    {
        var logs = new CapturingLoggerProvider();
        var factory = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        return (new OutboxMessagesSavedHandler(runner, factory.CreateLogger<OutboxMessagesSavedHandler>()), logs);
    }

    [Fact]
    public async Task A_failing_delivery_is_swallowed_and_logged_because_the_sweep_retries()
    {
        var runner = new Mock<IUnitOfWorkRunner>();
        runner.Setup(r => r.RunAsync<int>(It.IsAny<Func<IServiceProvider, CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("relay exploded"));
        var (handler, logs) = Create(runner.Object);

        var act = () => handler.Handle(Notification(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await act.Should().NotThrowAsync();
        var warning = logs.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("sweep");
        runner.Verify(r => r.RunAsync<int>(It.IsAny<Func<IServiceProvider, CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var runner = new Mock<IUnitOfWorkRunner>();
        runner.Setup(r => r.RunAsync<int>(It.IsAny<Func<IServiceProvider, CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var (handler, _) = Create(runner.Object);

        var act = () => handler.Handle(Notification(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Notification_without_ids_does_nothing()
    {
        var runner = new Mock<IUnitOfWorkRunner>(MockBehavior.Strict);
        var (handler, _) = Create(runner.Object);

        await handler.Handle(Notification(), CancellationToken.None);

        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Delivery_runs_inside_the_tenant_scope_of_the_save()
    {
        Guid? seen = null;
        var runner = new Mock<IUnitOfWorkRunner>();
        runner.Setup(r => r.RunAsync<int>(It.IsAny<Func<IServiceProvider, CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Callback(() => seen = TenantScope.Current?.Tenant?.Id)
            .Returns(Task.FromResult(0));
        var (handler, _) = Create(runner.Object);
        var tenant = OutboxFixture.TenantA;

        await handler.Handle(new OutboxMessagesSaved(typeof(OutboxTenantDbContext), [Guid.NewGuid()], tenant, false),
            CancellationToken.None);

        seen.Should().Be(tenant.Id);
        TenantScope.Current?.Tenant?.Id.Should().NotBe(tenant.Id, "the scope ended with the handler");
    }
}

public sealed class OutboxOptionsTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    [InlineData(10, 2560)]
    [InlineData(11, 3600)]
    [InlineData(20, 3600)]
    [InlineData(100, 3600)]
    public void Back_off_doubles_from_five_seconds_and_is_capped_at_one_hour(int attempt, int expectedSeconds) =>
        OutboxOptions.BackoffFor(attempt).Should().Be(TimeSpan.FromSeconds(expectedSeconds));

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Non_positive_attempts_are_treated_as_the_first(int attempt) =>
        OutboxOptions.BackoffFor(attempt).Should().Be(TimeSpan.FromSeconds(5));

    [Fact]
    public void Defaults_are_sane()
    {
        var options = new OutboxOptions();
        options.BatchSize.Should().Be(100);
        options.MaxAttempts.Should().Be(10);
        options.LeaseSeconds.Should().Be(60);
        options.RetentionDays.Should().Be(7);
        options.SweepCron.Should().Be("* * * * *");
    }
}
