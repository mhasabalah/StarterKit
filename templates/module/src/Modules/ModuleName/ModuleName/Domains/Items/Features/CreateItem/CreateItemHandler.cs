using System.Globalization;
using ModuleName.Data.Configurations;
using ModuleName.Domains.Items.Exceptions;
using ModuleName.Domains.Items.Models;

namespace ModuleName.Domains.Items.Features.CreateItem;

public class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator(IStringLocalizer<ModuleNameResource> localizer)
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage(_ => localizer["Validation.Code.Required"])
            .MaximumLength(Item.CodeMaxLength)
            .WithMessage(_ => localizer["Validation.Code.MaxLength"].Value
                .Replace("{MaxLength}", Item.CodeMaxLength.ToString(CultureInfo.InvariantCulture)));

        // A separate rule: a trailing When() applies to every validator of its chain.
        RuleFor(x => x.Code)
            .Must(code => Item.CodePattern().IsMatch(Item.NormalizeCode(code)))
            .WithMessage(_ => localizer["Validation.Code.Format"])
            .When(x => !string.IsNullOrWhiteSpace(x.Code) && x.Code.Trim().Length <= Item.CodeMaxLength);
        this.AddNameRules(x => x.Name, localizer);
        this.AddDescriptionRules(x => x.Description, localizer);
    }
}

public class CreateItemHandler(IItemRepository repository, IStringLocalizer<ModuleNameResource> localizer)
    : ICommandHandler<CreateItemCommand, CreateItemResult>
{
    public async Task<CreateItemResult> Handle(CreateItemCommand command, CancellationToken cancellationToken)
    {
        var item = Item.Create(command.Code, command.Name, command.Description);

        if (await repository.CodeExistsAsync(item.Code, cancellationToken))
            throw AlreadyExists(item.Code);

        repository.Add(item);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(ItemConfiguration.CodeIndexName))
        {
            throw AlreadyExists(item.Code); // a concurrent create won the race
        }

        return new CreateItemResult(item.Id);
    }

    private ItemAlreadyExistsException AlreadyExists(string code) =>
        new(string.Format(CultureInfo.CurrentCulture, localizer["Item.AlreadyExists"].Value, code));
}
