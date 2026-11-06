using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Attachments.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace DataTransfer.Engine;

/// <summary>A local scratch file, deleted on dispose (best effort).</summary>
public sealed class TempFile : IAsyncDisposable
{
    private TempFile(string path) => Path = path;

    public string Path { get; }

    public static TempFile Create(string extension)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "starterkit-datatransfer");
        Directory.CreateDirectory(directory);
        return new TempFile(System.IO.Path.Combine(directory, $"{Guid.NewGuid():N}{extension}"));
    }

    public FileStream OpenWrite() =>
        new(Path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // Still open elsewhere (should not happen): the OS temp cleanup removes it later.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Moves job files between the Attachment module (blob storage, content validation, malware scan) and local scratch
/// files the streaming readers/writers work on. Each call is its own unit of work in the ambient tenant.
/// </summary>
public sealed class TransferFileStore(IUnitOfWorkRunner unitOfWork)
{
    /// <summary>Copies a stored attachment to a scratch file, refusing more than <paramref name="maxBytes"/>.</summary>
    public async Task<TempFile> DownloadAsync(Guid attachmentId, string extension, long maxBytes,
        CancellationToken cancellationToken)
    {
        var file = TempFile.Create(extension);
        try
        {
            await unitOfWork.RunAsync(async (services, ct) =>
            {
                var sender = services.GetRequiredService<ISender>();
                var content = await sender.Send(new OpenAttachmentContentQuery(attachmentId), ct);
                await using var source = content.Content;
                await using var target = file.OpenWrite();
                await CopyLimitedAsync(source, target, maxBytes, ct);
            }, cancellationToken);
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>Stores a produced file as a private attachment of the job; returns the attachment id.</summary>
    public Task<Guid> UploadAsync(Guid jobId, TempFile file, string fileName, string contentType, Guid uploadedBy,
        CancellationToken cancellationToken) =>
        unitOfWork.RunAsync(async (services, ct) =>
        {
            var sender = services.GetRequiredService<ISender>();
            await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous);
            var result = await sender.Send(new UploadAttachmentCommand(
                fileName, contentType, stream.Length, stream, AttachmentType.Document,
                EntityType.DataTransferJob, jobId, IsPublic: false, GenerateThumbnail: false, UploadedBy: uploadedBy), ct);
            return result.Id;
        }, cancellationToken);

    public static async Task CopyLimitedAsync(Stream source, Stream target, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidTransferFileException("file_too_large", maxBytes);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
