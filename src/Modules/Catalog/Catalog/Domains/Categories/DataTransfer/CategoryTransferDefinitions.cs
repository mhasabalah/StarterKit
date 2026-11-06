using System.Globalization;
using System.Runtime.CompilerServices;
using Catalog.Domains.Categories.Exceptions;
using Catalog.Domains.Categories.Features;
using DataTransfer.Contracts.Definitions;
using DataTransfer.Contracts.Enums;
using Shared.Core.MultiTenancy.Authorization;

namespace Catalog.Domains.Categories.DataTransfer;

/// <summary>Columns shared by the category import and export (stable names: they are the file contract).</summary>
public static class CategoryTransferColumns
{
    public const string Key = "catalog.categories";

    public const string Slug = "slug";
    public const string NameEn = "name_en";
    public const string NameAr = "name_ar";
    public const string Parent = "parent";
    public const string DisplayOrder = "display_order";
    public const string Active = "active";

    public static readonly LocalizedText DisplayName = new("Categories", "التصنيفات");

    public static readonly IReadOnlyList<TransferColumn> All =
    [
        new(Slug, new LocalizedText("Slug", "المعرف النصي"), Required: true, Sample: "clothing",
            Description: new LocalizedText("Unique key: lowercase letters, digits and single hyphens; an existing slug updates that category.",
                "مفتاح فريد: أحرف إنجليزية صغيرة وأرقام وشرطات مفردة؛ المعرف الموجود يحدّث ذلك التصنيف.")),
        new(NameEn, new LocalizedText("Name (English)", "الاسم (إنجليزي)"), Required: true, Sample: "Clothing"),
        new(NameAr, new LocalizedText("Name (Arabic)", "الاسم (عربي)"), Required: true, Sample: "ملابس"),
        new(Parent, new LocalizedText("Parent (slug)", "الأب (المعرف النصي)"),
            Description: new LocalizedText(
                "Slug of the parent category; empty = top level. The parent must already exist or appear in an EARLIER row.",
                "المعرف النصي للتصنيف الأب؛ فارغ = مستوى أعلى. يجب أن يكون الأب موجودًا أو في صف سابق.")),
        new(DisplayOrder, new LocalizedText("Display order", "ترتيب العرض"), Sample: "0",
            Description: new LocalizedText("Whole number, 0 or more; empty keeps the current value (0 for new).",
                "عدد صحيح 0 أو أكثر؛ الفارغ يُبقي القيمة الحالية (0 للجديد)."),
            Kind: TransferColumnKind.Number),
        new(Active, new LocalizedText("Active", "نشط"), Sample: "true",
            Description: new LocalizedText("true or false; empty keeps the current value (true for new).",
                "true أو false؛ الفارغ يُبقي القيمة الحالية (true للجديد)."))
    ];
}

/// <summary>
/// Bulk category import: upsert by slug through the <see cref="Category"/> aggregate (so <c>CategoryChangedEvent</c>
/// invalidates the cached tree when the job's unit of work commits). The parent is a slug resolved against the
/// tenant's categories plus the rows already processed in the file, so parents must come BEFORE their children;
/// moving a category checks for loops with <see cref="CategoryHierarchy"/>. Identical rows are <c>Unchanged</c> (no
/// version bump, no event). One save per batch; a dry run applies everything in memory and never saves.
/// </summary>
public sealed class CategoryImportDefinition(
    ICategoryRepository repository,
    IStringLocalizer<CatalogResource> localizer) : IImportDefinition
{
    public string Key => CategoryTransferColumns.Key;
    public LocalizedText DisplayName => CategoryTransferColumns.DisplayName;
    public string RequiredPermission => TenantPermissions.CategoriesManage;
    public IReadOnlyList<TransferColumn> Columns => CategoryTransferColumns.All;

    public async Task<IReadOnlyList<ImportRowResult>> ApplyBatchAsync(IReadOnlyList<ImportRecord> records,
        ImportContext context, CancellationToken cancellationToken)
    {
        var parsed = records.Select(r => (Record: r, Row: CategoryRow.Parse(r, localizer))).ToList();

        var wanted = parsed.Where(p => p.Row.Errors.Count == 0)
            .SelectMany(p => new[] { p.Row.Slug, p.Row.ParentSlug })
            .OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var bySlug = (await repository.GetBySlugsAsync(wanted, cancellationToken))
            .ToDictionary(c => c.Slug, StringComparer.Ordinal);
        var parents = new Dictionary<Guid, Guid?>(await repository.GetParentMapAsync(cancellationToken));

        var validator = new CategoryRowValidator(localizer);
        var results = new List<ImportRowResult>(records.Count);
        foreach (var (record, row) in parsed)
            results.Add(await ApplyRowAsync(record, row, bySlug, parents, validator, context, cancellationToken));

        if (!context.DryRun && results.Exists(r => r.Outcome is ImportRowOutcome.Created or ImportRowOutcome.Updated))
            await repository.SaveChangesAsync(cancellationToken);

        return results;
    }

    private async Task<ImportRowResult> ApplyRowAsync(ImportRecord record, CategoryRow row,
        Dictionary<string, Category> bySlug, Dictionary<Guid, Guid?> parents, CategoryRowValidator validator,
        ImportContext context, CancellationToken cancellationToken)
    {
        if (row.Errors.Count > 0)
            return ImportRowResult.Failed(record.RowNumber, row.Errors);

        var validation = await validator.ValidateAsync(row, cancellationToken);
        if (!validation.IsValid)
        {
            return ImportRowResult.Failed(record.RowNumber, validation.Errors
                .Select(e => new ImportRowError(CategoryRow.ColumnOf(e.PropertyName), "invalid", e.ErrorMessage))
                .ToList());
        }

        Guid? parentId = null;
        if (row.ParentSlug is { } parentSlug)
        {
            if (parentSlug == row.Slug)
                return Fail(record, CategoryTransferColumns.Parent, "parent_cycle", "Import.Category.Cycle", parentSlug);
            if (!bySlug.TryGetValue(parentSlug, out var parent))
                return Fail(record, CategoryTransferColumns.Parent, "parent_not_found", "Import.Category.ParentNotFound", parentSlug);
            parentId = parent.Id;
        }

        try
        {
            return bySlug.TryGetValue(row.Slug, out var existing)
                ? Update(record, existing, row, parentId, parents)
                : Create(record, row, parentId, bySlug, parents, context);
        }
        catch (InvalidCategoryException ex)
        {
            // Defence in depth: an aggregate invariant the validator did not cover fails the row, not the batch.
            return ImportRowResult.Failed(record.RowNumber, null, "invalid", ex.Message);
        }
    }

    private ImportRowResult Create(ImportRecord record, CategoryRow row, Guid? parentId,
        Dictionary<string, Category> bySlug, Dictionary<Guid, Guid?> parents, ImportContext context)
    {
        var category = Category.Create(new LocalizedText(row.NameEn, row.NameAr), row.Slug, parentId, row.DisplayOrder ?? 0);
        if (row.Active == false)
            category.Deactivate();
        bySlug[category.Slug] = category;
        parents[category.Id] = parentId;
        if (!context.DryRun)
            repository.Add(category);
        return ImportRowResult.Created(record.RowNumber);
    }

    private ImportRowResult Update(ImportRecord record, Category category, CategoryRow row, Guid? parentId,
        Dictionary<Guid, Guid?> parents)
    {
        var displayOrder = row.DisplayOrder ?? category.DisplayOrder;
        var active = row.Active ?? category.IsActive;
        if (category.Name.En == row.NameEn.Trim() && category.Name.Ar == row.NameAr.Trim() &&
            category.ParentId == parentId && category.DisplayOrder == displayOrder && category.IsActive == active)
            return ImportRowResult.Unchanged(record.RowNumber);

        if (parentId != category.ParentId && CategoryHierarchy.WouldCreateCycle(category.Id, parentId, parents))
        {
            return Fail(record, CategoryTransferColumns.Parent, "parent_cycle", "Import.Category.Cycle",
                row.ParentSlug ?? string.Empty);
        }

        category.Update(new LocalizedText(row.NameEn, row.NameAr), category.Slug, parentId, displayOrder);
        if (active)
            category.Activate();
        else
            category.Deactivate();
        parents[category.Id] = parentId;
        return ImportRowResult.Updated(record.RowNumber);
    }

    private ImportRowResult Fail(ImportRecord record, string column, string code, string key, string value) =>
        ImportRowResult.Failed(record.RowNumber, column, code, localizer[key, value]);
}

/// <summary>
/// Category export: every category of the tenant, parents before their children (so the file re-imports as is),
/// siblings by display order then slug. Categories are a small bounded lookup, so the tree is ordered in memory.
/// </summary>
public sealed class CategoryExportDefinition(CatalogDbContext dbContext) : IExportDefinition
{
    public string Key => CategoryTransferColumns.Key;
    public LocalizedText DisplayName => CategoryTransferColumns.DisplayName;
    public string RequiredPermission => TenantPermissions.CategoriesManage;
    public IReadOnlyList<TransferColumn> Columns => CategoryTransferColumns.All;

    public async IAsyncEnumerable<IReadOnlyList<string?>> StreamAsync(ExportContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await dbContext.Categories.AsNoTracking()
            .Select(c => new Row(c.Id, c.ParentId, c.Slug, c.Name.En, c.Name.Ar, c.DisplayOrder, c.IsActive))
            .ToListAsync(cancellationToken);

        var slugById = rows.ToDictionary(r => r.Id, r => r.Slug);
        foreach (var row in InParentFirstOrder(rows))
        {
            yield return
            [
                row.Slug, row.NameEn, row.NameAr,
                row.ParentId is { } parent ? slugById.GetValueOrDefault(parent) : null,
                row.DisplayOrder.ToString(CultureInfo.InvariantCulture),
                row.IsActive ? "true" : "false"
            ];
        }
    }

    private static List<Row> InParentFirstOrder(List<Row> rows)
    {
        var ids = rows.Select(r => r.Id).ToHashSet();
        var children = rows.Where(r => r.ParentId is { } p && ids.Contains(p)).ToLookup(r => r.ParentId!.Value);
        var ordered = new List<Row>(rows.Count);
        var seen = new HashSet<Guid>();

        void Visit(IEnumerable<Row> level)
        {
            foreach (var row in level.OrderBy(r => r.DisplayOrder).ThenBy(r => r.Slug, StringComparer.Ordinal))
            {
                if (!seen.Add(row.Id))
                    continue;
                ordered.Add(row);
                Visit(children[row.Id]);
            }
        }

        Visit(rows.Where(r => r.ParentId is null || !ids.Contains(r.ParentId.Value)));
        Visit(rows); // only a corrupt loop is left: still export it
        return ordered;
    }

    private sealed record Row(Guid Id, Guid? ParentId, string Slug, string NameEn, string NameAr, int DisplayOrder, bool IsActive);
}

/// <summary>One parsed import row: typed values, or the parse errors of its cells.</summary>
internal sealed class CategoryRow
{
    public string Slug { get; private init; } = string.Empty;
    public string NameEn { get; private init; } = string.Empty;
    public string NameAr { get; private init; } = string.Empty;
    public string? ParentSlug { get; private init; }
    public int? DisplayOrder { get; private init; }
    public bool? Active { get; private init; }
    public List<ImportRowError> Errors { get; } = [];

    // Shape read by the shared category field rules (same validation as the API).
    public LocalizedText Name => new(NameEn, NameAr);
    public Guid? ParentId { get; }
    public int DisplayOrderOrZero => DisplayOrder ?? 0;

    public static CategoryRow Parse(ImportRecord record, IStringLocalizer<CatalogResource> localizer)
    {
        var errors = new List<ImportRowError>();
        var row = new CategoryRow
        {
            Slug = Category.NormalizeSlug(Required(record, CategoryTransferColumns.Slug, errors, localizer)),
            NameEn = Required(record, CategoryTransferColumns.NameEn, errors, localizer),
            NameAr = Required(record, CategoryTransferColumns.NameAr, errors, localizer),
            ParentSlug = record.Get(CategoryTransferColumns.Parent) is { } p ? Category.NormalizeSlug(p) : null,
            DisplayOrder = ParseInt(record, CategoryTransferColumns.DisplayOrder, errors, localizer),
            Active = ParseBool(record, CategoryTransferColumns.Active, errors, localizer)
        };
        row.Errors.AddRange(errors);
        return row;
    }

    /// <summary>Validator property to file column, so row errors point at the right cell.</summary>
    public static string? ColumnOf(string propertyName) => propertyName switch
    {
        nameof(Slug) => CategoryTransferColumns.Slug,
        _ when propertyName.EndsWith(".En", StringComparison.Ordinal) => CategoryTransferColumns.NameEn,
        _ when propertyName.EndsWith(".Ar", StringComparison.Ordinal) => CategoryTransferColumns.NameAr,
        nameof(DisplayOrderOrZero) => CategoryTransferColumns.DisplayOrder,
        _ => null
    };

    private static string Required(ImportRecord record, string column, List<ImportRowError> errors,
        IStringLocalizer<CatalogResource> localizer)
    {
        var value = record.Get(column);
        if (value is null)
            errors.Add(new ImportRowError(column, "required", localizer["Import.Value.Required"]));
        return value ?? string.Empty;
    }

    private static int? ParseInt(ImportRecord record, string column, List<ImportRowError> errors,
        IStringLocalizer<CatalogResource> localizer)
    {
        var value = record.Get(column);
        if (value is null)
            return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return number;
        errors.Add(new ImportRowError(column, "invalid_number", localizer["Import.Integer.Invalid", value]));
        return null;
    }

    private static bool? ParseBool(ImportRecord record, string column, List<ImportRowError> errors,
        IStringLocalizer<CatalogResource> localizer)
    {
        var value = record.Get(column);
        if (value is null)
            return null;
        switch (value.ToLowerInvariant())
        {
            case "true" or "1" or "yes":
                return true;
            case "false" or "0" or "no":
                return false;
            default:
                errors.Add(new ImportRowError(column, "invalid_value", localizer["Import.Boolean.Invalid", value]));
                return null;
        }
    }
}

/// <summary>The API's category field rules applied to an import row.</summary>
internal sealed class CategoryRowValidator : AbstractValidator<CategoryRow>
{
    public CategoryRowValidator(IStringLocalizer<CatalogResource> localizer) =>
        this.AddCategoryFieldRules(localizer, x => x.Name, x => x.Slug, x => x.ParentId, x => x.DisplayOrderOrZero);
}
