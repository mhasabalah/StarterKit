using Attachment.Contracts.Attachments.Dtos;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.EventHandlers;
using Attachment.Domains.Attachments.Events;
using Attachment.Domains.Attachments.Models;
using Attachment.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Attachment.Tests.Unit;

/// <summary>
/// A new avatar replaces OLDER avatars only. Regression: two avatars associated in one batch used to delete each
/// other (each removed "every other avatar"), leaving the user with none.
/// </summary>
public sealed class AvatarReplacementTests
{
    private readonly Guid _userId = Guid.NewGuid();

    private AttachmentFile Avatar(Guid id) => AttachmentFile.Create(
        id, $"{id:N}.png", "me.png", "image/png", 10, $"t/{id:N}.png", $"/api/v1/attachments/{id}/download",
        AttachmentType.Image, _userId, EntityType.User, _userId);

    private async Task RunHandlersAsync(IReadOnlyList<AttachmentFile> stored, IEnumerable<AttachmentFile> raisedInOrder)
    {
        var repository = new Mock<IAttachmentRepository>();
        repository.Setup(r => r.GetByEntityAsync(EntityType.User, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored.Where(a => a.IsReady).ToList());
        var handler = new AttachmentUploadedEventHandler(repository.Object, Mock.Of<IAttachmentBackgroundJobs>(),
            TimeProvider.System, NullLogger<AttachmentUploadedEventHandler>.Instance);

        foreach (var avatar in raisedInOrder)
            await handler.Handle(new AttachmentUploadedEvent(avatar), CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Avatars_that_become_ready_together_leave_exactly_the_newest(bool reverseOrder)
    {
        // Same save → same CreatedAt, so the Id decides (as in GetUserAvatarHandler's ORDER BY).
        var low = Avatar(Guid.Parse("11111111-0000-0000-0000-000000000000"));
        var high = Avatar(Guid.Parse("f1111111-0000-0000-0000-000000000000"));
        var batch = reverseOrder ? new[] { high, low } : new[] { low, high };

        await RunHandlersAsync([low, high], batch);

        high.IsReady.Should().BeTrue("the newest avatar is the current one and must survive");
        low.IsReady.Should().BeFalse("the older one is replaced");
    }

    [Fact]
    public async Task A_new_avatar_replaces_the_previous_one()
    {
        var previous = Avatar(Guid.Parse("f0000000-0000-0000-0000-000000000000"));
        var replacement = Avatar(Guid.Parse("f9999999-0000-0000-0000-000000000000"));

        await RunHandlersAsync([previous, replacement], [replacement]);

        replacement.IsReady.Should().BeTrue();
        previous.IsReady.Should().BeFalse();
    }

    [Fact]
    public void Older_means_earlier_CreatedAt_then_lower_id_like_the_sql_order()
    {
        var a = Avatar(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var b = Avatar(Guid.Parse("00000000-0000-0000-0000-000000000002"));

        AttachmentUploadedEventHandler.IsOlder(a, b).Should().BeTrue();
        AttachmentUploadedEventHandler.IsOlder(b, a).Should().BeFalse();
        AttachmentUploadedEventHandler.IsOlder(a, a).Should().BeFalse();
    }
}
