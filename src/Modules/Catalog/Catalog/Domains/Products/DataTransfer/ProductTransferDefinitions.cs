using System.Globalization;
using System.Runtime.CompilerServices;
using DataTransfer.Contracts.Definitions;
using DataTransfer.Contracts.Enums;
using Catalog.Domains.Products.Features;
using Shared.Core.MultiTenancy.Authorization;

namespace Catalog.Domains.Products.DataTransfer;

/// <summary>Columns shared by the product import and export (stable names: they are the file contract).</summary>
public static class ProductTransferColumns
{
    public const string Key = "catalog.products";

    public const string Sku = "sku";
    public const string NameEn = "name_en";
    public const string NameAr = "name_ar";
    public const string Description = "description";
    public const string Type = "type";
    public const string Price = "price";
    public const string CompareAtPrice = "compare_at_price";
    public const string Currency = "currency";
    public const string Category = "category";
    public const string Status = "status";

    public static readonly LocalizedText DisplayName = new("Products", "المنتجات");

    public static readonly IReadOnlyList<TransferColumn> Import =
    [
        new(Sku, new LocalizedText("SKU", "رمز المنتج"), Required: true, Sample: "TSHIRT-RED-M",
            Description: new LocalizedText("Unique product code; an existing SKU updates that product.",
                "رمز فريد للمنتج؛ الرمز الموجود يحدّث ذلك المنتج.")),
        new(NameEn, new LocalizedText("Name (English)", "الاسم (إنجليزي)"), Required: true, Sample: "Red T-shirt"),
        new(NameAr, new LocalizedText("Name (Arabic)", "الاسم (عربي)"), Required: true, Sample: "قميص أحمر"),
        new(Description, new LocalizedText("Description", "الوصف"), Sample: "100% cotton"),
        new(Type, new LocalizedText("Type", "النوع"), Required: true, Sample: nameof(ProductType.Physical),
            Description: new LocalizedText("Physical, Digital or Service.", "Physical أو Digital أو Service.")),
        new(Price, new LocalizedText("Price", "السعر"), Required: true, Sample: "199.00",
            Description: new LocalizedText("Decimal with a dot, at most 2 decimals.", "رقم عشري بنقطة، بحد أقصى منزلتين."),
            Kind: TransferColumnKind.Number),
        new(CompareAtPrice, new LocalizedText("Compare-at price", "السعر قبل الخصم"), Sample: "249.00",
            Description: new LocalizedText("Optional; must be greater than the price.", "اختياري؛ يجب أن يكون أكبر من السعر."),
            Kind: TransferColumnKind.Number),
        new(Currency, new LocalizedText("Currency", "العملة"), Required: true, Sample: nameof(Contracts.Products.Enums.Currency.EGP),
            Description: new LocalizedText("USD, EUR, AED, SAR or EGP.", "USD أو EUR أو AED أو SAR أو EGP.")),
        new(Category, new LocalizedText("Category (slug)", "التصنيف (المعرف النصي)"), Sample: "clothing",
            Description: new LocalizedText("Optional slug of an active category.", "اختياري: المعرف النصي لتصنيف نشط."))
    ];

    public static readonly IReadOnlyList<TransferColumn> Export =
    [
        .. Import,
        new(Status, new LocalizedText("Status", "الحالة"), Description: new LocalizedText("Read only: ignored on import.", "للقراءة فقط: يُتجاهل عند الاستيراد."))
    ];
}

/// <summary>
/// Bulk product import (reference implementation of <see cref="IImportDefinition"/>). Upsert by SKU, through the
/// <see cref="Product"/> aggregate and <see cref="IProductRepository"/>, so invariants and domain events stay intact.
/// <list type="bullet">
/// <item>New SKU: a Draft product owned by the requester (the moderation workflow is unchanged).</item>
/// <item>Existing SKU: updated only when the requester owns it and its status lets the owner edit it, like
/// <c>PUT /products/{id}</c>; identical values are <c>Unchanged</c> (no version bump, no event). A SKU of a deleted
/// product is refused.</item>
/// <item>Rows are validated with the same <see cref="ProductDetailsValidator"/> as the API; messages come out in the
/// requester's language (the job sets the UI culture to <see cref="ImportContext.Culture"/>).</item>
/// <item>One save per batch; a SKU repeated in the batch updates the row created earlier in it.</item>
/// </list>
/// </summary>
public sealed class ProductImportDefinition(
    IProductRepository repository,
    CatalogDbContext dbContext,
    IStringLocalizer<CatalogResource> localizer) : IImportDefinition
{
    public string Key => ProductTransferColumns.Key;
    public LocalizedText DisplayName => ProductTransferColumns.DisplayName;
    public string RequiredPermission => TenantPermissions.ProductsManage;
    public IReadOnlyList<TransferColumn> Columns => ProductTransferColumns.Import;

    public async Task<IReadOnlyList<ImportRowResult>> ApplyBatchAsync(IReadOnlyList<ImportRecord> records,
        ImportContext context, CancellationToken cancellationToken)
    {
        var parsed = records.Select(r => (Record: r, Row: ProductRow.Parse(r, localizer))).ToList();

        var categories = await LoadCategoriesAsync(parsed.Select(p => p.Row.CategorySlug), cancellationToken);
        var skus = parsed.Where(p => p.Row.Errors.Count == 0).Select(p => Product.NormalizeSku(p.Row.Sku)).Distinct().ToList();
        var existing = (await repository.GetBySkusIncludingDeletedAsync(skus, cancellationToken))
            .ToDictionary(p => p.Sku, StringComparer.Ordinal);

        var validator = new ProductDetailsValidator(localizer);
        var results = new List<ImportRowResult>(records.Count);
        foreach (var (record, row) in parsed)
            results.Add(await ApplyRowAsync(record, row, categories, existing, validator, context, cancellationToken));

        if (!context.DryRun && results.Exists(r => r.Outcome is ImportRowOutcome.Created or ImportRowOutcome.Updated))
            await repository.SaveChangesAsync(cancellationToken);

        return results;
    }

    private async Task<ImportRowResult> ApplyRowAsync(ImportRecord record, ProductRow row,
        IReadOnlyDictionary<string, Guid> categories, Dictionary<string, Product> existing,
        ProductDetailsValidator validator, ImportContext context, CancellationToken cancellationToken)
    {
        if (row.Errors.Count > 0)
            return ImportRowResult.Failed(record.RowNumber, row.Errors);

        Guid? categoryId = null;
        if (row.CategorySlug is { } slug)
        {
            if (!categories.TryGetValue(slug, out var id))
                return Fail(record, ProductTransferColumns.Category, "category_not_found", "Import.Category.NotFound", slug);
            categoryId = id;
        }

        var details = row.ToDetails(categoryId);
        var validation = await validator.ValidateAsync(details, cancellationToken);
        if (!validation.IsValid)
        {
            return ImportRowResult.Failed(record.RowNumber, validation.Errors
                .Select(e => new ImportRowError(ProductRow.ColumnOf(e.PropertyName), "invalid", e.ErrorMessage))
                .ToList());
        }

        var sku = Product.NormalizeSku(details.Sku);
        try
        {
            return existing.TryGetValue(sku, out var product)
                ? Update(record, product, details, context)
                : Create(record, details, existing, context);
        }
        catch (InvalidProductException ex)
        {
            // Defence in depth: an aggregate invariant the validator did not cover fails the row, not the batch.
            return ImportRowResult.Failed(record.RowNumber, null, "invalid", ex.Message);
        }
    }

    private ImportRowResult Create(ImportRecord record, ProductRow.Details details, Dictionary<string, Product> existing,
        ImportContext context)
    {
        var product = Product.Create(details.Name, details.Description, details.Sku, details.Type,
            Pricing.Create(details.Price, details.CompareAtPrice, details.Currency), context.RequestedBy, details.CategoryId);
        existing[product.Sku] = product;
        if (!context.DryRun)
            repository.Add(product);
        return ImportRowResult.Created(record.RowNumber);
    }

    private ImportRowResult Update(ImportRecord record, Product product, ProductRow.Details details, ImportContext context)
    {
        if (product.IsDeleted)
            return Fail(record, ProductTransferColumns.Sku, "sku_deleted", "Import.Sku.Deleted", product.Sku);
        if (product.OwnerId != context.RequestedBy || !ProductStatusTransitions.EditableByOwner.Contains(product.Status))
            return Fail(record, ProductTransferColumns.Sku, "not_editable", "Import.Product.NotEditable", product.Sku);
        if (Matches(product, details))
            return ImportRowResult.Unchanged(record.RowNumber);

        if (!context.DryRun)
        {
            product.Update(details.Name, details.Description, details.Sku, details.Type,
                Pricing.Create(details.Price, details.CompareAtPrice, details.Currency), details.CategoryId);
        }

        return ImportRowResult.Updated(record.RowNumber);
    }

    private static bool Matches(Product p, ProductRow.Details d) =>
        p.Name.En == d.Name.En.Trim() && p.Name.Ar == d.Name.Ar.Trim() &&
        p.Description == d.Description && p.Type == d.Type && p.CategoryId == d.CategoryId &&
        p.Pricing.Price == d.Price && p.Pricing.CompareAtPrice == d.CompareAtPrice && p.Pricing.Currency == d.Currency;

    private ImportRowResult Fail(ImportRecord record, string column, string code, string key, string value) =>
        ImportRowResult.Failed(record.RowNumber, column, code, localizer[key, value]);

    /// <summary>Active categories of the batch by slug (one query).</summary>
    private async Task<IReadOnlyDictionary<string, Guid>> LoadCategoriesAsync(IEnumerable<string?> slugs,
        CancellationToken cancellationToken)
    {
        var wanted = slugs.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
            return new Dictionary<string, Guid>();

        return await dbContext.Categories.AsNoTracking()
            .Where(c => c.IsActive && wanted.Contains(c.Slug))
            .ToDictionaryAsync(c => c.Slug, c => c.Id, StringComparer.Ordinal, cancellationToken);
    }
}

/// <summary>
/// Product export: every product of the tenant (not deleted), oldest first, in the import column order plus the
/// status. Filters: <c>status</c> (a <see cref="ProductStatus"/>), <c>mine=true</c> (the requester's products only).
/// </summary>
public sealed class ProductExportDefinition(CatalogDbContext dbContext) : IExportDefinition
{
    public string Key => ProductTransferColumns.Key;
    public LocalizedText DisplayName => ProductTransferColumns.DisplayName;
    public string RequiredPermission => TenantPermissions.ProductsManage;
    public IReadOnlyList<TransferColumn> Columns => ProductTransferColumns.Export;

    public async IAsyncEnumerable<IReadOnlyList<string?>> StreamAsync(ExportContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var products = dbContext.Products.AsNoTracking();
        if (context.Filter.TryGetValue("status", out var statusText) &&
            Enum.TryParse<ProductStatus>(statusText, ignoreCase: true, out var status))
        {
            products = products.Where(p => p.Status == status);
        }

        if (context.Filter.TryGetValue("mine", out var mine) && bool.TryParse(mine, out var onlyMine) && onlyMine)
            products = products.Where(p => p.OwnerId == context.RequestedBy);

        var rows = products
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Sku,
                NameEn = p.Name.En,
                NameAr = p.Name.Ar,
                p.Description,
                p.Type,
                p.Pricing.Price,
                p.Pricing.CompareAtPrice,
                p.Pricing.Currency,
                CategorySlug = p.Category != null ? p.Category.Slug : null,
                p.Status
            })
            .AsAsyncEnumerable();

        await foreach (var p in rows.WithCancellation(cancellationToken))
        {
            yield return
            [
                p.Sku, p.NameEn, p.NameAr, p.Description, p.Type.ToString(),
                p.Price.ToString("0.00", CultureInfo.InvariantCulture),
                p.CompareAtPrice?.ToString("0.00", CultureInfo.InvariantCulture),
                p.Currency.ToString(), p.CategorySlug, p.Status.ToString()
            ];
        }
    }
}

/// <summary>One parsed import row: typed values, or the parse errors of its cells.</summary>
internal sealed class ProductRow
{
    public string Sku { get; private init; } = string.Empty;
    public string NameEn { get; private init; } = string.Empty;
    public string NameAr { get; private init; } = string.Empty;
    public string? Description { get; private init; }
    public ProductType Type { get; private init; }
    public decimal Price { get; private init; }
    public decimal? CompareAtPrice { get; private init; }
    public Currency Currency { get; private init; }
    public string? CategorySlug { get; private init; }
    public List<ImportRowError> Errors { get; } = [];

    public static ProductRow Parse(ImportRecord record, IStringLocalizer<CatalogResource> localizer)
    {
        var errors = new List<ImportRowError>();
        var row = new ProductRow
        {
            Sku = Required(record, ProductTransferColumns.Sku, errors, localizer),
            NameEn = Required(record, ProductTransferColumns.NameEn, errors, localizer),
            NameAr = Required(record, ProductTransferColumns.NameAr, errors, localizer),
            Description = record.Get(ProductTransferColumns.Description),
            Type = ParseEnum<ProductType>(record, ProductTransferColumns.Type, errors, localizer),
            Price = ParseDecimal(record, ProductTransferColumns.Price, required: true, errors, localizer) ?? 0,
            CompareAtPrice = ParseDecimal(record, ProductTransferColumns.CompareAtPrice, required: false, errors, localizer),
            Currency = ParseEnum<Currency>(record, ProductTransferColumns.Currency, errors, localizer),
            CategorySlug = record.Get(ProductTransferColumns.Category)?.ToLowerInvariant()
        };
        row.Errors.AddRange(errors);
        return row;
    }

    public Details ToDetails(Guid? categoryId) =>
        new(new LocalizedText(NameEn, NameAr), Description, Sku, Type, Price, CompareAtPrice, Currency, categoryId);

    /// <summary>Validator property → file column, so row errors point at the right cell.</summary>
    public static string? ColumnOf(string propertyName) => propertyName switch
    {
        nameof(IProductDetails.Sku) => ProductTransferColumns.Sku,
        "Name.En" => ProductTransferColumns.NameEn,
        "Name.Ar" => ProductTransferColumns.NameAr,
        nameof(IProductDetails.Description) => ProductTransferColumns.Description,
        nameof(IProductDetails.Type) => ProductTransferColumns.Type,
        nameof(IProductDetails.Price) => ProductTransferColumns.Price,
        nameof(IProductDetails.CompareAtPrice) => ProductTransferColumns.CompareAtPrice,
        nameof(IProductDetails.Currency) => ProductTransferColumns.Currency,
        nameof(IProductDetails.CategoryId) => ProductTransferColumns.Category,
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

    private static decimal? ParseDecimal(ImportRecord record, string column, bool required, List<ImportRowError> errors,
        IStringLocalizer<CatalogResource> localizer)
    {
        var value = record.Get(column);
        if (value is null)
        {
            if (required)
                errors.Add(new ImportRowError(column, "required", localizer["Import.Value.Required"]));
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return number;
        errors.Add(new ImportRowError(column, "invalid_number", localizer["Import.Number.Invalid", value]));
        return null;
    }

    private static TEnum ParseEnum<TEnum>(ImportRecord record, string column, List<ImportRowError> errors,
        IStringLocalizer<CatalogResource> localizer) where TEnum : struct, Enum
    {
        var value = record.Get(column);
        if (value is null)
        {
            errors.Add(new ImportRowError(column, "required", localizer["Import.Value.Required"]));
            return default;
        }

        if (!int.TryParse(value, out _) && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            return parsed;
        errors.Add(new ImportRowError(column, "invalid_value",
            localizer["Import.Enum.Invalid", value, string.Join(", ", Enum.GetNames<TEnum>())]));
        return default;
    }

    /// <summary>The validated shape (same rules as the create/update commands).</summary>
    public sealed record Details(
        LocalizedText Name,
        string? Description,
        string Sku,
        ProductType Type,
        decimal Price,
        decimal? CompareAtPrice,
        Currency Currency,
        Guid? CategoryId) : IProductDetails
    {
        public List<Guid>? TagIds => null;
        public List<ProductAttributeInput>? Attributes => null;
    }
}
