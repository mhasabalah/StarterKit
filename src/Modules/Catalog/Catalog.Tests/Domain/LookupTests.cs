using Catalog.Contracts.Categories;
using Catalog.Domains.Categories.Events;
using Catalog.Domains.Categories.Exceptions;
using static Catalog.Tests.TestSupport;

namespace Catalog.Tests.Domain;

public class CategoryTests
{
    [Fact]
    public void Create_NormalizesSlug_IsActive_AndRaisesChangedEvent()
    {
        var category = Category.Create(Name("Phones", "هواتف"), "  Phones-2024 ");

        category.Slug.Should().Be("phones-2024");
        category.IsActive.Should().BeTrue();
        category.DomainEvents.Should().ContainSingle().Which.Should().Be(new CategoryChangedEvent(category.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("double--hyphen")]
    [InlineData("-leading")]
    [InlineData("emoji-é")]
    public void Create_WithInvalidSlug_Throws(string slug) =>
        FluentActions.Invoking(() => Category.Create(Name(), slug)).Should().Throw<InvalidCategoryException>();

    [Fact]
    public void Update_CannotBeItsOwnParent()
    {
        var category = Category.Create(Name(), "a");

        category.Invoking(c => c.Update(Name(), "a", category.Id, 0))
            .Should().Throw<InvalidCategoryException>().WithMessage("*own parent*");
    }

    [Fact]
    public void ActivateDeactivate_AreIdempotent_AndOnlyRaiseOnChange()
    {
        var category = Category.Create(Name(), "a");
        category.ClearDomainEvents();

        category.Activate();
        category.DomainEvents.Should().BeEmpty();

        category.Deactivate();
        category.Deactivate();
        category.IsActive.Should().BeFalse();
        category.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void Create_WithNegativeDisplayOrder_Throws() =>
        FluentActions.Invoking(() => Category.Create(Name(), "a", null, -1)).Should().Throw<InvalidCategoryException>();
}

public class CategoryHierarchyTests
{
    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid Child = Guid.NewGuid();
    private static readonly Guid GrandChild = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static readonly Dictionary<Guid, Guid?> Parents = new()
    {
        [Root] = null, [Child] = Root, [GrandChild] = Child, [Other] = null
    };

    [Fact]
    public void MovingUnderADescendant_IsACycle()
    {
        CategoryHierarchy.WouldCreateCycle(Root, GrandChild, Parents).Should().BeTrue();
        CategoryHierarchy.WouldCreateCycle(Root, Root, Parents).Should().BeTrue();
    }

    [Fact]
    public void MovingElsewhere_IsNotACycle()
    {
        CategoryHierarchy.WouldCreateCycle(GrandChild, Other, Parents).Should().BeFalse();
        CategoryHierarchy.WouldCreateCycle(Child, null, Parents).Should().BeFalse();
    }

    [Fact]
    public void ExistingCorruptLoop_IsReportedInsteadOfSpinning()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var loop = new Dictionary<Guid, Guid?> { [a] = b, [b] = a };

        CategoryHierarchy.WouldCreateCycle(Guid.NewGuid(), a, loop).Should().BeTrue();
    }

    [Fact]
    public void GetDescendantIds_IncludesRootAndAllLevels()
    {
        var ids = CategoryHierarchy.GetDescendantIds(Root, Parents.Select(p => (p.Key, p.Value)));

        ids.Should().BeEquivalentTo([Root, Child, GrandChild]);
    }

    [Fact]
    public void BuildTree_LocalizesSortsAndNests()
    {
        var nodes = new List<CategoryNodeSnapshot>
        {
            new(Root, null, "Electronics", "إلكترونيات", "electronics", 1),
            new(Other, null, "Home", "المنزل", "home", 0),
            new(Child, Root, "Phones", "هواتف", "phones", 0),
            new(GrandChild, Child, "Android", "أندرويد", "android", 0),
        };

        var english = CategoryHierarchy.BuildTree(nodes, arabic: false);
        english.Select(n => n.Name).Should().Equal("Home", "Electronics");
        english[1].Children.Single().Name.Should().Be("Phones");
        english[1].Children.Single().Children.Single().Name.Should().Be("Android");

        var arabic = CategoryHierarchy.BuildTree(nodes, arabic: true);
        arabic[1].Name.Should().Be("إلكترونيات");
    }

    [Fact]
    public void BuildTree_HidesSubtreeWhoseParentIsMissing()
    {
        var nodes = new List<CategoryNodeSnapshot>
        {
            new(Child, Root, "Phones", "هواتف", "phones", 0), // Root is inactive → not in the snapshot
            new(Other, null, "Home", "المنزل", "home", 0),
        };

        CategoryHierarchy.BuildTree(nodes, false).Should().ContainSingle().Which.Name.Should().Be("Home");
    }
}

public class AttributeTypeTests
{
    [Theory]
    [InlineData(AttributeDataType.Text, "Red", true)]
    [InlineData(AttributeDataType.Text, "  ", false)]
    [InlineData(AttributeDataType.Number, "12.5", true)]
    [InlineData(AttributeDataType.Number, "-3", true)]
    [InlineData(AttributeDataType.Number, "12,5", false)]
    [InlineData(AttributeDataType.Number, "1,000", false)]
    [InlineData(AttributeDataType.Number, "abc", false)]
    [InlineData(AttributeDataType.Boolean, "true", true)]
    [InlineData(AttributeDataType.Boolean, "FALSE", true)]
    [InlineData(AttributeDataType.Boolean, "yes", false)]
    public void IsValidValue_FollowsTheDataType(AttributeDataType type, string value, bool valid) =>
        AttributeType.IsValidValue(type, value).Should().Be(valid);

    [Fact]
    public void AppliesTo_NullMeansAllTypes()
    {
        AttributeType.Create(Name(), AttributeDataType.Text).AppliesTo(ProductType.Service).Should().BeTrue();

        var physicalOnly = AttributeType.Create(Name(), AttributeDataType.Text, null, [ProductType.Physical]);
        physicalOnly.AppliesTo(ProductType.Physical).Should().BeTrue();
        physicalOnly.AppliesTo(ProductType.Digital).Should().BeFalse();
    }

    [Fact]
    public void Create_DeduplicatesAndStoresApplicableTypesAsInts()
    {
        var type = AttributeType.Create(Name(), AttributeDataType.Number, " kg ",
            [ProductType.Service, ProductType.Physical, ProductType.Physical]);

        type.ApplicableTypes.Should().Equal(1, 3);
        type.Unit.Should().Be("kg");
    }

    [Fact]
    public void Create_EmptyApplicableTypes_MeansAll() =>
        AttributeType.Create(Name(), AttributeDataType.Text, null, []).ApplicableTypes.Should().BeNull();

    [Fact]
    public void Create_UnknownApplicableType_Throws() =>
        FluentActions.Invoking(() => AttributeType.Create(Name(), AttributeDataType.Text, null, [(ProductType)9]))
            .Should().Throw<BadRequestException>();

    [Fact]
    public void DeactivateActivate()
    {
        var type = AttributeType.Create(Name(), AttributeDataType.Text);
        type.Deactivate();
        type.IsActive.Should().BeFalse();
        type.Activate();
        type.IsActive.Should().BeTrue();
    }
}

public class TagTests
{
    [Fact]
    public void Create_TrimsName_AndIsActive()
    {
        var tag = Tag.Create(Name("  Sale ", " تخفيض "));

        tag.Name.Should().Be(LocalizedText.Create("Sale", "تخفيض"));
        tag.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Rename_RequiresBothLanguages() =>
        Tag.Create(Name()).Invoking(t => t.Rename(Name("x", ""))).Should().Throw<BadRequestException>();

    [Fact]
    public void BuildTree_ReturnsDtoType() =>
        CategoryHierarchy.BuildTree([], false).Should().BeOfType<List<CategoryTreeNodeDto>>();
}
