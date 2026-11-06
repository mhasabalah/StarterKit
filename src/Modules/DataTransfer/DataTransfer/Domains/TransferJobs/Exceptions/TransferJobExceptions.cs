namespace DataTransfer.Domains.TransferJobs.Exceptions;

// Every message comes from DataTransferResource (en/ar) through TransferText, in the current UI culture: the request
// culture in a handler, the requester's culture inside a job. Error codes stay stable for clients.

public sealed class TransferJobNotFoundException(Guid id)
    : NotFoundException(TransferText.Get("Transfer.Error.job_not_found", id));

public sealed class TransferDefinitionNotFoundException(string key)
    : NotFoundException(TransferText.Get("Transfer.Error.definition_not_found", key));

/// <summary>A job file that does not exist (no export result yet, no error report).</summary>
public sealed class TransferJobFileNotFoundException(TransferJobFileKind kind)
    : NotFoundException(TransferText.Get("Transfer.Error.file_not_found", TransferText.Get("Transfer.FileKind." + kind)));

/// <summary>An operation that the job's current state does not allow (400 <c>invalid_job_state</c>).</summary>
public sealed class InvalidTransferJobStateException : BadRequestException
{
    /// <param name="reason">Resx key suffix: <c>Transfer.State.&lt;reason&gt;</c>.</param>
    public InvalidTransferJobStateException(string reason)
        : base(TransferText.Get("Transfer.State." + reason)) => ErrorCode = "invalid_job_state";
}

/// <summary>An import of the same definition is already pending or running in this tenant (409).</summary>
public sealed class ImportAlreadyRunningException : ConflictException
{
    public ImportAlreadyRunningException(string definitionKey)
        : base(TransferText.Get("Transfer.Error.import_already_running", definitionKey)) => ErrorCode = "import_already_running";
}

/// <summary>
/// The file is not usable (400 / job failure with a stable code, e.g. <c>unsupported_format</c>). The message is
/// resolved from the resx key <c>Transfer.File.&lt;code&gt;</c> (or an explicit key) in the current UI culture.
/// </summary>
public sealed class InvalidTransferFileException : BadRequestException
{
    public InvalidTransferFileException(string code, params object?[] args)
        : this(code, "Transfer.File." + code, args)
    {
    }

    public InvalidTransferFileException(string code, string messageKey, object?[] args)
        : base(TransferText.Get(messageKey, args)) => ErrorCode = code;
}
