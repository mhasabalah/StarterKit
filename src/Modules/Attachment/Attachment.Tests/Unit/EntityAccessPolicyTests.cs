using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Authorization;
using Attachment.Domains.Attachments.Models;
using Attachment.Tests.TestSupport;
using Shared.Core.Exceptions;

namespace Attachment.Tests.Unit;

public sealed class EntityAccessPolicyTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    public static TheoryData<string, bool> UserEntityMatrix => new()
    {
        { "self", true },
        { "other", false },
        { "admin", true },
        { "anonymous", false }
    };

    private static Caller CallerFor(string who) => who switch
    {
        "self" => Caller.User(Owner),
        "other" => Caller.User(Other),
        "admin" => Caller.Admin(),
        _ => Caller.Anonymous()
    };

    [Theory]
    [MemberData(nameof(UserEntityMatrix))]
    public async Task User_entity_attach_matrix(string who, bool expected)
    {
        var access = TestServices.Access(CallerFor(who));

        (await access.CanAttachToEntityAsync(EntityType.User, Owner)).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(UserEntityMatrix))]
    public async Task User_entity_read_matrix(string who, bool expected)
    {
        var access = TestServices.Access(CallerFor(who));

        (await access.CanReadEntityAsync(EntityType.User, Owner)).Should().Be(expected);
    }

    [Theory]
    [InlineData(EntityType.Product)]
    [InlineData(EntityType.Company)]
    [InlineData(EntityType.Other)]
    public async Task Entity_types_without_a_policy_are_denied_by_default(EntityType type)
    {
        var access = TestServices.Access(Caller.User(Owner));

        (await access.CanAttachToEntityAsync(type, Owner)).Should().BeFalse();
        (await access.CanReadEntityAsync(type, Owner)).Should().BeFalse();
    }

    [Fact]
    public async Task Admin_is_allowed_even_without_a_policy()
    {
        var access = TestServices.Access(Caller.Admin());

        (await access.CanAttachToEntityAsync(EntityType.Product, Guid.NewGuid())).Should().BeTrue();
    }

    [Fact]
    public async Task Policy_registered_by_another_module_is_used()
    {
        var productId = Guid.NewGuid();
        var access = TestServices.Access(Caller.User(Owner),
            s => s.AddAttachmentEntityPolicy<OwnsEverythingPolicy>(EntityType.Product));

        (await access.CanAttachToEntityAsync(EntityType.Product, productId)).Should().BeTrue();
        (await access.CanAttachToEntityAsync(EntityType.Company, productId)).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureCanAttach_throws_403_for_users_and_401_for_anonymous()
    {
        await FluentActions.Awaiting(() => TestServices.Access(Caller.User(Other))
                .EnsureCanAttachToEntityAsync(EntityType.User, Owner))
            .Should().ThrowAsync<ForbiddenException>();

        await FluentActions.Awaiting(() => TestServices.Access(Caller.Anonymous())
                .EnsureCanAttachToEntityAsync(EntityType.User, Owner))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Delete_through_http_requires_entity_policy_but_trusted_calls_require_uploader()
    {
        // Uploaded by Other onto Owner's user entity (e.g. by module code).
        var attachment = Attachment(uploadedBy: Other, EntityType.User, Owner);

        (await TestServices.Access(Caller.User(Owner)).CanDeleteAsync(attachment, enforceEntityPolicy: true))
            .Should().BeTrue("the policy lets the user manage files of their own entity");
        (await TestServices.Access(Caller.User(Other)).CanDeleteAsync(attachment, enforceEntityPolicy: true))
            .Should().BeFalse("the uploader cannot manage files of someone else's entity over HTTP");
        (await TestServices.Access(Caller.User(Other)).CanDeleteAsync(attachment, enforceEntityPolicy: false))
            .Should().BeTrue("trusted module calls keep the uploader rule");
        (await TestServices.Access(Caller.Anonymous()).CanDeleteAsync(attachment, enforceEntityPolicy: false))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Delete_of_policy_less_entity_attachment_is_denied_over_http_even_for_the_uploader()
    {
        var proof = Attachment(uploadedBy: Owner, EntityType.Other, Guid.NewGuid());

        (await TestServices.Access(Caller.User(Owner)).CanDeleteAsync(proof, enforceEntityPolicy: true)).Should().BeFalse();
        (await TestServices.Access(Caller.Admin()).CanDeleteAsync(proof, enforceEntityPolicy: true)).Should().BeTrue();
    }

    internal static AttachmentFile Attachment(Guid uploadedBy, EntityType? type = null, Guid? entityId = null, bool isPublic = false) =>
        AttachmentFile.Create(Guid.NewGuid(), "f.png", "f.png", "image/png", 10, "2026/01/01/f.png",
            "/api/v1/attachments/x/download", AttachmentType.Image, uploadedBy, type, entityId, null, isPublic);

    private sealed class OwnsEverythingPolicy : IAttachmentEntityAccessPolicy
    {
        public Task<bool> CanAttachAsync(Guid entityId, AttachmentAccessContext caller, CancellationToken ct = default) =>
            Task.FromResult(caller.IsAuthenticated);

        public Task<bool> CanReadAsync(Guid entityId, AttachmentAccessContext caller, CancellationToken ct = default) =>
            Task.FromResult(caller.IsAuthenticated);
    }
}
