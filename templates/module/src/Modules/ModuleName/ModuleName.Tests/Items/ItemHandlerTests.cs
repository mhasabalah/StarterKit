using ModuleName.Contracts.Items;
using ModuleName.Data.Repository;
using ModuleName.Domains.Items.Exceptions;
using ModuleName.Domains.Items.Features.CreateItem;
using ModuleName.Domains.Items.Features.DeleteItem;
using ModuleName.Domains.Items.Models;

namespace ModuleName.Tests.Items;

public class ItemHandlerTests
{
    private readonly Mock<IItemRepository> _repository = new();

    [Fact]
    public async Task Create_SavesTheNewItem_AndReturnsItsId()
    {
        Item? added = null;
        _repository.Setup(r => r.Add(It.IsAny<Item>())).Callback<Item>(i => added = i);

        var result = await new CreateItemHandler(_repository.Object, TestSupport.Localizer())
            .Handle(new CreateItemCommand("a1", "Name", null), CancellationToken.None);

        added.Should().NotBeNull();
        result.Id.Should().Be(added!.Id);
        added.Code.Should().Be("A1");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_WithATakenCode_IsAConflict_AndNothingIsSaved()
    {
        _repository.Setup(r => r.CodeExistsAsync("A1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => new CreateItemHandler(_repository.Object, TestSupport.Localizer())
            .Handle(new CreateItemCommand("a1", "Name", null), CancellationToken.None);

        (await act.Should().ThrowAsync<ItemAlreadyExistsException>()).WithMessage("*'A1'*");
        _repository.Verify(r => r.Add(It.IsAny<Item>()), Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_OfAMissingItem_IsNotFound()
    {
        var act = () => new DeleteItemHandler(_repository.Object)
            .Handle(new DeleteItemCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ItemNotFoundException>();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_RemovesTheItem_AndSaves()
    {
        var item = Item.Create("A1", "Name", null);
        _repository.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var result = await new DeleteItemHandler(_repository.Object)
            .Handle(new DeleteItemCommand(item.Id), CancellationToken.None);

        result.Deleted.Should().BeTrue();
        _repository.Verify(r => r.Remove(item), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
