using Catalog.Data.Configurations;

namespace Catalog.Domains.AttributeTypes.Features;

// Lightweight lookup pattern (see TagHandlers): handlers use CatalogDbContext directly.

internal static class AttributeTypeRules
{
    public static void AddAttributeTypeRules<T>(
        this AbstractValidator<T> validator,
        IStringLocalizer<CatalogResource> localizer,
        System.Linq.Expressions.Expression<Func<T, LocalizedText>> name,
        System.Linq.Expressions.Expression<Func<T, AttributeDataType>> dataType,
        System.Linq.Expressions.Expression<Func<T, string?>> unit,
        System.Linq.Expressions.Expression<Func<T, ProductType[]?>> applicableTypes)
    {
        validator.RuleFor(name).ValidLocalizedName(localizer, AttributeType.NameMaxLength);
        validator.RuleFor(dataType).IsInEnum().WithMessage(_ => localizer["Validation.DataType.Invalid"]);
        validator.RuleFor(unit).MaximumLength(AttributeType.UnitMaxLength)
            .WithMessage(_ => localizer["Validation.Unit.MaxLength"]);
        validator.RuleFor(applicableTypes)
            .Must(types => types is null || types.All(Enum.IsDefined))
            .WithMessage(_ => localizer["Validation.ApplicableTypes.Invalid"]);
    }

    public static async Task SaveAsync(CatalogDbContext dbContext, AttributeType type, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(AttributeTypeConfiguration.NameIndexName))
        {
            throw new ConflictException($"An attribute type named '{type.Name.En}' already exists.");
        }
    }
}

public class CreateAttributeTypeCommandValidator : AbstractValidator<CreateAttributeTypeCommand>
{
    public CreateAttributeTypeCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        this.AddAttributeTypeRules(localizer, x => x.Name, x => x.DataType, x => x.Unit, x => x.ApplicableTypes);
    }
}

public class CreateAttributeTypeHandler(CatalogDbContext dbContext)
    : ICommandHandler<CreateAttributeTypeCommand, CreateAttributeTypeResult>
{
    public async Task<CreateAttributeTypeResult> Handle(CreateAttributeTypeCommand command, CancellationToken ct)
    {
        var type = AttributeType.Create(command.Name, command.DataType, command.Unit, command.ApplicableTypes);
        dbContext.AttributeTypes.Add(type);
        await AttributeTypeRules.SaveAsync(dbContext, type, ct);
        return new CreateAttributeTypeResult(type.Id);
    }
}

public class UpdateAttributeTypeCommandValidator : AbstractValidator<UpdateAttributeTypeCommand>
{
    public UpdateAttributeTypeCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        this.AddAttributeTypeRules(localizer, x => x.Name, x => x.DataType, x => x.Unit, x => x.ApplicableTypes);
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
    }
}

/// <summary>
/// Changing the data type does not re-validate existing product values (they stay as stored text); new
/// writes are validated against the new type.
/// </summary>
public class UpdateAttributeTypeHandler(CatalogDbContext dbContext)
    : ICommandHandler<UpdateAttributeTypeCommand, UpdateAttributeTypeResult>
{
    public async Task<UpdateAttributeTypeResult> Handle(UpdateAttributeTypeCommand command, CancellationToken ct)
    {
        var type = await dbContext.AttributeTypes.FirstOrDefaultAsync(a => a.Id == command.Id, ct)
                   ?? throw new NotFoundException("AttributeType", command.Id);

        dbContext.ExpectVersion(type, command.Version);

        type.Update(command.Name, command.DataType, command.Unit, command.ApplicableTypes);
        if (command.IsActive)
            type.Activate();
        else
            type.Deactivate();

        await AttributeTypeRules.SaveAsync(dbContext, type, ct);
        return new UpdateAttributeTypeResult(true, type.Version);
    }
}

public class DeleteAttributeTypeCommandValidator : AbstractValidator<DeleteAttributeTypeCommand>
{
    public DeleteAttributeTypeCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
    }
}

public class DeleteAttributeTypeHandler(CatalogDbContext dbContext)
    : ICommandHandler<DeleteAttributeTypeCommand, DeleteAttributeTypeResult>
{
    public async Task<DeleteAttributeTypeResult> Handle(DeleteAttributeTypeCommand command, CancellationToken ct)
    {
        var type = await dbContext.AttributeTypes.FirstOrDefaultAsync(a => a.Id == command.Id, ct)
                   ?? throw new NotFoundException("AttributeType", command.Id);

        type.Deactivate();
        await dbContext.SaveChangesAsync(ct);
        return new DeleteAttributeTypeResult(true);
    }
}

/// <summary>
/// Explicit LINQ projection (int[] → ProductType[] and enum labels make it not "simple"). The
/// <c>ApplicableTypes</c> filter is translated by Npgsql to an array-contains check.
/// </summary>
public class GetAttributeTypesHandler(CatalogDbContext dbContext, IEnumLocalizationService enumLocalization)
    : IQueryHandler<GetAttributeTypesQuery, List<AttributeTypeDto>>
{
    public async Task<List<AttributeTypeDto>> Handle(GetAttributeTypesQuery query, CancellationToken ct)
    {
        var productType = query.ProductType.HasValue ? (int?)query.ProductType.Value : null;

        var rows = await dbContext.AttributeTypes.AsNoTracking()
            .Where(a => query.IncludeInactive || a.IsActive)
            .Where(a => productType == null || a.ApplicableTypes == null ||
                        a.ApplicableTypes.Contains(productType.Value))
            .OrderBy(a => a.Name.En)
            .Select(a => new
            {
                a.Id,
                a.Name.En,
                a.Name.Ar,
                a.DataType,
                a.Unit,
                a.ApplicableTypes,
                a.IsActive,
                a.Version
            })
            .ToListAsync(ct);

        return rows.Select(a =>
        {
            var name = LocalizedText.Create(a.En, a.Ar);
            return new AttributeTypeDto
            {
                Id = a.Id,
                Name = name,
                DisplayName = name.Resolve(),
                DataType = a.DataType,
                DataTypeLabel = enumLocalization.Localize(a.DataType),
                Unit = a.Unit,
                ApplicableTypes = a.ApplicableTypes?.Select(t => (ProductType)t).ToArray(),
                IsActive = a.IsActive,
                Version = a.Version
            };
        }).ToList();
    }
}
