using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;
using UserDirectory.Contracts.Queries;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Subscriptions.Tests;

public class AdminNotificationRecipientsTests
{
    [Fact]
    public async Task Configured_emails_win_and_the_directory_is_not_queried()
    {
        var sender = new RoleSender(_ => throw new InvalidOperationException("must not be called"));
        var sut = Create(sender, new AdminNotificationOptions { Emails = [" Ops@Example.com ", "ops@example.com", "bad"] });

        var emails = await sut.GetEmailsAsync();

        emails.Should().Equal("Ops@Example.com");
        sender.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task Falls_back_to_active_users_of_each_role_deduplicated()
    {
        var sender = new RoleSender(role => role switch
        {
            "Admin" => [User("a@example.com"), User("shared@example.com")],
            "Finance" => [User("SHARED@example.com"), User(null), User("f@example.com")],
            _ => []
        });
        var sut = Create(sender, new AdminNotificationOptions { Roles = ["Admin", "Finance"] });

        var emails = await sut.GetEmailsAsync();

        emails.Should().Equal("a@example.com", "shared@example.com", "f@example.com");
        sender.Roles.Should().Equal("Admin", "Finance");
    }

    [Fact]
    public async Task A_failing_role_lookup_does_not_lose_the_other_roles()
    {
        var sender = new RoleSender(role => role == "Admin"
            ? throw new InvalidOperationException("directory down")
            : [User("f@example.com")]);
        var sut = Create(sender, new AdminNotificationOptions { Roles = ["Admin", "Finance"] });

        var emails = await sut.GetEmailsAsync();

        emails.Should().Equal("f@example.com");
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var sut = Create(new RoleSender(_ => throw new OperationCanceledException()),
            new AdminNotificationOptions { Roles = ["Admin"] });

        var act = () => sut.GetEmailsAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static UserSummaryDto User(string? email) => new(Guid.NewGuid(), email ?? "x", email, null, true);

    private static AdminNotificationRecipients Create(ISender sender, AdminNotificationOptions admin) =>
        new(MsOptions.Create(new SubscriptionOptions { AdminNotifications = admin }), sender,
            NullLogger<AdminNotificationRecipients>.Instance);

    private sealed class RoleSender(Func<string, IReadOnlyList<UserSummaryDto>> respond) : ISender
    {
        public List<string> Roles { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var query = (GetUsersInRoleQuery)(object)request;
            Roles.Add(query.Role);
            return Task.FromResult((TResponse)(object)respond(query.Role));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
