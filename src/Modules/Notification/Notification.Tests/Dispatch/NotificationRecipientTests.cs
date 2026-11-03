using Moq;

namespace Notification.Tests.Dispatch;

/// <summary>Notifications addressed to a non-user recipient (a tenant subscriber) reach the resolved user.</summary>
public sealed class NotificationRecipientTests
{
    private readonly Mock<INotificationJobEnqueuer> _enqueuer = new();
    private NotificationMessage? _sent;

    public NotificationRecipientTests() =>
        _enqueuer.Setup(e => e.EnqueueAsync(It.IsAny<NotificationMessage>(), It.IsAny<NotificationChannel?>(),
                It.IsAny<CancellationToken>()))
            .Callback((NotificationMessage m, NotificationChannel? _, CancellationToken _) => _sent = m)
            .ReturnsAsync(Guid.NewGuid());

    [Fact]
    public async Task Tenant_subscriber_is_delivered_to_the_resolved_owner()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var resolver = new Mock<INotificationRecipientResolver>();
        resolver.Setup(r => r.ResolveUserIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(ownerId);

        await new NotificationSender(_enqueuer.Object, resolver.Object)
            .For(tenantId).WithEmail(EmailTemplate.WelcomeUser).SendAsync();

        _sent!.UserId.Should().Be(ownerId);
    }

    [Fact]
    public async Task User_ids_are_kept()
    {
        var userId = Guid.NewGuid();
        var resolver = new Mock<INotificationRecipientResolver>();

        await new NotificationSender(_enqueuer.Object, resolver.Object)
            .For(userId).WithEmail(EmailTemplate.WelcomeUser).SendAsync();

        _sent!.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task Without_a_resolver_the_id_is_the_user_id()
    {
        var userId = Guid.NewGuid();
        await new NotificationSender(_enqueuer.Object).For(userId).WithEmail(EmailTemplate.WelcomeUser).SendAsync();
        _sent!.UserId.Should().Be(userId);
    }
}
