using Attachment.Contracts.Attachments.IntegrationEvents;
using Attachment.Contracts.Attachments.Queries;
using Identity.Domains.Users.Consumers;
using Identity.Domains.Users.Models;
using MassTransit;
using Shared.Contracts.Mediation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace Identity.Tests.Users;

/// <summary>
/// The consumer treats the event as a trigger: it re-reads the CURRENT avatar and mirrors its URL into
/// <c>ProfilePictureUrl</c>. Idempotent, order independent, and retryable on failure.
/// </summary>
public sealed class UserAvatarChangedConsumerTests
{
    private static readonly DateTimeOffset Now = new(2030, 5, 6, 7, 8, 9, TimeSpan.Zero);

    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<ISender> _sender = new();
    private readonly FakeTimeProvider _clock = new(Now);

    private UserAvatarChangedConsumer Consumer() =>
        new(_users.Object, _sender.Object, _clock, NullLogger<UserAvatarChangedConsumer>.Instance);

    private static ApplicationUser User(string? pictureUrl = null) =>
        new() { Id = Guid.NewGuid(), UserName = "u", Email = "u@example.com", ProfilePictureUrl = pictureUrl };

    private void UserExists(ApplicationUser user) =>
        _users.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);

    private void CurrentAvatarIs(Guid userId, Guid? attachmentId, string? url) =>
        _sender.Setup(s => s.Send(It.Is<GetUserAvatarQuery>(q => q.UserId == userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetUserAvatarResult(attachmentId, url));

    private static ConsumeContext<UserAvatarChangedIntegrationEvent> Message(Guid userId)
    {
        var context = new Mock<ConsumeContext<UserAvatarChangedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(new UserAvatarChangedIntegrationEvent
            { UserId = userId, AttachmentId = Guid.NewGuid() });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    [Fact]
    public async Task Sets_the_profile_picture_url_from_the_current_avatar_not_from_the_event()
    {
        var user = User(pictureUrl: "/api/v1/attachments/old/download");
        UserExists(user);
        var current = Guid.NewGuid();
        CurrentAvatarIs(user.Id, current, $"/api/v1/attachments/{current}/download");

        await Consumer().Consume(Message(user.Id));

        user.ProfilePictureUrl.Should().Be($"/api/v1/attachments/{current}/download");
        user.UpdatedAt.Should().Be(Now.UtcDateTime);
        _users.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task Clears_the_url_when_the_user_has_no_avatar_any_more()
    {
        var user = User(pictureUrl: "/api/v1/attachments/gone/download");
        UserExists(user);
        CurrentAvatarIs(user.Id, null, null);

        await Consumer().Consume(Message(user.Id));

        user.ProfilePictureUrl.Should().BeNull();
        _users.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task Does_not_update_when_the_url_is_already_current()
    {
        var current = Guid.NewGuid();
        var user = User(pictureUrl: $"/api/v1/attachments/{current}/download");
        UserExists(user);
        CurrentAvatarIs(user.Id, current, $"/api/v1/attachments/{current}/download");

        await Consumer().Consume(Message(user.Id));

        _users.Verify(m => m.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        user.UpdatedAt.Should().BeNull("a duplicate delivery must not touch the user");
    }

    [Fact]
    public async Task Duplicate_delivery_converges_to_one_update()
    {
        var user = User();
        UserExists(user);
        var current = Guid.NewGuid();
        CurrentAvatarIs(user.Id, current, $"/api/v1/attachments/{current}/download");

        await Consumer().Consume(Message(user.Id));
        await Consumer().Consume(Message(user.Id));

        _users.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task Unknown_user_is_acknowledged_without_throwing_or_querying()
    {
        var userId = Guid.NewGuid();
        _users.Setup(m => m.FindByIdAsync(userId.ToString())).ReturnsAsync((ApplicationUser?)null);

        var act = () => Consumer().Consume(Message(userId));

        await act.Should().NotThrowAsync("there is nothing to retry for a user that does not exist");
        _sender.Verify(s => s.Send(It.IsAny<GetUserAvatarQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(m => m.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task Failed_update_throws_so_the_bus_retries()
    {
        var user = User();
        UserExists(user);
        CurrentAvatarIs(user.Id, Guid.NewGuid(), "/api/v1/attachments/x/download");
        _users.Setup(m => m.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure" }));

        var act = () => Consumer().Consume(Message(user.Id));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain(user.Id.ToString()).And.Contain("ConcurrencyFailure");
    }
}
