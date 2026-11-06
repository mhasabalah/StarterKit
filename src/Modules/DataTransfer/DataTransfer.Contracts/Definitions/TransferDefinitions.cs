using Microsoft.Extensions.DependencyInjection;

namespace DataTransfer.Contracts.Definitions;

/// <summary>One column of an import/export definition.</summary>
/// <param name="Name">Stable machine name (e.g. <c>sku</c>); also accepted as a header on import.</param>
/// <param name="Header">Localized header written to templates and exports; both languages are accepted on import.</param>
/// <param name="Required">Import: the header must be present (the cell rules are the definition's).</param>
/// <param name="Sample">Example value written to the template's sample row.</param>
/// <param name="Description">Format hint shown in the definition list (allowed values, units, ...).</param>
/// <param name="Kind">How exports and templates write the column to XLSX (CSV is always text).</param>
public sealed record TransferColumn(
    string Name,
    LocalizedText Header,
    bool Required = false,
    string? Sample = null,
    LocalizedText? Description = null,
    TransferColumnKind Kind = TransferColumnKind.Text)
{
    /// <summary>The header in <paramref name="culture"/> (<c>ar</c> = Arabic, anything else = English).</summary>
    public string HeaderFor(string culture) => TransferCulture.IsArabic(culture) ? Header.Ar : Header.En;
}

/// <summary>The two cultures of the template: everything Arabic-like is <c>ar</c>, the rest <c>en</c>.</summary>
public static class TransferCulture
{
    public const string Arabic = "ar";
    public const string English = "en";

    public static string Normalize(string? culture) => IsArabic(culture) ? Arabic : English;

    public static bool IsArabic(string? culture) =>
        culture is not null && culture.StartsWith(Arabic, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Common part of import and export definitions.</summary>
public interface ITransferDefinition
{
    /// <summary>Stable key, <c>&lt;module&gt;.&lt;entity&gt;</c> (e.g. <c>catalog.products</c>). Never rename.</summary>
    string Key { get; }

    LocalizedText DisplayName { get; }

    /// <summary>
    /// Tenant permission (a <c>TenantPermissions</c> value) the caller must ALSO hold, on top of the DataTransfer
    /// import/export permission. Checked when the job is created.
    /// </summary>
    string RequiredPermission { get; }

    /// <summary>Columns in file order.</summary>
    IReadOnlyList<TransferColumn> Columns { get; }
}

/// <summary>
/// Implemented by the module that OWNS the data (DataTransfer never references it) and registered with
/// <see cref="TransferDefinitionServiceCollectionExtensions.AddImportDefinition{TDefinition}"/>. Resolved per batch
/// from a fresh unit-of-work scope, so it may inject the owning module's repositories and DbContext.
/// <para><b>Contract:</b></para>
/// <list type="bullet">
/// <item>Return exactly one <see cref="ImportRowResult"/> per input record.</item>
/// <item>Validate every row (types, invariants, references) and report problems as row errors, never by throwing.
/// A thrown exception fails the whole batch; the job retries it.</item>
/// <item>When <see cref="ImportContext.DryRun"/> is true, perform every check but save NOTHING.</item>
/// <item>Be idempotent: upsert by a natural key, so a replayed batch (crash before the checkpoint) changes nothing.</item>
/// <item>Save once per batch through the owning module's repository (aggregate methods, so invariants and domain
/// events stay intact). There is no HTTP user: act as <see cref="ImportContext.RequestedBy"/>.</item>
/// <item>The job runs the call in the requester's CURRENT branch scope (EF branch filters apply as in their requests)
/// and with <c>CultureInfo.CurrentUICulture</c> set to <see cref="ImportContext.Culture"/>, so localized messages are
/// in their language.</item>
/// </list>
/// </summary>
public interface IImportDefinition : ITransferDefinition
{
    Task<IReadOnlyList<ImportRowResult>> ApplyBatchAsync(
        IReadOnlyList<ImportRecord> records,
        ImportContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Implemented by the module that owns the data; registered with
/// <see cref="TransferDefinitionServiceCollectionExtensions.AddExportDefinition{TDefinition}"/>.
/// <para>
/// Stream rows (<c>AsNoTracking</c>, SQL projection, <c>AsAsyncEnumerable</c>); never materialize the whole set. Each
/// row is the cell values in <see cref="ITransferDefinition.Columns"/> order, formatted with the invariant culture.
/// </para>
/// </summary>
public interface IExportDefinition : ITransferDefinition
{
    IAsyncEnumerable<IReadOnlyList<string?>> StreamAsync(ExportContext context, CancellationToken cancellationToken);
}

/// <summary>One data row of an import file.</summary>
/// <param name="RowNumber">Row number as the user sees it in the file (the header is row 1, the first data row is 2).</param>
/// <param name="Values">Cell values keyed by <see cref="TransferColumn.Name"/>, trimmed; empty cells are null.</param>
public sealed record ImportRecord(long RowNumber, IReadOnlyDictionary<string, string?> Values)
{
    public string? Get(string column) => Values.TryGetValue(column, out var value) ? value : null;
}

/// <param name="JobId">The transfer job.</param>
/// <param name="RequestedBy">User who started the job: the actor of every change.</param>
/// <param name="DryRun">Check everything, save nothing.</param>
/// <param name="Culture">UI culture of the requester (<c>en</c> / <c>ar</c>) for row messages.</param>
public sealed record ImportContext(Guid JobId, Guid RequestedBy, bool DryRun, string Culture);

/// <param name="Filter">Optional definition-specific filter (validated by the definition; unknown keys are ignored).</param>
public sealed record ExportContext(
    Guid JobId,
    Guid RequestedBy,
    IReadOnlyDictionary<string, string> Filter,
    string Culture);

/// <param name="Column">Column the error is about, or null for the whole row.</param>
/// <param name="Code">Machine-readable code (e.g. <c>required</c>, <c>invalid_number</c>, <c>sku_taken</c>).</param>
/// <param name="Message">Localized, human-readable message.</param>
public sealed record ImportRowError(string? Column, string Code, string Message);

public sealed record ImportRowResult(long RowNumber, ImportRowOutcome Outcome, IReadOnlyList<ImportRowError> Errors)
{
    public static ImportRowResult Created(long row) => new(row, ImportRowOutcome.Created, []);

    public static ImportRowResult Updated(long row) => new(row, ImportRowOutcome.Updated, []);

    public static ImportRowResult Unchanged(long row) => new(row, ImportRowOutcome.Unchanged, []);

    public static ImportRowResult Failed(long row, IReadOnlyList<ImportRowError> errors) =>
        new(row, ImportRowOutcome.Failed, errors);

    public static ImportRowResult Failed(long row, string? column, string code, string message) =>
        new(row, ImportRowOutcome.Failed, [new ImportRowError(column, code, message)]);
}

public static class TransferDefinitionServiceCollectionExtensions
{
    /// <summary>Registers an import definition (scoped). Keys must be unique (checked by the registry at startup).</summary>
    public static IServiceCollection AddImportDefinition<TDefinition>(this IServiceCollection services)
        where TDefinition : class, IImportDefinition
    {
        services.AddScoped<IImportDefinition, TDefinition>();
        return services;
    }

    /// <summary>Registers an export definition (scoped). Keys must be unique (checked by the registry at startup).</summary>
    public static IServiceCollection AddExportDefinition<TDefinition>(this IServiceCollection services)
        where TDefinition : class, IExportDefinition
    {
        services.AddScoped<IExportDefinition, TDefinition>();
        return services;
    }
}
