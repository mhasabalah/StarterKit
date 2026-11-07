using ModuleName.Domains.Items.Events;
using ModuleName.Domains.Items.Exceptions;
using ModuleName.Domains.Items.Models;
#if (OUTBOX)
using ModuleName.Contracts.Items.IntegrationEvents;
#endif

namespace ModuleName.Tests.Items;

public class ItemTests
{
    [Fact]
    public void Create_NormalizesTheCode_TrimsTheDetails_AndRaisesItemCreated()
    {
        var item = Item.Create("  ab-1 ", "  First item ", "  ");

        item.Id.Should().NotBeEmpty();
        item.Code.Should().Be("AB-1");
        item.Name.Should().Be("First item");
        item.Description.Should().BeNull();
        item.DomainEvents.Should().ContainSingle().Which.Should().Be(new ItemCreatedEvent(item.Id, "AB-1"));
#if (OUTBOX)
        item.IntegrationEvents.Should().ContainSingle().Which.Should().BeOfType<ItemCreatedIntegrationEvent>()
            .Which.ItemId.Should().Be(item.Id);
#else
        item.IntegrationEvents.Should().BeEmpty();
#endif
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    public void Create_WithAnInvalidCode_IsRejected(string code)
    {
        var act = () => Item.Create(code, "Name", null);

        act.Should().Throw<InvalidItemException>();
    }

    [Fact]
    public void Create_WithATooLongName_IsRejected()
    {
        var act = () => Item.Create("A1", new string('n', Item.NameMaxLength + 1), null);

        act.Should().Throw<InvalidItemException>();
    }

    [Fact]
    public void Update_ChangesTheDetails_ButNeverTheCode()
    {
        var item = Item.Create("A1", "Old", null);

        item.Update(" New ", " Described ");

        item.Name.Should().Be("New");
        item.Description.Should().Be("Described");
        item.Code.Should().Be("A1");
    }

    [Fact]
    public void Update_WithAnEmptyName_IsRejected_AndNothingChanges()
    {
        var item = Item.Create("A1", "Old", "Kept");

        var act = () => item.Update(" ", null);

        act.Should().Throw<InvalidItemException>();
        item.Name.Should().Be("Old");
        item.Description.Should().Be("Kept");
    }
}
