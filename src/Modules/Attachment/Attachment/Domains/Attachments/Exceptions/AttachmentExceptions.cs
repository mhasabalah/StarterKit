using Shared.Core.Exceptions;

namespace Attachment.Domains.Attachments.Exceptions;

public class AttachmentNotFoundException(Guid id)
    : NotFoundException("Attachment", id);

public class InvalidFileException(string message) : BadRequestException(message);

public class FileStorageException(string message, Exception? innerException = null)
    : Exception(message, innerException);

// Pending Attachment Exceptions
public class PendingAttachmentNotFoundException(Guid id)
    : NotFoundException("PendingAttachment", id);

public class SessionLimitExceededException(Guid sessionId, int maxUploads)
    : BadRequestException($"Session {sessionId} has exceeded the maximum allowed uploads ({maxUploads}).");

public class SessionExpiredException(Guid sessionId)
    : BadRequestException($"Session {sessionId} has expired.");

public class UploadRateLimitExceededException(string message)
    : BadRequestException(message);
