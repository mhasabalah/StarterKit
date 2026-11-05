using System.Net;
using Shared.Core.Exceptions;

namespace Attachment.Domains.Attachments.Exceptions;

public class AttachmentNotFoundException(Guid id)
    : NotFoundException("Attachment", id);

/// <summary>The uploaded file failed validation (type, content, size, malware). 400.</summary>
public class InvalidFileException(string message) : BadRequestException(message);

/// <summary>A storage path is malformed or escapes the storage root (path traversal). 400.</summary>
public class InvalidStoragePathException(string storagePath)
    : BadRequestException($"Invalid storage path '{storagePath}'.");

/// <summary>The row exists but its file is missing in storage. 404.</summary>
public class StoredFileNotFoundException(string storagePath)
    : NotFoundException($"Stored file '{storagePath}' was not found.");

/// <summary>The storage provider failed (network, permissions, ...). 500 with a generic message.</summary>
public class FileStorageException(string message, Exception? innerException = null)
    : CustomException(message, innerException ?? new InvalidOperationException(message), HttpStatusCode.InternalServerError);

// Pending Attachment Exceptions
public class PendingAttachmentNotFoundException(Guid id)
    : NotFoundException("PendingAttachment", id);

public class SessionLimitExceededException(Guid sessionId, int maxUploads)
    : BadRequestException($"Session {sessionId} has reached the maximum allowed uploads ({maxUploads}).");

public class SessionExpiredException(Guid sessionId)
    : BadRequestException($"Session {sessionId} has expired.");

/// <summary>The session id was not issued by the server to the current caller. 403.</summary>
public class PendingSessionAccessDeniedException()
    : ForbiddenException("This upload session does not belong to you.");

public class DisplayOrderConflictException(int displayOrder)
    : ConflictException($"Display order {displayOrder} is already used in this session.");

/// <summary>The malware scanner flagged the upload. 400 <c>malware_detected</c>; the signature is logged, never returned.</summary>
public class MalwareDetectedException : BadRequestException
{
    public const string MalwareDetectedCode = "malware_detected";

    public MalwareDetectedException(string message) : base(message) => ErrorCode = MalwareDetectedCode;
}

/// <summary>
/// The upload could not be scanned (scanner down, timed out or errored) and scanning fails closed.
/// 503 <c>malware_scanner_unavailable</c>: the client may retry later.
/// </summary>
public class FileScanUnavailableException : CustomException
{
    public const string ScannerUnavailableCode = "malware_scanner_unavailable";

    public FileScanUnavailableException(string message, Exception innerException)
        : base(message, innerException, HttpStatusCode.ServiceUnavailable) => ErrorCode = ScannerUnavailableCode;
}
