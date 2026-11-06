using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace DataTransfer.Engine;

/// <summary>Turns raw file rows into <see cref="ImportRecord"/>s keyed by column name.</summary>
public static class ImportRecords
{
    /// <summary>
    /// Builds the record of <paramref name="row"/>. A cell longer than <paramref name="maxCellLength"/> fails the row
    /// here (code <c>cell_too_long</c>) and the row never reaches the definition.
    /// </summary>
    public static (ImportRecord Record, ImportRowError? Error) Build(HeaderMap map, TabularRow row, int maxCellLength)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        ImportRowError? error = null;
        foreach (var (index, column) in map.Columns)
        {
            var value = index < row.Cells.Length ? CellText.Clean(row.Cells[index]) : null;
            if (value is { Length: var length } && length > maxCellLength)
            {
                error ??= new ImportRowError(column, "cell_too_long", TransferText.Get("Transfer.Row.cell_too_long", maxCellLength));
                value = null;
            }

            values[column] = value;
        }

        return (new ImportRecord(row.RowNumber, values), error);
    }
}

/// <summary>Writes the error report (CSV: row, column, code, message) from the stored row errors.</summary>
public static class ErrorReport
{
    public static async Task WriteHeaderAsync(TabularWriter writer, string culture, CancellationToken cancellationToken) =>
        await writer.WriteRowAsync(TransferCulture.IsArabic(culture)
            ? ["الصف", "العمود", "الرمز", "الرسالة"]
            : ["Row", "Column", "Code", "Message"], cancellationToken);

    public static Task WriteAsync(TabularWriter writer, TransferJobError error, CancellationToken cancellationToken) =>
        writer.WriteRowAsync(
            [error.RowNumber.ToString(CultureInfo.InvariantCulture), error.Column, error.Code, error.Message],
            cancellationToken);
}

/// <summary>Metrics of the DataTransfer module (meter <c>StarterKit.DataTransfer</c>).</summary>
public sealed class DataTransferMetrics : IDisposable
{
    public const string MeterName = "StarterKit.DataTransfer";

    private readonly Meter _meter;
    private readonly Counter<long> _rows;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _jobs;

    public DataTransferMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        _rows = _meter.CreateCounter<long>("datatransfer.rows", "{row}", "Rows processed, by definition and outcome.");
        _duration = _meter.CreateHistogram<double>("datatransfer.job.duration", "s", "Duration of finished jobs.");
        _jobs = _meter.CreateCounter<long>("datatransfer.jobs.finished", "{job}", "Finished jobs, by kind and status.");
    }

    public void RecordRows(string definition, BatchCounts counts)
    {
        Add(definition, "created", counts.Created);
        Add(definition, "updated", counts.Updated);
        Add(definition, "unchanged", counts.Unchanged);
        Add(definition, "failed", counts.Failed);
    }

    public void RecordFinished(TransferJob job)
    {
        var tags = new TagList
        {
            { "kind", job.Kind.ToString() },
            { "definition", job.DefinitionKey },
            { "status", job.Status.ToString() }
        };
        _jobs.Add(1, tags);
        if (job is { StartedAt: { } started, FinishedAt: { } finished })
            _duration.Record((finished - started).TotalSeconds, tags);
    }

    public void Dispose() => _meter.Dispose();

    private void Add(string definition, string outcome, long count)
    {
        if (count > 0)
            _rows.Add(count, new KeyValuePair<string, object?>("definition", definition),
                new KeyValuePair<string, object?>("outcome", outcome));
    }
}
