using FluentAssertions;
using Shared.Contracts.Mediation;
using Microsoft.Extensions.Logging.Abstractions;
using Subscriptions.Infrastructure.Subscribers;
using UserDirectory.Contracts.Queries;
using Xunit;

namespace Subscriptions.Tests;

public class UserDirectorySubscriberInfoProviderTests
{
    [Fact]
    public async Task GetAsync_maps_user_profile_to_subscriber_info()
    {
        var userId = Guid.NewGuid();
        var sender = new StubSender(_ => new UserProfileDto(userId, "Jane Doe", "jane@example.com", "+1 555 0100", true));
        var provider = Create(sender);

        var info = await provider.GetAsync(userId);

        info.Should().NotBeNull();
        info!.SubscriberId.Should().Be(userId);
        info.DisplayName.Should().Be("Jane Doe");
        info.BillingName.Should().Be("Jane Doe");
        info.Email.Should().Be("jane@example.com");
        info.Phone.Should().Be("+1 555 0100");
        sender.LastRequest.Should().BeOfType<GetUserProfileQuery>()
            .Which.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_user_is_unknown()
    {
        var provider = Create(new StubSender(_ => null));

        var info = await provider.GetAsync(Guid.NewGuid());

        info.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_returns_null_when_directory_fails()
    {
        var provider = Create(new StubSender(_ => throw new InvalidOperationException("directory down")));

        var info = await provider.GetAsync(Guid.NewGuid());

        info.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_propagates_cancellation()
    {
        var provider = Create(new StubSender(_ => throw new OperationCanceledException()));

        var act = () => provider.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static UserDirectorySubscriberInfoProvider Create(ISender sender) =>
        new(sender, NullLogger<UserDirectorySubscriberInfoProvider>.Instance);

    private sealed class StubSender(Func<object, object?> respond) : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)respond(request)!);
        }
    }
}
