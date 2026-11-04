using Attachment.Contracts.Attachments.Dtos;
using Attachment.Domains.Attachments.Consumers;
using Attachment.Domains.Attachments.Models;
using Attachment.Infrastructure.BackgroundJobs;
using Attachment.Tests.TestSupport;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.BackgroundJobs;
using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Attachment.Tests.Integration;

/// <summary>
/// Account erasure on the Attachment side: the user's personal files (avatar) are hidden and queued for
/// the normal file-deletion job; tenant-owned files and other users' files are untouched; redelivery is harmless.
/// </summary>
[Collection(AttachmentHostCollection.Name)]
public sealed class UserDeletedConsumerTests : IDisposable
{
    private readonly AttachmentHostFixture _host;

    public UserDeletedConsumerTests(AttachmentHostFixture host)
    {
        _host = host;
        _host.Reset();
    }

    public void Dispose() => _host.Reset();

    private async Task<Guid> SeedAsync(Guid owner, AttachmentStatus status = AttachmentStatus.Ready, bool avatar = false,
        EntityType? entityType = null, Guid? entityId = null)
    {
        var id = Guid.NewGuid();
        var content = TestFiles.Png();
        var path = $"seed/{id:N}.png";
        await _host.Storage.Inner.UploadToPathAsync(new MemoryStream(content), path, "image/png");

        var attachment = AttachmentFile.StartUpload(id, $"{id:N}.png", "seed.png", "image/png", content.Length, path,
            $"/api/v1/attachments/{id}/download", AttachmentType.Image, owner,
            avatar ? EntityType.User : entityType, avatar ? owner : entityId, null, true);
        if (status != AttachmentStatus.Uploading)
            attachment.MarkReady(content.Length, null, null, null);

        await _host.DbAsync(async db =>
        {
            db.Attachments.Add(attachment);
            return await db.SaveChangesAsync();
        });
        return id;
    }

    private Task<AttachmentFile?> RowAsync(Guid id) =>
        _host.DbAsync(db => db.Attachments.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id));

    private Task ConsumeAsync(Guid userId, ITenantStore? tenantStore = null) =>
        _host.InScopeAsync(async sp =>
        {
            var runner = new TenantJobRunner(new DefaultTenantStore(), sp.GetRequiredService<IUnitOfWorkRunner>(),
                NullLogger<TenantJobRunner>.Instance);
            var consumer = new UserDeletedAttachmentConsumer(runner, tenantStore ?? new DefaultTenantStore(),
                sp.GetRequiredService<IUnitOfWorkRunner>(),
                sp.GetRequiredService<IAttachmentBackgroundJobs>(), _host.Clock,
                NullLogger<UserDeletedAttachmentConsumer>.Instance);

            var context = new Mock<ConsumeContext<UserDeletedIntegrationEvent>>();
            context.SetupGet(c => c.Message).Returns(new UserDeletedIntegrationEvent { UserId = userId });
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            await consumer.Consume(context.Object);
        });

    [Fact]
    public async Task Consume_HidesThePersonalFiles_AndQueuesTheFileDeletionJob()
    {
        var user = Guid.NewGuid();
        var avatar = await SeedAsync(user, avatar: true);
        var pendingAvatar = await SeedAsync(user, AttachmentStatus.Uploading, avatar: true);

        await ConsumeAsync(user);

        foreach (var id in new[] { avatar, pendingAvatar })
        {
            var row = await RowAsync(id);
            row!.Status.Should().Be(AttachmentStatus.PendingDeletion);
            row.IsDeleted.Should().BeTrue();
        }

        var job = _host.Jobs.Of<DeleteAttachmentFilesJob>(nameof(DeleteAttachmentFilesJob.ExecuteAsync))
            .Should().ContainSingle().Subject;
        ((Guid[])job.Args[0]).Should().BeEquivalentTo([avatar, pendingAvatar]);
    }

    [Fact]
    public async Task Consume_KeepsTenantOwnedFilesTheUserMerelyUploaded()
    {
        var user = Guid.NewGuid();
        var avatar = await SeedAsync(user, avatar: true);
        var productImage = await SeedAsync(user, entityType: EntityType.Product, entityId: Guid.NewGuid());
        var proof = await SeedAsync(user);

        await ConsumeAsync(user);

        (await RowAsync(avatar))!.Status.Should().Be(AttachmentStatus.PendingDeletion);
        (await RowAsync(productImage))!.Status.Should().Be(AttachmentStatus.Ready);
        (await RowAsync(proof))!.Status.Should().Be(AttachmentStatus.Ready);
    }

    [Fact]
    public async Task Consume_WhenATenantIsMigrating_ProcessesTheOthers_ThenThrowsSoTheBusRetries()
    {
        var user = Guid.NewGuid();
        var avatar = await SeedAsync(user, avatar: true);
        var store = new Mock<ITenantStore>();
        store.Setup(s => s.GetServiceableTenantsAsync(It.IsAny<CancellationToken>()))
            .Returns(new DefaultTenantStore().GetServiceableTenantsAsync());
        store.Setup(s => s.GetMigratingTenantsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([TenantDefaults.DefaultTenant with { Id = Guid.NewGuid(), Identifier = "moving", Status = TenantStatus.Migrating }]);

        var act = () => ConsumeAsync(user, store.Object);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*migrating*");
        (await RowAsync(avatar))!.Status.Should().Be(AttachmentStatus.PendingDeletion, "the serviceable tenants were still processed");
    }

    [Fact]
    public async Task Consume_LeavesOtherUsersFilesAlone()
    {
        var user = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SeedAsync(user, avatar: true);
        var kept = await SeedAsync(other, avatar: true);

        await ConsumeAsync(user);

        (await RowAsync(kept))!.Status.Should().Be(AttachmentStatus.Ready);
    }

    [Fact]
    public async Task Consume_Twice_QueuesNothingTheSecondTime()
    {
        var user = Guid.NewGuid();
        await SeedAsync(user, avatar: true);

        await ConsumeAsync(user);
        _host.Jobs.Clear();
        await ConsumeAsync(user);

        _host.Jobs.Of<DeleteAttachmentFilesJob>(nameof(DeleteAttachmentFilesJob.ExecuteAsync)).Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_ForAUserWithoutFiles_DoesNothing()
    {
        await ConsumeAsync(Guid.NewGuid());

        _host.Jobs.All.Should().BeEmpty();
    }
}
