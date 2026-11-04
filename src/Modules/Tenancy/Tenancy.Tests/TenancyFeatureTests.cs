using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.MultiTenancy;
using Notification.Contracts;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Tenancy.Contracts.Tenants;
using Tenancy.Domains.Invitations.Models;
using Tenancy.Domains.Invitations.Services;
using Tenancy.Domains.Tenants.Exceptions;
using Tenancy.Domains.Tenants.Models;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tenancy.Tests;

/// <summary>Automated tenant move: state machine of the <see cref="Tenant"/> aggregate.</summary>
public sealed class TenantMoveTests
{
    private static Tenant Shared() => Tenant.Create("acme", "Acme", Guid.NewGuid(), TenantIsolation.Shared);

    [Fact]
    public void Begin_marks_the_tenant_migrating_and_exposes_the_target()
    {
        var tenant = Shared();
        tenant.BeginMove(TenantIsolation.Dedicated, " acme-eu ");

        tenant.Status.Should().Be(TenantStatus.Migrating);
        tenant.EffectiveStatus.Should().Be(TenantStatus.Active);
        tenant.ToInfo().Isolation.Should().Be(TenantIsolation.Shared, "the data still lives in the source");
        tenant.ToMoveTargetInfo().Should().Match<TenantInfo>(t =>
            t.Isolation == TenantIsolation.Dedicated && t.ConnectionSecretRef == "acme-eu" && t.Id == tenant.Id);
    }

    [Fact]
    public void Complete_switches_the_isolation_and_restores_the_status()
    {
        var tenant = Shared();
        tenant.BeginMove(TenantIsolation.Dedicated, null);
        tenant.CompleteMove();

        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.Isolation.Should().Be(TenantIsolation.Dedicated);
        tenant.ConnectionSecretRef.Should().BeNull();
        tenant.MoveTargetIsolation.Should().BeNull();
    }

    [Fact]
    public void Fail_keeps_the_tenant_where_it_was()
    {
        var tenant = Shared();
        tenant.Suspend("ops");
        tenant.BeginMove(TenantIsolation.Dedicated, null);
        tenant.FailMove();

        tenant.Status.Should().Be(TenantStatus.Suspended);
        tenant.Isolation.Should().Be(TenantIsolation.Shared);
    }

    [Fact]
    public void Moving_to_the_current_location_is_rejected() =>
        FluentActions.Invoking(() => Shared().BeginMove(TenantIsolation.Shared, null))
            .Should().Throw<InvalidTenantException>();

    [Fact]
    public void Only_active_or_suspended_tenants_can_move()
    {
        var tenant = Shared();
        tenant.RequestDeletion(DateTime.UtcNow, 30, null);
        FluentActions.Invoking(() => tenant.BeginMove(TenantIsolation.Dedicated, null))
            .Should().Throw<InvalidTenantException>();
    }

    [Fact]
    public void A_move_blocks_deletion_and_manual_isolation_changes()
    {
        var tenant = Shared();
        tenant.BeginMove(TenantIsolation.Dedicated, null);

        FluentActions.Invoking(() => tenant.RequestDeletion(DateTime.UtcNow, 30, null)).Should().Throw<InvalidTenantException>();
        FluentActions.Invoking(() => tenant.ChangeIsolation(TenantIsolation.Shared, null)).Should().Throw<InvalidTenantException>();
        FluentActions.Invoking(() => tenant.BeginMove(TenantIsolation.Dedicated, "x")).Should().Throw<InvalidTenantException>();
    }

    [Fact]
    public void Billing_suspension_during_a_move_applies_when_it_ends()
    {
        var tenant = Shared();
        tenant.BeginMove(TenantIsolation.Dedicated, null);

        tenant.Suspend("Subscription expired");
        tenant.Status.Should().Be(TenantStatus.Migrating, "the move keeps the tenant read-only meanwhile");
        tenant.EffectiveStatus.Should().Be(TenantStatus.Suspended);

        tenant.CompleteMove();
        tenant.Status.Should().Be(TenantStatus.Suspended);
        tenant.StatusReason.Should().Be("Subscription expired");
    }

    [Fact]
    public void Reactivation_during_a_move_applies_when_it_ends()
    {
        var tenant = Shared();
        tenant.Suspend("Subscription expired");
        tenant.BeginMove(TenantIsolation.Dedicated, null);

        tenant.Reactivate();
        tenant.CompleteMove();
        tenant.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public void Ending_without_a_move_is_rejected() =>
        FluentActions.Invoking(() => Shared().CompleteMove()).Should().Throw<InvalidTenantException>();
}

public sealed class InvitationDeliveryTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static TenantInvitation Invitation() =>
        TenantInvitation.Create(Guid.NewGuid(), "new@acme.com", [], [], Guid.NewGuid(), Now, TimeSpan.FromDays(7)).Invitation;

    [Fact]
    public void Renew_rotates_the_token_and_restarts_the_lifetime()
    {
        var (invitation, oldToken) = TenantInvitation.Create(Guid.NewGuid(), "new@acme.com", [], [], Guid.NewGuid(), Now,
            TimeSpan.FromDays(1));

        var newToken = invitation.Renew(Now.AddDays(5), TimeSpan.FromDays(7));

        newToken.Should().NotBe(oldToken);
        invitation.TokenHash.Should().Be(TenantInvitation.HashToken(newToken), "the old link stops working");
        invitation.ExpiresAt.Should().Be(Now.AddDays(12));
        invitation.Status.Should().Be(TenantInvitationStatus.Pending);
    }

    [Fact]
    public void Accepted_or_revoked_invitations_cannot_be_renewed()
    {
        var invitation = Invitation();
        invitation.Revoke();
        FluentActions.Invoking(() => invitation.Renew(Now, TimeSpan.FromDays(7)))
            .Should().Throw<Exception>().Which.GetType().Name.Should().Be("InvalidMembershipException");
    }

    [Theory]
    [InlineData("/invitations/accept?token={token}", "https://app.example.com/invitations/accept?token=a%2Bb")]
    [InlineData("invitations/{token}", "https://app.example.com/invitations/a%2Bb")]
    [InlineData("https://portal.example.com/join?t={token}", "https://portal.example.com/join?t=a%2Bb")]
    public void Accept_url_is_absolute_and_escapes_the_token(string template, string expected)
    {
        Mailer(new TenantInvitationOptions { AcceptUrl = template }, new RecordingNotificationSender())
            .BuildAcceptUrl("a+b").Should().Be(expected);
    }

    [Fact]
    public async Task Sends_the_invitation_email_for_the_tenant()
    {
        var notifications = new RecordingNotificationSender();
        var tenant = new TenantInfo(Guid.NewGuid(), "acme", "Acme", Culture: "ar-EG");
        var invitation = Invitation();

        var queued = await Mailer(new TenantInvitationOptions(), notifications)
            .SendAsync(invitation, "tok", tenant, Guid.NewGuid(), Now, CancellationToken.None);

        queued.Should().BeTrue();
        var sent = notifications.Sent.Should().ContainSingle().Subject;
        sent.Email.Should().Be("new@acme.com");
        sent.UserId.Should().BeNull("the invitee may not be a user yet");
        sent.Culture.Should().Be(NotificationCulture.Ar);
        sent.Template.Should().Be(EmailTemplate.TenantInvitation);
        sent.ForcedChannel.Should().Be(NotificationChannel.Email);
        ((string)sent.Model!["AcceptUrl"]).Should().EndWith("token=tok");
        ((int)sent.Model["ExpiresInDays"]).Should().Be(7);
    }

    [Fact]
    public async Task Queue_failures_are_reported_not_thrown()
    {
        var notifications = new RecordingNotificationSender { Failure = new InvalidOperationException("db down") };

        (await Mailer(new TenantInvitationOptions(), notifications)
                .SendAsync(Invitation(), "tok", new TenantInfo(Guid.NewGuid(), "acme", "Acme"), Guid.NewGuid(), Now,
                    CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Disabled_sending_queues_nothing()
    {
        var notifications = new RecordingNotificationSender();
        (await Mailer(new TenantInvitationOptions { SendEmail = false }, notifications)
                .SendAsync(Invitation(), "tok", new TenantInfo(Guid.NewGuid(), "acme", "Acme"), Guid.NewGuid(), Now,
                    CancellationToken.None))
            .Should().BeFalse();
        notifications.Sent.Should().BeEmpty();
    }

    private static TenantInvitationMailer Mailer(TenantInvitationOptions invitations, INotificationSender notifications)
    {
        var sender = new Mock<Shared.Contracts.Mediation.ISender>();
        sender.Setup(s => s.Send(It.IsAny<UserDirectory.Contracts.Queries.GetUserSummariesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return new TenantInvitationMailer(notifications, new AppLinks(), sender.Object,
            MsOptions.Create(new MultiTenancyOptions { Invitations = invitations }),
            NullLogger<TenantInvitationMailer>.Instance);
    }

    /// <summary>Same contract as the Notification module's resolver, against https://app.example.com/.</summary>
    private sealed class AppLinks : INotificationLinkResolver
    {
        public string ToAbsolute(string url) => url.StartsWith("https://", StringComparison.Ordinal)
            ? url
            : "https://app.example.com/" + url.TrimStart('/');
    }

    private sealed record SentNotification(
        string Email, Guid? UserId, NotificationCulture Culture, EmailTemplate? Template,
        Dictionary<string, object>? Model, NotificationChannel? ForcedChannel);

    private sealed class RecordingNotificationSender : INotificationSender
    {
        public List<SentNotification> Sent { get; } = [];
        public Exception? Failure { get; init; }

        public INotificationBuilder For(Guid userId, NotificationCulture culture = NotificationCulture.En) =>
            throw new NotSupportedException("invitations are addressed to the invited e-mail");

        public INotificationBuilder ToEmail(string email, NotificationCulture culture = NotificationCulture.En, Guid? userId = null) =>
            new Builder(this, email, userId, culture);

        private sealed class Builder(RecordingNotificationSender owner, string email, Guid? userId, NotificationCulture culture)
            : INotificationBuilder
        {
            private EmailTemplate? _template;
            private Dictionary<string, object>? _model;

            public INotificationBuilder WithEmail(EmailTemplate template, Dictionary<string, object>? model = null,
                EmailAttachment[]? attachments = null)
            {
                (_template, _model) = (template, model);
                return this;
            }

            public INotificationBuilder WithWhatsApp(WhatsAppTemplate template, Dictionary<string, object>? model = null) => this;

            public Task<Guid> SendAsync(NotificationChannel? forcedChannel = null, CancellationToken ct = default)
            {
                if (owner.Failure is not null) throw owner.Failure;
                owner.Sent.Add(new SentNotification(email, userId, culture, _template, _model, forcedChannel));
                return Task.FromResult(Guid.NewGuid());
            }
        }
    }
}
