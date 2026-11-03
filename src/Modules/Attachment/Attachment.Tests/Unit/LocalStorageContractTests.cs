using System.Web;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Storage;
using Attachment.Tests.TestSupport;

namespace Attachment.Tests.Unit;

/// <summary>
/// Storage contract for the Local provider, run on the current OS (CI runs Linux, dev runs Windows).
/// Storage paths are always '/'-separated keys.
/// </summary>
public sealed class LocalStorageContractTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly LocalFileStorageService _storage;

    public LocalStorageContractTests() => _storage = TestServices.LocalStorage(_dir.Path);

    public void Dispose() => _dir.Dispose();

    private static MemoryStream Content(string text = "hello") => new(System.Text.Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadAll(Stream s)
    {
        await using (s)
        using (var reader = new StreamReader(s))
            return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Upload_read_exists_delete_round_trip_with_nested_forward_slash_paths()
    {
        const string path = "2026/11/01/abc.txt";

        var stored = await _storage.UploadToPathAsync(Content(), path, "text/plain");

        stored.StoragePath.Should().Be(path);
        stored.FileSize.Should().Be(5);
        File.Exists(Path.Combine(_dir.Path, "2026", "11", "01", "abc.txt")).Should().BeTrue();
        (await _storage.ExistsAsync(path)).Should().BeTrue();
        (await ReadAll(await _storage.OpenReadAsync(path))).Should().Be("hello");

        await _storage.DeleteAsync(path);

        (await _storage.ExistsAsync(path)).Should().BeFalse();
        _dir.AllFiles().Should().BeEmpty("delete must really remove the file (it was a no-op on Linux)");
    }

    [Fact]
    public async Task Copy_and_move_create_target_folders()
    {
        await _storage.UploadToPathAsync(Content("x"), "temp/s1/a.txt", "text/plain");

        await _storage.CopyAsync("temp/s1/a.txt", "products/p1/copy.txt");
        await _storage.MoveAsync("temp/s1/a.txt", "products/p1/moved.txt");

        (await _storage.ExistsAsync("temp/s1/a.txt")).Should().BeFalse();
        (await _storage.ExistsAsync("products/p1/copy.txt")).Should().BeTrue();
        (await _storage.ExistsAsync("products/p1/moved.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Batch_delete_reports_success_and_ignores_missing_files()
    {
        await _storage.UploadToPathAsync(Content(), "a/1.txt", "text/plain");

        var result = await _storage.DeleteBatchAsync(["a/1.txt", "a/missing.txt"]);

        result.AllSucceeded.Should().BeTrue();
        _dir.AllFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Batch_delete_reports_invalid_paths_as_failed()
    {
        var result = await _storage.DeleteBatchAsync(["../outside.txt"]);

        result.AllSucceeded.Should().BeFalse();
        result.Failed.Should().Contain("../outside.txt");
    }

    [Fact]
    public async Task Opening_a_missing_file_is_a_not_found_domain_exception()
    {
        await FluentActions.Awaiting(() => _storage.OpenReadAsync("nope/file.txt"))
            .Should().ThrowAsync<StoredFileNotFoundException>();
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    [InlineData("a/./b.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("a\\..\\..\\escape.txt")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("a//b.txt")]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a/b./c")]
    public async Task Path_traversal_and_malformed_paths_are_rejected(string path)
    {
        FluentActions.Invoking(() => _storage.ResolveFullPath(path)).Should().Throw<InvalidStoragePathException>();

        await FluentActions.Awaiting(() => _storage.UploadToPathAsync(Content(), path, "text/plain"))
            .Should().ThrowAsync<InvalidStoragePathException>();

        Directory.GetFiles(Path.GetDirectoryName(_dir.Path)!, "escape.txt", SearchOption.AllDirectories)
            .Should().BeEmpty();
    }

    [Fact]
    public void Resolved_paths_stay_inside_the_upload_root()
    {
        var full = _storage.ResolveFullPath("x/y/z.bin");

        full.Should().StartWith(_storage.UploadRoot);
        full.Should().EndWith(Path.Combine("x", "y", "z.bin"));
    }

    [Fact]
    public async Task Signed_read_url_round_trips_and_rejects_tampering_and_expiry()
    {
        var url = await _storage.GetReadUrlAsync("a/b.pdf", TimeSpan.FromMinutes(5),
            new ReadUrlOptions("report.pdf", "application/pdf", Inline: false));

        url.Should().StartWith(LocalReadUrlSigner.RoutePath + "?");
        var q = HttpUtility.ParseQueryString(new Uri("http://x" + url).Query);
        var signer = new LocalReadUrlSigner(TestServices.SigningKeys, TestOptions.Wrap(TestOptions.Create()));

        var ok = signer.Verify(q["p"], q["e"], q["n"], q["t"], q["i"], q["s"], DateTimeOffset.UtcNow);
        ok.Should().NotBeNull();
        ok!.StoragePath.Should().Be("a/b.pdf");
        ok.Inline.Should().BeFalse();

        signer.Verify("a/other.pdf", q["e"], q["n"], q["t"], q["i"], q["s"], DateTimeOffset.UtcNow)
            .Should().BeNull("the path is signed");
        signer.Verify(q["p"], q["e"], q["n"], q["t"], "1", q["s"], DateTimeOffset.UtcNow)
            .Should().BeNull("the disposition is signed");
        signer.Verify(q["p"], q["e"], q["n"], q["t"], q["i"], q["s"], DateTimeOffset.UtcNow.AddMinutes(10))
            .Should().BeNull("the URL expired");
    }
}
