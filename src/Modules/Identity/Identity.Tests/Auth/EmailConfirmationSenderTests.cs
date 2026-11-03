using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Services.EmailConfirmation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Notification.Contracts;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;

namespace Identity.Tests.Auth;

public sealed class EmailConfirmationSenderTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<INotificationSender> _notifications = new();
    private readonly Mock<INotificationBuilder> _builder = new();
    private Dictionary<string, object>? _model;
    private string? _recipient;

    private EmailConfirmationSender CreateSender(bool sendOnRegister = true)
    {
        _users.Setup(m => m.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("tok+/=");
        _notifications
            .Setup(n => n.ToEmail(It.IsAny<string>(), It.IsAny<NotificationCulture>(), It.IsAny<Guid?>()))
            .Callback<string, NotificationCulture, Guid?>((email, _, _) => _recipient = email)
            .Returns(_builder.Object);
        _builder
            .Setup(b => b.WithEmail(EmailTemplate.EmailConfirmation, It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<EmailAttachment[]?>()))
            .Callback<EmailTemplate, Dictionary<string, object>?, EmailAttachment[]?>((_, model, _) => _model = model)
            .Returns(_builder.Object);

        return new EmailConfirmationSender(_users.Object, _notifications.Object,
            Microsoft.Extensions.Options.Options.Create(new EmailConfirmationOptions
            {
                FrontendBaseUrl = "https://app.test/",
                TokenExpiryHours = 24,
                SendOnRegister = sendOnRegister
            }),
            NullLogger<EmailConfirmationSender>.Instance);
    }

    [Fact]
    public async Task Sends_an_escaped_link_to_the_users_email_forced_to_the_email_channel()
    {
        var user = Mocks.User();
        user.FirstName = "Ada";

        (await CreateSender().SendAsync(user)).Should().BeTrue();

        _recipient.Should().Be(user.Email);
        _model!["ConfirmUrl"].Should().Be($"https://app.test/confirm-email?userId={user.Id}&token=tok%2B%2F%3D");
        _model["ExpiresInHours"].Should().Be(24);
        _model["FirstName"].Should().Be("Ada");
        _builder.Verify(b => b.SendAsync(NotificationChannel.Email, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Skips_confirmed_inactive_and_phone_placeholder_accounts()
    {
        var sender = CreateSender();

        var confirmed = Mocks.User();
        confirmed.EmailConfirmed = true;
        var inactive = Mocks.User(active: false);
        var phoneOnly = Mocks.User("+201000000000");
        phoneOnly.Email = $"201000000000{ApplicationUser.PhonePlaceholderEmailDomain}";
        var noEmail = Mocks.User();
        noEmail.Email = null;

        foreach (var user in new[] { confirmed, inactive, phoneOnly, noEmail })
        {
            (await sender.SendAsync(user)).Should().BeFalse();
        }

        _users.Verify(m => m.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TrySend_never_throws_so_registration_survives_a_mail_outage()
    {
        var sender = CreateSender();
        _builder.Setup(b => b.SendAsync(It.IsAny<NotificationChannel?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));

        await sender.Invoking(s => s.TrySendAsync(Mocks.User())).Should().NotThrowAsync();
    }

    [Fact]
    public async Task TrySend_respects_SendOnRegister_false()
    {
        await CreateSender(sendOnRegister: false).TrySendAsync(Mocks.User());

        _notifications.VerifyNoOtherCalls();
    }
}
