using FluentAssertions;
using Identity.Contracts.Users.Dtos;
using Identity.Contracts.Users.Queries;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Subscriptions.Infrastructure.Subscribers;
using Xunit;

namespace Subscriptions.Tests;

public class IdentityUserSubscriberInfoProviderTests
{
    [Fact]
    public async Task GetAsync_maps_identity_user_to_subscriber_info()
    {
        var userId = Guid.NewGuid();
        var sender = new StubSender(_ => new UserDto
        {
            Id = userId,
            UserName = "jane",
            Email = "jane@example.com",
            PhoneNumber = "+1 555 0100",
            FullName = "Jane Doe"
        });
        var provider = new IdentityUserSubscriberInfoProvider(sender, NullLogger<IdentityUserSubscriberInfoProvider>.Instance);

        var info = await provider.GetAsync(userId);

        info.Should().NotBeNull();
        info!.SubscriberId.Should().Be(userId);
        info.DisplayName.Should().Be("Jane Doe");
        info.BillingName.Should().Be("Jane Doe");
        info.Email.Should().Be("jane@example.com");
        info.Phone.Should().Be("+1 555 0100");
        sender.LastRequest.Should().BeOfType<GetUserByIdQuery>()
            .Which.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task GetAsync_falls_back_to_username_when_full_name_is_blank()
    {
        var sender = new StubSender(_ => new UserDto { Id = Guid.NewGuid(), UserName = "jane", Email = "j@example.com", FullName = " " });
        var provider = new IdentityUserSubscriberInfoProvider(sender, NullLogger<IdentityUserSubscriberInfoProvider>.Instance);

        var info = await provider.GetAsync(Guid.NewGuid());

        info!.DisplayName.Should().Be("jane");
    }

    [Fact]
    public async Task GetAsync_returns_null_when_user_cannot_be_resolved()
    {
        var sender = new StubSender(_ => throw new InvalidOperationException("user not found"));
        var provider = new IdentityUserSubscriberInfoProvider(sender, NullLogger<IdentityUserSubscriberInfoProvider>.Instance);

        var info = await provider.GetAsync(Guid.NewGuid());

        info.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_propagates_cancellation()
    {
        var sender = new StubSender(_ => throw new OperationCanceledException());
        var provider = new IdentityUserSubscriberInfoProvider(sender, NullLogger<IdentityUserSubscriberInfoProvider>.Instance);

        var act = () => provider.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class StubSender(Func<object, object?> respond) : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)respond(request)!);
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
