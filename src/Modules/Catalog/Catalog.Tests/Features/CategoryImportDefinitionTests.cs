using Catalog.Data.Repository;
using Catalog.Domains.Categories.DataTransfer;
using Catalog.Domains.Categories.Models;
using DataTransfer.Contracts.Definitions;
using DataTransfer.Contracts.Enums;
using Moq;

namespace Catalog.Tests.Features;

/// <summary>catalog.categories import rules without a database: parsing, slug upsert, parent resolution, loops, dry run.</summary>
public sealed class CategoryImportDefinitionTests
{
    private readonly Mock<ICategoryRepository> _repository = new();
    private readonly List<Category> _added = [];
    private readonly List<Category> _stored = [];

    private CategoryImportDefinition Sut()
    {
        _repository.Setup(r => r.GetBySlugsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<string> slugs, CancellationToken _) =>
                _stored.Where(c => slugs.Contains(c.Slug)).ToList());
        _repository.Setup(r => r.GetParentMapAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _stored.ToDictionary(c => c.Id, c => c.ParentId));
        _repository.Setup(r => r.Add(It.IsAny<Category>())).Callback<Category>(_added.Add);
        return new CategoryImportDefinition(_repository.Object, TestSupport.Localizer());
    }

    private Category Stored(string slug, Guid? parentId = null, int order = 0)
    {
        var category = Category.Create(TestSupport.Name(slug, slug + "-ar"), slug, parentId, order);
        category.ClearDomainEvents();
        _stored.Add(category);
        return category;
    }

    private static ImportRecord Row(long row, string? slug, string? nameEn = "Name", string? nameAr = "name-ar",
        string? parent = null, string? order = null, string? active = null) =>
        new(row, new Dictionary<string, string?>
        {
            [CategoryTransferColumns.Slug] = slug,
            [CategoryTransferColumns.NameEn] = nameEn,
            [CategoryTransferColumns.NameAr] = nameAr,
            [CategoryTransferColumns.Parent] = parent,
            [CategoryTransferColumns.DisplayOrder] = order,
            [CategoryTransferColumns.Active] = active
        });

    private static ImportContext Context(bool dryRun = false) => new(Guid.NewGuid(), Guid.NewGuid(), dryRun, "en");

    [Fact]
    public async Task A_child_after_its_parent_in_the_same_batch_resolves_the_parent_and_saves_once()
    {
        var results = await Sut().ApplyBatchAsync(
            [Row(2, "apparel"), Row(3, "shirts", parent: "apparel", order: "2")], Context(), default);

        results.Select(r => r.Outcome).Should().Equal(ImportRowOutcome.Created, ImportRowOutcome.Created);
        _added.Should().HaveCount(2);
        _added[1].ParentId.Should().Be(_added[0].Id);
        _added[1].DisplayOrder.Should().Be(2);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_parent_that_is_only_in_a_previous_batch_resolves_from_the_database()
    {
        var parent = Stored("apparel");

        var results = await Sut().ApplyBatchAsync([Row(2, "shirts", parent: "Apparel")], Context(), default);

        results.Single().Outcome.Should().Be(ImportRowOutcome.Created);
        _added.Single().ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task A_child_before_its_parent_or_an_unknown_parent_fails_that_row_only()
    {
        var results = await Sut().ApplyBatchAsync(
            [Row(2, "shirts", parent: "apparel"), Row(3, "apparel"), Row(4, "hats", parent: "nope")], Context(), default);

        results[0].Errors.Single().Should().Match<ImportRowError>(e => e.Column == "parent" && e.Code == "parent_not_found");
        results[1].Outcome.Should().Be(ImportRowOutcome.Created);
        results[2].Errors.Single().Code.Should().Be("parent_not_found");
        _added.Select(c => c.Slug).Should().Equal("apparel");
    }

    [Fact]
    public async Task Slugs_are_normalised_and_match_the_existing_category_which_is_then_updated_in_place()
    {
        var shoes = Stored("shoes");

        var results = await Sut().ApplyBatchAsync([Row(2, "  SHOES ", nameEn: "Footwear", order: "5")], Context(), default);

        results.Single().Outcome.Should().Be(ImportRowOutcome.Updated);
        _added.Should().BeEmpty();
        shoes.Name.En.Should().Be("Footwear");
        shoes.DisplayOrder.Should().Be(5);
        shoes.Slug.Should().Be("shoes");
    }

    [Fact]
    public async Task Identical_values_are_unchanged_and_raise_no_event()
    {
        var shoes = Stored("shoes", order: 3);

        var results = await Sut().ApplyBatchAsync(
            [Row(2, "shoes", nameEn: "shoes", nameAr: "shoes-ar", order: "3", active: "true")], Context(), default);

        results.Single().Outcome.Should().Be(ImportRowOutcome.Unchanged);
        shoes.DomainEvents.Should().BeEmpty();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Empty_order_and_active_keep_the_current_values()
    {
        var shoes = Stored("shoes", order: 7);
        shoes.Deactivate();

        var results = await Sut().ApplyBatchAsync([Row(2, "shoes", nameEn: "Footwear")], Context(), default);

        results.Single().Outcome.Should().Be(ImportRowOutcome.Updated);
        shoes.DisplayOrder.Should().Be(7);
        shoes.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Moving_a_category_under_its_own_descendant_or_itself_is_a_cycle_error_and_changes_nothing()
    {
        var root = Stored("root");
        var child = Stored("child", root.Id);
        var grandchild = Stored("grandchild", child.Id);

        var results = await Sut().ApplyBatchAsync(
            [Row(2, "root", nameEn: "root", nameAr: "root-ar", parent: "grandchild"),
             Row(3, "child", nameEn: "child", nameAr: "child-ar", parent: "child")], Context(), default);

        results.Select(r => r.Errors.SingleOrDefault()?.Code).Should().Equal("parent_cycle", "parent_cycle");
        root.ParentId.Should().BeNull();
        grandchild.ParentId.Should().Be(child.Id);
    }

    [Fact]
    public async Task Two_rows_cannot_build_a_loop_together()
    {
        Stored("a");
        Stored("b");

        var results = await Sut().ApplyBatchAsync(
            [Row(2, "a", nameEn: "a", nameAr: "a-ar", parent: "b"), Row(3, "b", nameEn: "b", nameAr: "b-ar", parent: "a")],
            Context(), default);

        results.Select(r => r.Outcome).Should().Equal(ImportRowOutcome.Updated, ImportRowOutcome.Failed);
        results[1].Errors.Single().Code.Should().Be("parent_cycle");
    }

    [Fact]
    public async Task Invalid_cells_fail_their_row_with_the_column_and_valid_rows_still_save()
    {
        var results = await Sut().ApplyBatchAsync(
            [Row(2, "Bad Slug!"), Row(3, "ok-one", nameAr: null), Row(4, "ok-two", order: "x"),
             Row(5, "ok-three", active: "maybe"), Row(6, "ok-four", order: "-1"), Row(7, "good")], Context(), default);

        results[0].Errors.Single().Column.Should().Be("slug");
        results[1].Errors.Single().Should().Match<ImportRowError>(e => e.Column == "name_ar" && e.Code == "required");
        results[2].Errors.Single().Should().Match<ImportRowError>(e => e.Column == "display_order" && e.Code == "invalid_number");
        results[3].Errors.Single().Should().Match<ImportRowError>(e => e.Column == "active" && e.Code == "invalid_value");
        results[4].Errors.Single().Column.Should().Be("display_order");
        results[5].Outcome.Should().Be(ImportRowOutcome.Created);
        _added.Select(c => c.Slug).Should().Equal("good");
    }

    [Fact]
    public async Task A_dry_run_reports_the_same_outcomes_but_adds_and_saves_nothing()
    {
        Stored("old");

        var results = await Sut().ApplyBatchAsync(
            [Row(2, "fresh"), Row(3, "kid", parent: "fresh"), Row(4, "old", nameEn: "Renamed")], Context(dryRun: true), default);

        results.Select(r => r.Outcome).Should().Equal(
            ImportRowOutcome.Created, ImportRowOutcome.Created, ImportRowOutcome.Updated);
        _repository.Verify(r => r.Add(It.IsAny<Category>()), Times.Never);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_slug_repeated_in_the_batch_updates_the_category_created_earlier_in_it()
    {
        var results = await Sut().ApplyBatchAsync(
            [Row(2, "dup", nameEn: "First"), Row(3, "dup", nameEn: "Second")], Context(), default);

        results.Select(r => r.Outcome).Should().Equal(ImportRowOutcome.Created, ImportRowOutcome.Updated);
        _added.Should().ContainSingle().Which.Name.En.Should().Be("Second");
    }

    [Fact]
    public async Task Active_false_creates_an_inactive_category()
    {
        await Sut().ApplyBatchAsync([Row(2, "hidden", active: "no")], Context(), default);

        _added.Single().IsActive.Should().BeFalse();
    }

    [Fact]
    public void The_file_contract_is_bilingual_stable_and_requires_the_permission_it_documents()
    {
        CategoryTransferColumns.All.Select(c => c.Name).Should().Equal(
            "slug", "name_en", "name_ar", "parent", "display_order", "active");
        CategoryTransferColumns.All.Should().OnlyContain(c =>
            !string.IsNullOrWhiteSpace(c.Header.En) && !string.IsNullOrWhiteSpace(c.Header.Ar));
        CategoryTransferColumns.All.Single(c => c.Name == "display_order").Kind.Should().Be(TransferColumnKind.Number);
        Sut().RequiredPermission.Should().Be(Shared.Core.MultiTenancy.Authorization.TenantPermissions.CategoriesManage);
    }

    [Fact]
    public async Task A_name_over_the_limit_fails_on_its_column_and_the_limit_itself_is_accepted()
    {
        var results = await Sut().ApplyBatchAsync(
            [Row(2, "too-long", nameEn: new string('x', Category.NameMaxLength + 1)),
             Row(3, "just-fits", nameAr: new string('y', Category.NameMaxLength))], Context(), default);

        results[0].Errors.Should().Contain(e => e.Column == "name_en");
        results[1].Outcome.Should().Be(ImportRowOutcome.Created);
    }

    [Fact]
    public async Task Messages_follow_the_ui_culture_of_the_requester()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ar");
        try
        {
            var results = await Sut().ApplyBatchAsync(
                [Row(2, "kid", parent: "ghost"), Row(3, "bad-order", order: "x"), Row(4, "bad-flag", active: "maybe"),
                 Row(5, "no-name", nameEn: null)], Context(), default);

            results[0].Errors.Single().Message.Should().Contain("لا يوجد تصنيف");
            results[1].Errors.Single().Message.Should().Contain("عددًا صحيحًا");
            results[2].Errors.Single().Message.Should().Contain("true أو false");
            results[3].Errors.Single().Message.Should().Contain("مطلوبة");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task A_slug_taken_by_a_concurrent_writer_fails_the_batch_and_the_replay_then_sees_it_as_existing()
    {
        var sut = Sut();
        _repository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Catalog.Domains.Categories.Exceptions.CategorySlugAlreadyExistsException("raced"));

        var first = () => sut.ApplyBatchAsync([Row(2, "raced", nameEn: "Raced", nameAr: "raced-ar")], Context(), default);
        await first.Should().ThrowAsync<Catalog.Domains.Categories.Exceptions.CategorySlugAlreadyExistsException>();

        Stored("raced"); // the other writer's row is now committed
        var replay = await Sut().ApplyBatchAsync([Row(2, "raced", nameEn: "raced", nameAr: "raced-ar")], Context(), default);

        replay.Single().Outcome.Should().Be(ImportRowOutcome.Unchanged);
    }
}
