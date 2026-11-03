namespace Attachment.Infrastructure.Validation;

/// <summary>
/// Validation reads the stream several times (magic bytes, identify, scan, upload), so it must be seekable.
/// Form-file streams and MemoryStreams already are; anything else is buffered to a temp file that is
/// deleted when disposed.
/// </summary>
public sealed class SeekableUpload : IAsyncDisposable
{
    private readonly bool _owned;

    private SeekableUpload(Stream stream, bool owned)
    {
        Stream = stream;
        _owned = owned;
    }

    public Stream Stream { get; }

    public static async Task<SeekableUpload> CreateAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek)
        {
            source.Position = 0;
            return new SeekableUpload(source, owned: false);
        }

        var temp = new FileStream(
            Path.Combine(Path.GetTempPath(), $"upload_{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        await source.CopyToAsync(temp, cancellationToken);
        temp.Position = 0;
        return new SeekableUpload(temp, owned: true);
    }

    public ValueTask DisposeAsync() => _owned ? Stream.DisposeAsync() : ValueTask.CompletedTask;
}
