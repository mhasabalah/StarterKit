namespace DataTransfer.Domains.TransferJobs.Models;

/// <summary>
/// One failed row of an import (the source of the error report). Stored per batch, in the batch's unit of work, so
/// a resumed job keeps the errors of the rows before its checkpoint. Not a navigation child of
/// <see cref="TransferJob"/>: a job can have thousands, and the job row is reloaded every batch.
/// </summary>
public class TransferJobError : Entity<Guid>, IMustHaveTenant
{
    public const int ColumnMaxLength = 100;
    public const int CodeMaxLength = 100;
    public const int MessageMaxLength = 1000;

    public Guid TenantId { get; set; }

    public Guid JobId { get; private set; }
    public long RowNumber { get; private set; }
    public string? Column { get; private set; }
    public string Code { get; private set; } = default!;
    public string Message { get; private set; } = default!;

    private TransferJobError()
    {
    }

    public static TransferJobError Create(Guid jobId, long rowNumber, ImportRowError error) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            JobId = jobId,
            RowNumber = rowNumber,
            Column = error.Column is null ? null : TextLimits.Clip(error.Column, ColumnMaxLength),
            Code = TextLimits.Clip(string.IsNullOrWhiteSpace(error.Code) ? "invalid" : error.Code, CodeMaxLength),
            Message = TextLimits.Clip(error.Message ?? string.Empty, MessageMaxLength)
        };
}
