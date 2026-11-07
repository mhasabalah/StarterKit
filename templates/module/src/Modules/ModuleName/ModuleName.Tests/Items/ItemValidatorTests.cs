using FluentValidation.TestHelper;
using ModuleName.Contracts.Items;
using ModuleName.Domains.Items.Features.CreateItem;
using ModuleName.Domains.Items.Features.GetItems;
using ModuleName.Domains.Items.Features.UpdateItem;
using ModuleName.Domains.Items.Models;

namespace ModuleName.Tests.Items;

public class ItemValidatorTests
{
    private readonly CreateItemCommandValidator _create = new(TestSupport.Localizer());
    private readonly UpdateItemCommandValidator _update = new(TestSupport.Localizer());
    private readonly GetItemsQueryValidator _list = new(TestSupport.Localizer());

    [Fact]
    public void Create_AcceptsAValidCommand() =>
        _create.TestValidate(new CreateItemCommand("ab-1", "Name", "Description")).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("bad code")]
    public void Create_RejectsAMissingOrMalformedCode(string code) =>
        _create.TestValidate(new CreateItemCommand(code, "Name", null)).ShouldHaveValidationErrorFor(x => x.Code);

    [Fact]
    public void Create_RejectsATooLongCode_WithTheLimitInTheMessage()
    {
        var result = _create.TestValidate(new CreateItemCommand(new string('A', Item.CodeMaxLength + 1), "Name", null));

        result.ShouldHaveValidationErrorFor(x => x.Code)
            .WithErrorMessage($"The code must not exceed {Item.CodeMaxLength} characters.");
    }

    [Fact]
    public void Create_RejectsAMissingName() =>
        _create.TestValidate(new CreateItemCommand("A1", "", null)).ShouldHaveValidationErrorFor(x => x.Name);

    [Fact]
    public void Update_RejectsAnEmptyId_AndANegativeVersion()
    {
        var result = _update.TestValidate(new UpdateItemCommand(Guid.Empty, "Name", null, -1));

        result.ShouldHaveValidationErrorFor(x => x.Id);
        result.ShouldHaveValidationErrorFor(x => x.Version);
    }

    [Fact]
    public void Update_AllowsAMissingVersion() =>
        _update.TestValidate(new UpdateItemCommand(Guid.NewGuid(), "Name", null)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void List_RejectsAPageSizeOutOfRange(int pageSize) =>
        _list.TestValidate(new GetItemsQuery { PageSize = pageSize }).ShouldHaveValidationErrorFor(x => x.PageSize);
}
