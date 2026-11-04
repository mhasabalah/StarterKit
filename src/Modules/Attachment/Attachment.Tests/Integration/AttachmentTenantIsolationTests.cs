using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Attachments.Queries;
using Attachment.Infrastructure.Storage;
using Attachment.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using static Attachment.Tests.Integration.AttachmentMultiTenantFixture;

namespace Attachment.Tests.Integration;

/// <summary>
/// Two tenants, real migrations, real Postgres row-level security (non-superuser connection), real module handlers.
/// A tenant can never read, list or delete another tenant's files - even with a guessed id and even as an admin of
/// its own tenant - and blobs are stored under the owning tenant's root.
/// </summary>
[Collection(AttachmentMultiTenantCollection.Name)]
public sealed class AttachmentTenantIsolationTests(AttachmentMultiTenantFixture fx)
{
    private Task<UploadAttachmentResult> UploadIn(TenantInfo tenant, Caller caller, EntityType? type = null, Guid? entityId = null)
    {
        var png = TestFiles.Png();
        return fx.SendAsync(tenant, caller, new UploadAttachmentCommand("photo.png", "image/png", png.Length,
            new MemoryStream(png), AttachmentType.Other, type, entityId, IsPublic: false, GenerateThumbnail: false));
    }

    [Fact]
    public async Task Owner_tenant_can_read_its_upload_and_the_blob_lives_under_its_tenant_root()
    {
        var owner = Caller.User(Guid.NewGuid());
        var uploaded = await UploadIn(TenantA, owner);

        var read = await fx.SendAsync(TenantA, owner, new GetAttachmentByIdQuery(uploaded.Id));

        read.Attachment.Id.Should().Be(uploaded.Id);
        var row = await fx.InTenantDbAsync(TenantA, db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == uploaded.Id));
        row.TenantId.Should().Be(TenantA.Id, "TenantEntityInterceptor stamps the resolved tenant");
        row.StoragePath.Should().StartWith(StoragePaths.TenantRoot(TenantA.Id),
            "tenant purge/export/move work on the tenant's blob prefix");
    }

    [Fact]
    public async Task Another_tenant_cannot_read_an_attachment_by_its_id_even_as_admin()
    {
        var uploaded = await UploadIn(TenantA, Caller.User(Guid.NewGuid()));

        var act = () => fx.SendAsync(TenantB, Caller.Admin(), new GetAttachmentByIdQuery(uploaded.Id));

        await act.Should().ThrowAsync<NotFoundException>("tenant B must not even learn the attachment exists");
    }

    [Fact]
    public async Task Another_tenant_cannot_delete_an_attachment_and_the_owner_still_has_it()
    {
        var owner = Caller.User(Guid.NewGuid());
        var uploaded = await UploadIn(TenantA, owner);

        var act = () => fx.SendAsync(TenantB, Caller.Admin(), new DeleteAttachmentCommand(uploaded.Id));

        await act.Should().ThrowAsync<NotFoundException>();
        (await fx.SendAsync(TenantA, owner, new GetAttachmentByIdQuery(uploaded.Id))).Attachment.Id.Should().Be(uploaded.Id);
    }

    [Fact]
    public async Task Entity_listings_are_scoped_to_the_current_tenant()
    {
        var userId = Guid.NewGuid();
        await UploadIn(TenantA, Caller.User(userId), EntityType.User, userId);

        var fromA = await fx.SendAsync(TenantA, Caller.Admin(), new GetAttachmentsByEntityQuery(EntityType.User, userId));
        var fromB = await fx.SendAsync(TenantB, Caller.Admin(), new GetAttachmentsByEntityQuery(EntityType.User, userId));

        fromA.Attachments.Should().ContainSingle();
        fromB.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task Row_level_security_hides_other_tenants_rows_from_raw_sql_through_the_module_connection()
    {
        await UploadIn(TenantA, Caller.User(Guid.NewGuid()));
        await UploadIn(TenantB, Caller.User(Guid.NewGuid()));

        var visibleToA = await fx.CountRowsAsync(TenantA, "attachments");
        var visibleToB = await fx.CountRowsAsync(TenantB, "attachments");
        var visibleToHost = await fx.CountRowsAsync(null, "attachments");

        visibleToA.Should().BeGreaterThan(0);
        visibleToB.Should().BeGreaterThan(0);
        visibleToHost.Should().Be(visibleToA + visibleToB,
            "raw SQL skips EF filters, so only Postgres RLS can split the rows per tenant - the host scope sees all");
    }
}
