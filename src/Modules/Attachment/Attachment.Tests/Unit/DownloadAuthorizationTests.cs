using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Attachments.Queries;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.Features.DownloadAttachment;
using Attachment.Domains.Attachments.Features.GetAttachmentReadUrl;
using Attachment.Domains.Attachments.Models;
using Attachment.Infrastructure.Storage;
using Attachment.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Exceptions;

namespace Attachment.Tests.Unit;

public sealed class DownloadAuthorizationTests : IDisposable
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private readonly TempDirectory _dir = new();
    private readonly LocalFileStorageService _local;

    public DownloadAuthorizationTests() => _local = TestServices.LocalStorage(_dir.Path);

    public void Dispose() => _dir.Dispose();

    private async Task<AttachmentFile> Stored(bool isPublic, EntityType? type = null, Guid? entityId = null, string contentType = "application/pdf")
    {
        var attachment = AttachmentFile.Create(Guid.NewGuid(), "x.pdf", "report.pdf", contentType, 5,
            $"2026/11/01/{Guid.NewGuid():N}.pdf", "/u", AttachmentType.Document, OwnerId, type, entityId, null, isPublic);
        await _local.UploadToPathAsync(new MemoryStream("%PDF-"u8.ToArray()), attachment.StoragePath, contentType);
        return attachment;
    }

    private DownloadAttachmentHandler Handler(AttachmentFile attachment, Caller caller, IStorageService? storage = null)
    {
        var repo = new Mock<IAttachmentRepository>();
        repo.Setup(r => r.GetReadOnlyByIdAsync(attachment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(attachment);
        storage ??= _local;
        var readUrls = new AttachmentReadUrlFactory(storage, TestOptions.Wrap(TestOptions.Create()));
        return new DownloadAttachmentHandler(repo.Object, TestServices.Access(caller), readUrls, storage,
            NullLogger<DownloadAttachmentHandler>.Instance);
    }

    public static TheoryData<bool, string, string> Matrix => new()
    {
        // isPublic, caller, expected (ok | 401 | 403)
        { true, "anonymous", "ok" },
        { true, "other", "ok" },
        { false, "anonymous", "401" },
        { false, "other", "403" },
        { false, "owner", "ok" },
        { false, "admin", "ok" },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Download_access_matrix(bool isPublic, string who, string expected)
    {
        var attachment = await Stored(isPublic);
        var caller = who switch
        {
            "owner" => Caller.User(OwnerId),
            "other" => Caller.User(Guid.NewGuid()),
            "admin" => Caller.Admin(),
            _ => Caller.Anonymous()
        };

        var act = () => Handler(attachment, caller).Handle(new DownloadAttachmentQuery(attachment.Id), default);

        switch (expected)
        {
            case "ok":
                var result = await act();
                result.Content.Should().NotBeNull("Local storage is streamed by the API");
                await result.Content!.DisposeAsync();
                break;
            case "401":
                await act.Should().ThrowAsync<UnauthorizedException>();
                break;
            default:
                await act.Should().ThrowAsync<ForbiddenException>();
                break;
        }
    }

    [Fact]
    public async Task Entity_policy_grants_read_of_a_private_file_uploaded_by_someone_else()
    {
        var userId = Guid.NewGuid();
        var attachment = await Stored(isPublic: false, EntityType.User, userId); // uploaded by OwnerId for userId

        var result = await Handler(attachment, Caller.User(userId)).Handle(new DownloadAttachmentQuery(attachment.Id), default);

        result.Content.Should().NotBeNull();
        await result.Content!.DisposeAsync();
    }

    [Fact]
    public async Task Pdf_is_served_as_attachment_and_images_inline()
    {
        var pdf = await Stored(isPublic: true);
        var png = await Stored(isPublic: true, contentType: "image/png");

        var pdfResult = await Handler(pdf, Caller.Anonymous()).Handle(new DownloadAttachmentQuery(pdf.Id), default);
        var pngResult = await Handler(png, Caller.Anonymous()).Handle(new DownloadAttachmentQuery(png.Id), default);

        pdfResult.Inline.Should().BeFalse();
        pngResult.Inline.Should().BeTrue();
        FileResponseHeaders.ContentDisposition("report.pdf", false).Should().StartWith("attachment");
        await pdfResult.Content!.DisposeAsync();
        await pngResult.Content!.DisposeAsync();
    }

    [Fact]
    public async Task Cloud_providers_get_a_short_lived_redirect_after_the_access_check()
    {
        var attachment = await Stored(isPublic: false);
        var cloud = new Mock<IStorageService>();
        cloud.SetupGet(s => s.ServesThroughApplication).Returns(false);
        cloud.Setup(s => s.GetReadUrlAsync(attachment.StoragePath, TimeSpan.FromMinutes(5),
                It.Is<ReadUrlOptions>(o => !o.Inline && o.FileName == "report.pdf"), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://bucket.example/signed");

        var result = await Handler(attachment, Caller.User(OwnerId), cloud.Object)
            .Handle(new DownloadAttachmentQuery(attachment.Id), default);

        result.RedirectUrl.Should().Be("https://bucket.example/signed");

        await FluentActions.Awaiting(() => Handler(attachment, Caller.User(Guid.NewGuid()), cloud.Object)
                .Handle(new DownloadAttachmentQuery(attachment.Id), default))
            .Should().ThrowAsync<ForbiddenException>();
        cloud.Verify(s => s.GetReadUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<ReadUrlOptions>(),
            It.IsAny<CancellationToken>()), Times.Once, "no URL may be signed for a denied caller");
    }

    [Fact]
    public async Task Missing_thumbnail_is_404()
    {
        var attachment = await Stored(isPublic: true);

        await FluentActions.Awaiting(() => Handler(attachment, Caller.Anonymous())
                .Handle(new DownloadAttachmentQuery(attachment.Id, Thumbnail: true), default))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Trusted_read_url_query_returns_a_fresh_signed_url()
    {
        var attachment = await Stored(isPublic: false);
        var repo = new Mock<IAttachmentRepository>();
        repo.Setup(r => r.GetReadOnlyByIdAsync(attachment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(attachment);
        var handler = new GetAttachmentReadUrlHandler(repo.Object,
            new AttachmentReadUrlFactory(_local, TestOptions.Wrap(TestOptions.Create())));

        var result = await handler.Handle(new GetAttachmentReadUrlQuery(attachment.Id), default);

        result.Url.Should().Contain("s=").And.Contain("e=");
        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30));
        result.FileName.Should().Be("report.pdf");
    }
}
