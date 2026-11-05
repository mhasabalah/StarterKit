using Shared.Contracts.FeatureFlags;
using Tenancy.Contracts.FeatureFlags;
using Tenancy.Data.Configurations;
using Tenancy.Domains.FeatureFlags.Exceptions;
using Tenancy.Domains.FeatureFlags.Models;

namespace Tenancy.Domains.FeatureFlags.Features;

// Platform (host) flag administration. Flags live in the host database and are not tenant-owned; changes raise
// FeatureFlagChangedEvent, whose handler drops the flag cache on every replica after the commit.

public class GetFeatureFlagsHandler(TenancyDbContext dbContext)
    : IQueryHandler<GetFeatureFlagsQuery, IReadOnlyList<FeatureFlagDto>>
{
    public async Task<IReadOnlyList<FeatureFlagDto>> Handle(GetFeatureFlagsQuery query, CancellationToken cancellationToken)
    {
        var flags = await dbContext.FeatureFlags.AsNoTracking().OrderBy(f => f.Key)
            .Select(f => new
            {
                f.Id, f.Key, f.Description, f.Enabled, f.RolloutPercent, f.KillSwitch, f.Version,
                Overrides = f.Overrides.Select(o => new FeatureFlagOverrideDto(o.Id, o.TenantId, o.UserId, o.Enabled)).ToList()
            })
            .ToListAsync(cancellationToken);

        return flags.Select(f => new FeatureFlagDto(f.Id, f.Key, f.Description, f.Enabled, f.RolloutPercent,
            f.KillSwitch, f.Overrides, f.Version)).ToList();
    }
}

public class GetFeatureFlagByKeyQueryValidator : AbstractValidator<GetFeatureFlagByKeyQuery>
{
    public GetFeatureFlagByKeyQueryValidator() => RuleFor(x => x.Key).NotEmpty().MaximumLength(FeatureFlag.KeyMaxLength);
}

public class GetFeatureFlagByKeyHandler(TenancyDbContext dbContext) : IQueryHandler<GetFeatureFlagByKeyQuery, FeatureFlagDto>
{
    public async Task<FeatureFlagDto> Handle(GetFeatureFlagByKeyQuery query, CancellationToken cancellationToken)
    {
        var key = FeatureFlag.NormalizeKey(query.Key);
        var flag = await dbContext.FeatureFlags.AsNoTracking().Where(f => f.Key == key)
                       .Select(f => new
                       {
                           f.Id, f.Key, f.Description, f.Enabled, f.RolloutPercent, f.KillSwitch, f.Version,
                           Overrides = f.Overrides.Select(o => new FeatureFlagOverrideDto(o.Id, o.TenantId, o.UserId, o.Enabled)).ToList()
                       })
                       .FirstOrDefaultAsync(cancellationToken)
                   ?? throw new FeatureFlagNotFoundException(key);

        return new FeatureFlagDto(flag.Id, flag.Key, flag.Description, flag.Enabled, flag.RolloutPercent,
            flag.KillSwitch, flag.Overrides, flag.Version);
    }
}

/// <summary>Every flag evaluated for the caller, for clients that toggle UI (authenticated, tenant optional).</summary>
public class GetMyFeatureFlagsHandler(IFeatureFlags flags)
    : IQueryHandler<GetMyFeatureFlagsQuery, IReadOnlyDictionary<string, bool>>
{
    public Task<IReadOnlyDictionary<string, bool>> Handle(GetMyFeatureFlagsQuery query, CancellationToken cancellationToken) =>
        flags.GetAllAsync(cancellationToken);
}

public class CreateFeatureFlagCommandValidator : AbstractValidator<CreateFeatureFlagCommand>
{
    public CreateFeatureFlagCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(FeatureFlag.KeyMaxLength);
        RuleFor(x => x.Description).MaximumLength(FeatureFlag.DescriptionMaxLength);
        RuleFor(x => x.RolloutPercent).InclusiveBetween(0, 100);
    }
}

public class CreateFeatureFlagHandler(TenancyDbContext dbContext) : ICommandHandler<CreateFeatureFlagCommand, CreateFeatureFlagResult>
{
    public async Task<CreateFeatureFlagResult> Handle(CreateFeatureFlagCommand command, CancellationToken cancellationToken)
    {
        var flag = FeatureFlag.Create(command.Key, command.Description, command.Enabled, command.RolloutPercent,
            command.KillSwitch);
        if (await dbContext.FeatureFlags.AnyAsync(f => f.Key == flag.Key, cancellationToken))
            throw new FeatureFlagAlreadyExistsException(flag.Key);

        dbContext.FeatureFlags.Add(flag);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(FeatureFlagConfiguration.KeyIndexName))
        {
            throw new FeatureFlagAlreadyExistsException(flag.Key);
        }

        return new CreateFeatureFlagResult(flag.Id, flag.Key);
    }
}

public class UpdateFeatureFlagCommandValidator : AbstractValidator<UpdateFeatureFlagCommand>
{
    public UpdateFeatureFlagCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(FeatureFlag.KeyMaxLength);
        RuleFor(x => x.Description).MaximumLength(FeatureFlag.DescriptionMaxLength);
        RuleFor(x => x.RolloutPercent).InclusiveBetween(0, 100);
    }
}

public class UpdateFeatureFlagHandler(TenancyDbContext dbContext) : ICommandHandler<UpdateFeatureFlagCommand, UpdateFeatureFlagResult>
{
    public async Task<UpdateFeatureFlagResult> Handle(UpdateFeatureFlagCommand command, CancellationToken cancellationToken)
    {
        var key = FeatureFlag.NormalizeKey(command.Key);
        var flag = await dbContext.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key, cancellationToken)
                   ?? throw new FeatureFlagNotFoundException(key);
        dbContext.ExpectVersion(flag, command.Version);

        flag.Update(command.Description, command.Enabled, command.RolloutPercent, command.KillSwitch);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new UpdateFeatureFlagResult(flag.Id, flag.Version);
    }
}

public class DeleteFeatureFlagCommandValidator : AbstractValidator<DeleteFeatureFlagCommand>
{
    public DeleteFeatureFlagCommandValidator() => RuleFor(x => x.Key).NotEmpty().MaximumLength(FeatureFlag.KeyMaxLength);
}

public class DeleteFeatureFlagHandler(TenancyDbContext dbContext) : ICommandHandler<DeleteFeatureFlagCommand, DeleteFeatureFlagResult>
{
    public async Task<DeleteFeatureFlagResult> Handle(DeleteFeatureFlagCommand command, CancellationToken cancellationToken)
    {
        var key = FeatureFlag.NormalizeKey(command.Key);
        var flag = await dbContext.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key, cancellationToken)
                   ?? throw new FeatureFlagNotFoundException(key);

        flag.MarkDeleted();
        dbContext.FeatureFlags.Remove(flag); // overrides cascade
        await dbContext.SaveChangesAsync(cancellationToken);
        return new DeleteFeatureFlagResult(key);
    }
}

public class SetFeatureFlagOverrideCommandValidator : AbstractValidator<SetFeatureFlagOverrideCommand>
{
    public SetFeatureFlagOverrideCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(FeatureFlag.KeyMaxLength);
        RuleFor(x => x).Must(x => x.TenantId.HasValue != x.UserId.HasValue)
            .WithMessage("Exactly one of tenantId or userId is required.");
        RuleFor(x => x.TenantId).NotEqual(Guid.Empty).When(x => x.TenantId.HasValue);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty).When(x => x.UserId.HasValue);
    }
}

public class SetFeatureFlagOverrideHandler(TenancyDbContext dbContext)
    : ICommandHandler<SetFeatureFlagOverrideCommand, SetFeatureFlagOverrideResult>
{
    public async Task<SetFeatureFlagOverrideResult> Handle(SetFeatureFlagOverrideCommand command,
        CancellationToken cancellationToken)
    {
        var key = FeatureFlag.NormalizeKey(command.Key);
        var flag = await dbContext.FeatureFlags.Include(f => f.Overrides).FirstOrDefaultAsync(f => f.Key == key, cancellationToken)
                   ?? throw new FeatureFlagNotFoundException(key);

        var target = flag.SetOverride(command.TenantId, command.UserId, command.Enabled);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new ConflictException("The override was changed concurrently; retry.");
        }

        return new SetFeatureFlagOverrideResult(target.Id);
    }
}

public class RemoveFeatureFlagOverrideCommandValidator : AbstractValidator<RemoveFeatureFlagOverrideCommand>
{
    public RemoveFeatureFlagOverrideCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(FeatureFlag.KeyMaxLength);
        RuleFor(x => x.OverrideId).NotEmpty();
    }
}

public class RemoveFeatureFlagOverrideHandler(TenancyDbContext dbContext)
    : ICommandHandler<RemoveFeatureFlagOverrideCommand, RemoveFeatureFlagOverrideResult>
{
    public async Task<RemoveFeatureFlagOverrideResult> Handle(RemoveFeatureFlagOverrideCommand command,
        CancellationToken cancellationToken)
    {
        var key = FeatureFlag.NormalizeKey(command.Key);
        var flag = await dbContext.FeatureFlags.Include(f => f.Overrides).FirstOrDefaultAsync(f => f.Key == key, cancellationToken)
                   ?? throw new FeatureFlagNotFoundException(key);

        flag.RemoveOverride(command.OverrideId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new RemoveFeatureFlagOverrideResult(command.OverrideId);
    }
}
