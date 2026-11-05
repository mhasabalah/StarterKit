using Catalog.Data.Configurations;

namespace Catalog.Domains.Tags.Features;

// Lightweight lookup pattern: Tag is a simple entity (not an aggregate with a repository), so its admin
// CRUD handlers use CatalogDbContext directly — the same choice Properties makes for FeatureType/TagType.

public class CreateTagCommandValidator : AbstractValidator<CreateTagCommand>
{
    public CreateTagCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Name).ValidLocalizedName(localizer, Tag.NameMaxLength);
    }
}

public class CreateTagHandler(CatalogDbContext dbContext) : ICommandHandler<CreateTagCommand, CreateTagResult>
{
    public async Task<CreateTagResult> Handle(CreateTagCommand command, CancellationToken cancellationToken)
    {
        var tag = Tag.Create(command.Name);
        dbContext.Tags.Add(tag);
        await SaveAsync(dbContext, tag, cancellationToken);
        return new CreateTagResult(tag.Id);
    }

    internal static async Task SaveAsync(CatalogDbContext dbContext, Tag tag, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(TagConfiguration.NameIndexName))
        {
            throw new ConflictException($"A tag named '{tag.Name.En}' already exists.");
        }
    }
}

public class UpdateTagCommandValidator : AbstractValidator<UpdateTagCommand>
{
    public UpdateTagCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Name).ValidLocalizedName(localizer, Tag.NameMaxLength);
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
    }
}

public class UpdateTagHandler(CatalogDbContext dbContext) : ICommandHandler<UpdateTagCommand, UpdateTagResult>
{
    public async Task<UpdateTagResult> Handle(UpdateTagCommand command, CancellationToken cancellationToken)
    {
        var tag = await dbContext.Tags.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
                  ?? throw new NotFoundException("Tag", command.Id);

        dbContext.ExpectVersion(tag, command.Version);

        tag.Rename(command.Name);
        if (command.IsActive)
            tag.Activate();
        else
            tag.Deactivate();

        await CreateTagHandler.SaveAsync(dbContext, tag, cancellationToken);
        return new UpdateTagResult(true, tag.Version);
    }
}

public class DeleteTagCommandValidator : AbstractValidator<DeleteTagCommand>
{
    public DeleteTagCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
    }
}

/// <summary>Deactivate instead of delete: products keep the tag; it is hidden from public lists.</summary>
public class DeleteTagHandler(CatalogDbContext dbContext) : ICommandHandler<DeleteTagCommand, DeleteTagResult>
{
    public async Task<DeleteTagResult> Handle(DeleteTagCommand command, CancellationToken cancellationToken)
    {
        var tag = await dbContext.Tags.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
                  ?? throw new NotFoundException("Tag", command.Id);

        tag.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);
        return new DeleteTagResult(true);
    }
}

/// <summary>Mapster ProjectToType for the simple projection; DisplayName resolved in memory.</summary>
public class GetTagsHandler(CatalogDbContext dbContext) : IQueryHandler<GetTagsQuery, List<TagDto>>
{
    public async Task<List<TagDto>> Handle(GetTagsQuery query, CancellationToken cancellationToken)
    {
        var tags = await dbContext.Tags.AsNoTracking()
            .Where(t => query.IncludeInactive || t.IsActive)
            .OrderBy(t => t.Name.En)
            .ProjectToType<TagDto>()
            .ToListAsync(cancellationToken);

        return tags.Select(t => t with { DisplayName = t.Name.Resolve() }).ToList();
    }
}
