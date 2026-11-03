using Attachment.Infrastructure.Storage;
using Attachment.Tests.TestSupport;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Moq;

namespace Attachment.Tests.Unit;

/// <summary>Provider behaviour the reliability features rely on: listing, mtimes, streaming, known lengths.</summary>
public sealed class StorageProviderTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly LocalFileStorageService _local;

    public StorageProviderTests() => _local = TestServices.LocalStorage(_dir.Path);

    public void Dispose() => _dir.Dispose();

    private static MemoryStream Content(string text = "hello") => new(System.Text.Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Local_listing_returns_forward_slash_keys_with_modification_times()
    {
        await _local.UploadToPathAsync(Content(), "2026/11/02/a.txt", "text/plain");
        await _local.UploadToPathAsync(Content(), "temp/s/b.txt", "text/plain");

        var items = new List<StoredObjectInfo>();
        await foreach (var item in _local.ListAsync())
            items.Add(item);

        items.Select(i => i.StoragePath).Should().BeEquivalentTo(["2026/11/02/a.txt", "temp/s/b.txt"]);
        items.Should().OnlyContain(i => i.LastModifiedUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Local_move_resets_the_modification_time_so_the_orphan_scan_protects_it()
    {
        await _local.UploadToPathAsync(Content(), "temp/s/old.txt", "text/plain");
        File.SetLastWriteTimeUtc(_local.ResolveFullPath("temp/s/old.txt"), DateTime.UtcNow.AddDays(-5));

        await _local.MoveAsync("temp/s/old.txt", "products/p/moved.txt");

        File.GetLastWriteTimeUtc(_local.ResolveFullPath("products/p/moved.txt"))
            .Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Local_read_returns_a_file_stream_not_a_buffered_copy()
    {
        await _local.UploadToPathAsync(Content(), "a/b.txt", "text/plain");

        await using var stream = await _local.OpenReadAsync("a/b.txt");

        stream.Should().BeOfType<FileStream>();
    }

    [Fact]
    public async Task Upload_reports_the_number_of_bytes_written_from_the_current_position()
    {
        var source = Content("0123456789");
        source.Position = 4;

        var result = await _local.UploadToPathAsync(source, "a/partial.txt", "text/plain");

        result.FileSize.Should().Be(6);
        StorageStreams.RemainingLength(new MemoryStream(new byte[10])).Should().Be(10);
    }

    [Fact]
    public async Task Azure_container_is_created_once_and_retried_after_a_failure()
    {
        var calls = 0;
        var container = new Mock<BlobContainerClient>();
        container.Setup(c => c.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 1
                ? throw new RequestFailedException("transient")
                : Task.FromResult<Response<BlobContainerInfo>>(null!));
        container.Setup(c => c.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 1
                ? throw new RequestFailedException("transient")
                : Task.FromResult<Response<BlobContainerInfo>>(null!));

        var service = new Mock<BlobServiceClient>();
        service.Setup(s => s.GetBlobContainerClient(It.IsAny<string>())).Returns(container.Object);

        var gate = new AzureBlobContainerGate(service.Object, TestOptions.Wrap(TestOptions.Create()));

        await FluentActions.Awaiting(() => gate.EnsureCreatedAsync(default)).Should().ThrowAsync<RequestFailedException>();
        gate.IsEnsured.Should().BeFalse("a failed creation is retried by the next upload");

        await gate.EnsureCreatedAsync(default);
        await gate.EnsureCreatedAsync(default);
        await gate.EnsureCreatedAsync(default);

        gate.IsEnsured.Should().BeTrue();
        calls.Should().Be(2, "one failure, then exactly one successful creation; later uploads make no call");
    }
}
