using Moq;

namespace Notification.Tests.Dispatch;

/// <summary><see cref="INotificationSender.ToEmail"/>: explicit-address notifications (invitations, password reset).</summary>
public sealed class NotificationSenderTests
{
    private readonly Mock<INotificationJobEnqueuer> _enqueuer = new();
    private NotificationMessage? _sent;
    private NotificationChannel? _forced;

    public NotificationSenderTests() =>
        _enqueuer.Setup(e => e.EnqueueAsync(It.IsAny<NotificationMessage>(), It.IsAny<NotificationChannel?>(),
                It.IsAny<CancellationToken>()))
            .Callback((NotificationMessage m, NotificationChannel? c, CancellationToken _) => (_sent, _forced) = (m, c))
            .ReturnsAsync(Guid.NewGuid());

    [Fact]
    public async Task A_pinned_correlation_id_travels_on_the_message_and_defaults_to_none()
    {
        var pinned = Guid.NewGuid();
        var user = Guid.NewGuid();

        await new NotificationSender(_enqueuer.Object).For(user).WithCorrelationId(pinned).SendAsync();
        _sent!.CorrelationId.Should().Be(pinned);

        await new NotificationSender(_enqueuer.Object).For(user).SendAsync();
        _sent!.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void An_empty_correlation_id_is_rejected()
    {
        var act = () => new NotificationSender(_enqueuer.Object).For(Guid.NewGuid()).WithCorrelationId(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ToEmail_addresses_the_given_email_and_links_the_optional_user()
    {
        var userId = Guid.NewGuid();

        await new NotificationSender(_enqueuer.Object)
            .ToEmail(" reset@example.com ", NotificationCulture.Ar, userId)
            .WithEmail(EmailTemplate.PasswordResetRequired, new() { ["ResetUrl"] = "https://app.example.com/r" })
            .SendAsync(NotificationChannel.Email);

        _sent!.EmailOverride.Should().Be("reset@example.com");
        _sent.UserId.Should().Be(userId);
        _sent.Culture.Should().Be(NotificationCulture.Ar);
        _sent.Email!.Template.Should().Be(EmailTemplate.PasswordResetRequired);
        _forced.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public async Task ToEmail_without_user_is_not_linked_to_anyone()
    {
        await new NotificationSender(_enqueuer.Object)
            .ToEmail("invitee@example.com").WithEmail(EmailTemplate.TenantInvitation).SendAsync();

        _sent!.UserId.Should().BeEmpty();
    }

    [Fact]
    public async Task ToEmail_never_consults_the_recipient_resolver()
    {
        var resolver = new Mock<INotificationRecipientResolver>(MockBehavior.Strict);

        await new NotificationSender(_enqueuer.Object, resolver.Object)
            .ToEmail("x@example.com", userId: Guid.NewGuid()).WithEmail(EmailTemplate.WelcomeUser).SendAsync();

        resolver.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToEmail_rejects_blank_addresses(string email)
    {
        var act = () => new NotificationSender(_enqueuer.Object).ToEmail(email);

        act.Should().Throw<ArgumentException>();
    }
}
