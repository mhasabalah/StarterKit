namespace Attachment.Infrastructure.Validation;

public interface IFileValidationService
{
    /// <summary>
    /// Validates an upload and determines its real type from the content. The stream must be seekable;
    /// it is left at position 0. Use <see cref="FileValidationResult.Detected"/> (never the client values)
    /// for the stored extension, content type and attachment type.
    /// </summary>
    Task<FileValidationResult> ValidateFileAsync(
        Stream fileStream,
        string fileName,
        string declaredContentType,
        long declaredFileSize,
        AttachmentType requestedType,
        CancellationToken cancellationToken = default);
}

/// <param name="SafeFileName">Client file name reduced to a safe display name (no path, no control chars).</param>
/// <param name="Detected">Server-determined type information; null when invalid.</param>
public record FileValidationResult(
    bool IsValid,
    List<string> Errors,
    string SafeFileName,
    DetectedFileType? Detected)
{
    public static FileValidationResult Invalid(string safeName, params string[] errors) =>
        new(false, [.. errors], safeName, null);
}

/// <param name="Extension">Stored extension, lower case with dot.</param>
/// <param name="ContentType">Stored/served content type.</param>
/// <param name="Size">Actual byte length of the stream.</param>
public record DetectedFileType(
    string Extension,
    string ContentType,
    AttachmentType Type,
    long Size,
    int? Width = null,
    int? Height = null);
