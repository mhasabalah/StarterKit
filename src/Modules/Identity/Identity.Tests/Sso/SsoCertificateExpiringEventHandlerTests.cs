using Identity.Domains.Sso.EventHandlers;
using Notification.Contracts;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Shared.Contracts.Mediation;
using Shared.Contracts.Pagination;
using Identity.Domains.Sso.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Notification.Contracts.InApp;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Tenancy.Contracts.Members;
using Tenancy.Contracts.Roles;
using Tenancy.Contracts.Tenants;

namespace Identity.Tests.Sso;

public class SsoCertificateExpiringEventHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SsoAdminRole = Guid.NewGuid();
    private static readonly Guid ViewerRole = Guid.NewGuid();

    private readonly Mock<ISender> _sender = new();
    private readonly Mock<ITenantStore> _tenants = new();
    private readonly Mock<INotificationSender> _notifications = new();
    private readonly List<Guid> _notified = [];
    private readonly List<InAppContent> _inApp = [];
    private readonly List<EmailTemplate> _emails = [];

    public SsoCertificateExpiringEventHandlerTests()
    {
        _tenants.Setup(t => t.FindByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantInfo(TenantId, "acme", "Acme"));
        _sender.Setup(s => s.Send(It.IsAny<GetTenantRolesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TenantRoleDto>)
            [
                new TenantRoleDto(SsoAdminRole, "SSO admin", null, false, [TenantPermissions.SsoManage], 1, 1),
                new TenantRoleDto(ViewerRole, "Viewer", null, false, [TenantPermissions.MembersRead], 1, 1)
            ]);
        _notifications.Setup(n => n.For(It.IsAny<Guid>(), It.IsAny<NotificationCulture>()))
            .Returns<Guid, NotificationCulture>((id, _) =>
            {
                _notified.Add(id);
                var builder = new Mock<INotificationBuilder>();
                builder.Setup(b => b.WithEmail(It.IsAny<EmailTemplate>(), It.IsAny<Dictionary<string, object>?>(), null))
                    .Callback<EmailTemplate, Dictionary<string, object>?, EmailAttachment[]?>((t, _, _) => _emails.Add(t))
                    .Returns(builder.Object);
                builder.Setup(b => b.WithInApp(It.IsAny<InAppContent>()))
                    .Callback<InAppContent>(c => _inApp.Add(c)).Returns(builder.Object);
                builder.Setup(b => b.SendAsync(It.IsAny<NotificationChannel?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Guid.NewGuid());
                return builder.Object;
            });
    }

    private SsoCertificateExpiringEventHandler Handler() =>
        new(_sender.Object, _tenants.Object, _notifications.Object, NullLogger<SsoCertificateExpiringEventHandler>.Instance);

    private static SsoCertificateExpiringEvent Event(Guid? tenantId = null) =>
        new(Guid.NewGuid(), tenantId ?? TenantId, "Corporate SAML", DateTime.UtcNow.AddDays(10), 10);

    private static MemberDto Member(Guid userId, bool owner, TenantMembershipStatus status, params Guid[] roles) =>
        new(userId, null, "x@acme.example", status, owner, roles, [], [], DateTime.UtcNow, 1);

    private void Members(params MemberDto[] members) =>
        _sender.Setup(s => s.Send(It.IsAny<GetMembersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginatedResult<MemberDto> { Result = members.ToList() });

    [Fact]
    public async Task The_owner_and_members_of_a_role_with_sso_manage_are_alerted_by_email_and_in_app()
    {
        Guid owner = Guid.NewGuid(), admin = Guid.NewGuid(), viewer = Guid.NewGuid();
        Members(
            Member(owner, true, TenantMembershipStatus.Active),
            Member(admin, false, TenantMembershipStatus.Active, SsoAdminRole),
            Member(viewer, false, TenantMembershipStatus.Active, ViewerRole));

        await Handler().Handle(Event(), CancellationToken.None);

        _notified.Should().BeEquivalentTo([owner, admin]);
        _emails.Should().OnlyContain(t => t == EmailTemplate.SsoCertificateExpiring);
        _inApp.Should().HaveCount(2).And.OnlyContain(c => c.Category == "security.sso-certificate-expiring");
    }

    [Fact]
    public async Task A_tenant_that_no_longer_exists_notifies_nobody()
    {
        await Handler().Handle(Event(Guid.NewGuid()), CancellationToken.None);

        _notified.Should().BeEmpty();
    }
}
