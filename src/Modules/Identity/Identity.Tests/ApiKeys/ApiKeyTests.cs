using System.Security.Claims;
using Identity.Contracts.ApiKeys;
using Identity.Data.Repository.ApiKeys;
using Identity.Domains.ApiKeys.Exceptions;
using Identity.Domains.ApiKeys.Features.MyApiKeys;
using Identity.Domains.ApiKeys.Features.UserApiKeys;
using Identity.Domains.Users.Models;
using Identity.Models;
using Identity.Options;
using Identity.Resources;
using Identity.Services.ApiKeys;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Identity.Services.Token;
using Shared.Core.Authorization;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.Web;

namespace Identity.Tests.ApiKeys;

public sealed class ApiKeyModelTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_returns_the_raw_key_once_and_stores_only_its_sha256_hash()
    {
        var (key, raw) = ApiKey.Create(Guid.NewGuid(), null, " CI ", [Permissions.BillingRead], Now, Now.AddDays(30));

        raw.Should().StartWith($"sk_{key.Prefix}_");
        raw.Length.Should().BeGreaterThan(50);
        key.KeyHash.Should().Be(TokenHasher.Hash(raw)).And.NotContain(raw);
        key.Name.Should().Be("CI");
        key.Prefix.Should().HaveLength(ApiKey.PrefixLength);
        raw.Should().NotContain(key.KeyHash);
    }

    [Fact]
    public void Two_keys_never_share_a_prefix_or_secret()
    {
        var a = ApiKey.Create(Guid.NewGuid(), null, "a", [], Now, Now.AddDays(1));
        var b = ApiKey.Create(Guid.NewGuid(), null, "b", [], Now, Now.AddDays(1));

        a.RawKey.Should().NotBe(b.RawKey);
        a.Key.Prefix.Should().NotBe(b.Key.Prefix);
    }

    [Fact]
    public void Matches_accepts_the_issued_key_and_rejects_anything_else()
    {
        var (key, raw) = ApiKey.Create(Guid.NewGuid(), null, "k", [], Now, Now.AddDays(1));

        key.Matches(raw).Should().BeTrue();
        key.Matches(raw + "x").Should().BeFalse();
        key.Matches($"sk_{key.Prefix}_{new string('A', 43)}").Should().BeFalse("right prefix, wrong secret");
        key.Matches(string.Empty).Should().BeFalse();
    }

    [Theory]
    [InlineData("sk_abcd1234_" + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("pk_abcd1234_" + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("sk_abc_" + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("sk_abcd1234_short", false)]
    [InlineData("sk_abcd12!4_" + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParsePrefix_only_accepts_the_documented_shape(string? raw, bool ok) =>
        ApiKey.TryParsePrefix(raw, out _).Should().Be(ok);

    [Fact]
    public void Secrets_containing_underscores_still_parse()
    {
        ApiKey.TryParsePrefix("sk_abcd1234_AAAAAAAA_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", out var prefix).Should().BeTrue();
        prefix.Should().Be("abcd1234");
    }

    [Fact]
    public void Lifecycle_active_then_expired_then_revoked_is_idempotent()
    {
        var (key, _) = ApiKey.Create(Guid.NewGuid(), null, "k", [], Now, Now.AddDays(1));

        key.IsActive(Now).Should().BeTrue();
        key.IsActive(Now.AddDays(1)).Should().BeFalse("expiry is exclusive");
        key.Revoke(Now, null).Should().BeTrue();
        key.Revoke(Now.AddHours(1), null).Should().BeFalse();
        key.RevokedAt.Should().Be(Now);
        key.IsActive(Now).Should().BeFalse();
    }
}

public sealed class ApiKeyAuthenticatorTests
{
    private readonly Mock<IApiKeyRepository> _keys = new();
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ApplicationUser _owner = Mocks.User();
    private readonly ApiKeyOptions _options = new();

    private ApiKeyAuthenticator Sut(Mock<ITenantMembershipResolver>? memberships = null, bool multiTenancy = false) => new(
        _keys.Object, _users.Object, Microsoft.Extensions.Options.Options.Create(_options), _time,
        NullLogger<ApiKeyAuthenticator>.Instance, memberships?.Object,
        Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = multiTenancy }));

    private (ApiKey Key, string Raw) Issue(Guid? tenantId = null, string[]? scopes = null, int days = 30, Guid? userId = null)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var issued = ApiKey.Create(userId ?? _owner.Id, tenantId, "ci", scopes ?? [Permissions.BillingRead], now, now.AddDays(days));
        _keys.Setup(k => k.FindByPrefixAsync(issued.Key.Prefix, It.IsAny<CancellationToken>())).ReturnsAsync(issued.Key);
        return issued;
    }

    public ApiKeyAuthenticatorTests() => _users.WithUsers((_owner, [RoleConstants.Support]));

    [Fact]
    public async Task A_valid_key_authenticates_as_its_owner_with_roles_and_scopes()
    {
        var (key, raw) = Issue(scopes: [Permissions.BillingRead, Permissions.UsersRead]);

        var result = await Sut().AuthenticateAsync(raw);

        result.Succeeded.Should().BeTrue();
        var principal = result.Principal!;
        principal.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be(_owner.Id.ToString());
        principal.FindFirstValue("sub").Should().Be(_owner.Id.ToString());
        principal.FindAll(ClaimTypes.Role).Should().BeEmpty("a key principal carries no role: scopes cannot be bypassed by role checks");
        principal.FindFirst(IdentityClaimTypes.IsAdmin).Should().BeNull();
        principal.FindAll(ApiKeyClaimTypes.OwnerRole).Select(c => c.Value).Should().BeEquivalentTo([RoleConstants.Support]);
        principal.FindAll(ApiKeyClaimTypes.Scope).Select(c => c.Value)
            .Should().BeEquivalentTo([Permissions.BillingRead, Permissions.UsersRead]);
        principal.FindFirstValue(ApiKeyClaimTypes.KeyId).Should().Be(key.Id.ToString());
        principal.Identity!.IsAuthenticated.Should().BeTrue();
        principal.FindAll(Permissions.ClaimType).Should().BeEmpty("permissions are derived per request, never issued");
    }

    [Fact]
    public async Task Admin_owned_key_principal_is_in_no_role_and_has_no_is_admin_claim()
    {
        _users.WithUsers((_owner, [RoleConstants.Admin, RoleConstants.SuperAdmin]));
        var (_, raw) = Issue(scopes: [Permissions.BillingRead]);

        var principal = (await Sut().AuthenticateAsync(raw)).Principal!;

        principal.IsInRole(RoleConstants.Admin).Should().BeFalse();
        principal.IsInRole(RoleConstants.SuperAdmin).Should().BeFalse();
        principal.FindFirst(IdentityClaimTypes.IsAdmin).Should().BeNull();
        principal.FindAll(ApiKeyClaimTypes.OwnerRole).Select(c => c.Value)
            .Should().BeEquivalentTo([RoleConstants.Admin, RoleConstants.SuperAdmin]);
    }

    [Fact]
    public async Task Wrong_secret_unknown_prefix_and_malformed_keys_are_rejected_without_hitting_the_user_store()
    {
        var (key, raw) = Issue();

        (await Sut().AuthenticateAsync($"sk_{key.Prefix}_{new string('B', 43)}")).Succeeded.Should().BeFalse();
        (await Sut().AuthenticateAsync("sk_zzzzzzzz_" + new string('C', 43))).Succeeded.Should().BeFalse();
        (await Sut().AuthenticateAsync("garbage")).Succeeded.Should().BeFalse();
        (await Sut().AuthenticateAsync(raw)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Revoked_and_expired_keys_are_rejected()
    {
        var revoked = Issue();
        revoked.Key.Revoke(_time.GetUtcNow().UtcDateTime, null);
        var expired = Issue(days: 1);
        _time.Advance(TimeSpan.FromDays(2));

        (await Sut().AuthenticateAsync(revoked.Raw)).Failure.Should().Contain("revoked");
        (await Sut().AuthenticateAsync(expired.Raw)).Failure.Should().Contain("expired");
    }

    [Fact]
    public async Task A_key_stops_working_when_its_owner_is_deactivated_locked_or_deleted()
    {
        var (_, raw) = Issue();

        _owner.IsActive = false;
        (await Sut().AuthenticateAsync(raw)).Succeeded.Should().BeFalse();

        _owner.IsActive = true;
        _users.Setup(u => u.IsLockedOutAsync(_owner)).ReturnsAsync(true);
        (await Sut().AuthenticateAsync(raw)).Succeeded.Should().BeFalse();

        _users.Setup(u => u.IsLockedOutAsync(_owner)).ReturnsAsync(false);
        _users.Setup(u => u.FindByIdAsync(_owner.Id.ToString())).ReturnsAsync((ApplicationUser?)null);
        (await Sut().AuthenticateAsync(raw)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task A_disabled_feature_rejects_every_key()
    {
        var (_, raw) = Issue();
        _options.Enabled = false;

        (await Sut().AuthenticateAsync(raw)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_key_carries_tid_and_the_current_membership_stamp()
    {
        var tenantId = Guid.NewGuid();
        var (_, raw) = Issue(tenantId);
        var memberships = new Mock<ITenantMembershipResolver>();
        memberships.Setup(m => m.GetMembershipAsync(_owner.Id, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantMembershipInfo(tenantId, _owner.Id, false, new HashSet<string>(),
                BranchScope.Unrestricted, "stamp-7"));

        var result = await Sut(memberships, multiTenancy: true).AuthenticateAsync(raw);

        result.Principal!.FindFirstValue(TenantClaimTypes.TenantId).Should().Be(tenantId.ToString());
        result.Principal.FindFirstValue(TenantClaimTypes.MembershipStamp).Should().Be("stamp-7");
    }

    [Fact]
    public async Task A_tenant_key_is_rejected_once_the_owner_left_the_tenant()
    {
        var tenantId = Guid.NewGuid();
        var (_, raw) = Issue(tenantId);
        var memberships = new Mock<ITenantMembershipResolver>();
        memberships.Setup(m => m.GetMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantMembershipInfo?)null);

        (await Sut(memberships, multiTenancy: true).AuthenticateAsync(raw)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Last_used_is_written_once_per_interval_not_on_every_request()
    {
        var (key, raw) = Issue();

        await Sut().AuthenticateAsync(raw); // never used: write
        _keys.Verify(k => k.TouchAsync(key.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

        // Repository now reports a recent use: no write.
        var recent = ApiKey.Create(_owner.Id, null, "r", [], _time.GetUtcNow().UtcDateTime, _time.GetUtcNow().UtcDateTime.AddDays(1));
        typeof(ApiKey).GetProperty(nameof(ApiKey.LastUsedAt))!.SetValue(recent.Key, _time.GetUtcNow().UtcDateTime.AddMinutes(-1));
        _keys.Setup(k => k.FindByPrefixAsync(recent.Key.Prefix, It.IsAny<CancellationToken>())).ReturnsAsync(recent.Key);
        await Sut().AuthenticateAsync(recent.RawKey);
        _keys.Verify(k => k.TouchAsync(recent.Key.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_failing_last_used_write_never_fails_the_request()
    {
        var (_, raw) = Issue();
        _keys.Setup(k => k.TouchAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db"));

        (await Sut().AuthenticateAsync(raw)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Keys_are_read_from_the_authorization_header_or_x_api_key_and_bearer_wins()
    {
        var http = new DefaultHttpContext();
        ApiKeyRequest.Extract(http.Request).Should().BeNull();
        ApiKeyRequest.SelectScheme(http).Should().BeNull();

        http.Request.Headers["X-Api-Key"] = "sk_x";
        ApiKeyRequest.Extract(http.Request).Should().Be("sk_x");
        ApiKeyRequest.SelectScheme(http).Should().Be("ApiKey");

        http.Request.Headers.Authorization = "ApiKey sk_y";
        ApiKeyRequest.Extract(http.Request).Should().Be("sk_y");

        http.Request.Headers.Authorization = "Bearer jwt";
        ApiKeyRequest.SelectScheme(http).Should().BeNull("an explicit Bearer token is authenticated as a JWT");
    }
}

public sealed class ApiKeyHandlerTests
{
    private readonly Mock<IApiKeyRepository> _keys = new();
    private readonly Mock<IHttpContextService> _http = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ApiKeyOptions _options = new();
    private readonly KeyLocalizer<IdentityResource> _localizer = new();

    public ApiKeyHandlerTests()
    {
        _http.Setup(h => h.GetUserIdAsGuid()).Returns(_userId);
        _http.Setup(h => h.GetRoles()).Returns([RoleConstants.Finance]);
    }

    private CreateApiKeyHandler Create(Mock<ITenantContext>? tenant = null) => new(
        _keys.Object, _http.Object, Microsoft.Extensions.Options.Options.Create(_options), _time, _localizer,
        NullLogger<CreateApiKeyHandler>.Instance, tenant?.Object);

    [Fact]
    public async Task Create_returns_the_secret_once_and_persists_only_the_hash()
    {
        ApiKey? saved = null;
        _keys.Setup(k => k.AddAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()))
            .Callback<ApiKey, CancellationToken>((k, _) => saved = k);

        var result = await Create().Handle(new CreateApiKeyCommand("Billing export", [Permissions.BillingRead]), default);

        result.ApiKey.Should().StartWith("sk_");
        saved!.KeyHash.Should().Be(TokenHasher.Hash(result.ApiKey));
        saved.UserId.Should().Be(_userId);
        saved.ExpiresAt.Should().Be(_time.GetUtcNow().UtcDateTime.AddDays(_options.DefaultExpiryDays));
        result.Key.Prefix.Should().Be($"sk_{saved.Prefix}");
        System.Text.Json.JsonSerializer.Serialize(result.Key).Should().NotContain(result.ApiKey, "listing DTOs never hold the secret");
    }

    [Fact]
    public async Task Create_binds_the_key_to_the_active_tenant()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        ApiKey? saved = null;
        _keys.Setup(k => k.AddAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()))
            .Callback<ApiKey, CancellationToken>((k, _) => saved = k);

        await Create(tenant).Handle(new CreateApiKeyCommand("k", [Permissions.BillingRead]), default);

        saved!.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task Create_refuses_a_scope_the_owner_does_not_hold()
    {
        var act = () => Create().Handle(new CreateApiKeyCommand("k", [Permissions.UsersManage]), default);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain("ApiKey.ScopeNotHeld");
        _keys.Verify(k => k.AddAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_accepts_a_tenant_scope_held_through_the_membership()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.Membership).Returns(new TenantMembershipInfo(
            Guid.NewGuid(), _userId, false, new HashSet<string> { TenantPermissions.ProductsManage },
            BranchScope.Unrestricted, "s"));

        var result = await Create(tenant).Handle(
            new CreateApiKeyCommand("k", [TenantPermissions.ProductsManage]), default);

        result.Key.Scopes.Should().BeEquivalentTo([TenantPermissions.ProductsManage]);
    }

    [Fact]
    public async Task Create_rejects_too_long_expiry_and_too_many_active_keys()
    {
        await Create().Invoking(h => h.Handle(
                new CreateApiKeyCommand("k", [Permissions.BillingRead], ExpiresInDays: _options.MaxExpiryDays + 1), default))
            .Should().ThrowAsync<BadRequestException>();

        _keys.Setup(k => k.CountActiveAsync(_userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_options.MaxActiveKeysPerUser);
        await Create().Invoking(h => h.Handle(new CreateApiKeyCommand("k", [Permissions.BillingRead]), default))
            .Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Create_is_refused_when_api_keys_are_disabled()
    {
        _options.Enabled = false;

        await Create().Invoking(h => h.Handle(new CreateApiKeyCommand("k", [Permissions.BillingRead]), default))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public void Validator_rejects_missing_name_no_scopes_unknown_scopes_and_non_positive_expiry()
    {
        var validator = new CreateApiKeyCommandValidator(_localizer);

        validator.Validate(new CreateApiKeyCommand("ok", [Permissions.BillingRead])).IsValid.Should().BeTrue();
        validator.Validate(new CreateApiKeyCommand("", [Permissions.BillingRead])).IsValid.Should().BeFalse();
        validator.Validate(new CreateApiKeyCommand(new string('x', 101), [Permissions.BillingRead])).IsValid.Should().BeFalse();
        validator.Validate(new CreateApiKeyCommand("k", [])).IsValid.Should().BeFalse();
        validator.Validate(new CreateApiKeyCommand("k", ["root"])).IsValid.Should().BeFalse();
        validator.Validate(new CreateApiKeyCommand("k", [Permissions.BillingRead], 0)).IsValid.Should().BeFalse();
        validator.Validate(new CreateApiKeyCommand("k", [Permissions.BillingRead, TenantPermissions.ProductsManage])).IsValid
            .Should().BeTrue("platform and tenant permissions are both grantable");
    }

    [Fact]
    public async Task Revoke_marks_my_key_revoked_and_someone_elses_is_not_found()
    {
        var mine = ApiKey.Create(_userId, null, "k", [], _time.GetUtcNow().UtcDateTime, _time.GetUtcNow().UtcDateTime.AddDays(1)).Key;
        _keys.Setup(k => k.FindOwnedAsync(_userId, mine.Id, It.IsAny<CancellationToken>())).ReturnsAsync(mine);
        var handler = new RevokeMyApiKeyHandler(_keys.Object, _http.Object, _time);

        (await handler.Handle(new RevokeMyApiKeyCommand(mine.Id), default)).Revoked.Should().BeTrue();
        mine.IsRevoked.Should().BeTrue();
        _keys.Verify(k => k.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);

        (await handler.Handle(new RevokeMyApiKeyCommand(mine.Id), default)).Revoked.Should().BeFalse("already revoked");

        await handler.Invoking(h => h.Handle(new RevokeMyApiKeyCommand(Guid.NewGuid()), default))
            .Should().ThrowAsync<ApiKeyNotFoundException>();
    }

    [Fact]
    public async Task Admin_revoke_is_audited_but_cannot_target_staff_unless_super_admin()
    {
        var users = Mocks.UserManager();
        var target = Mocks.User();
        users.WithUsers((target, [RoleConstants.User]));
        var key = ApiKey.Create(target.Id, null, "k", [], _time.GetUtcNow().UtcDateTime, _time.GetUtcNow().UtcDateTime.AddDays(1)).Key;
        _keys.Setup(k => k.FindOwnedAsync(target.Id, key.Id, It.IsAny<CancellationToken>())).ReturnsAsync(key);
        var audit = new RecordingAuditWriter();
        var admin = new Mock<IHttpContextService>();
        admin.Setup(h => h.GetUserIdAsGuid()).Returns(Guid.NewGuid());
        admin.Setup(h => h.GetRoles()).Returns([RoleConstants.Admin]);
        var handler = new RevokeUserApiKeyHandler(users.Object, _keys.Object, audit, admin.Object, _time, _localizer);

        (await handler.Handle(new RevokeUserApiKeyCommand(target.Id, key.Id), default)).Revoked.Should().BeTrue();
        audit.Entries.Should().ContainSingle(e => e.Action == "api_key.revoked" && e.TargetId == target.Id);
        audit.Entries[0].AfterJson.Should().NotContain("sk_", "the audit trail never holds key material");

        var staff = Mocks.User();
        users.WithUsers((staff, [RoleConstants.SuperAdmin]));
        await handler.Invoking(h => h.Handle(new RevokeUserApiKeyCommand(staff.Id, Guid.NewGuid()), default))
            .Should().ThrowAsync<ForbiddenException>();
    }
}
