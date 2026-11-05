using static Catalog.Tests.TestSupport;

namespace Catalog.Tests.Domain;

/// <summary>
/// The state machine, tested exhaustively: every (from, action) pair is either an allowed transition (and
/// lands in the right status with the right event) or throws <see cref="InvalidProductStatusTransitionException"/>
/// and leaves the product untouched.
/// </summary>
public class ProductWorkflowTests
{
    public enum WorkflowAction { Submit, Approve, Reject, RequestChanges, Publish, Archive, ReturnToDraft }

    private static readonly Dictionary<WorkflowAction, ProductStatus> Target = new()
    {
        [WorkflowAction.Submit] = ProductStatus.PendingReview,
        [WorkflowAction.Approve] = ProductStatus.Approved,
        [WorkflowAction.Reject] = ProductStatus.Rejected,
        [WorkflowAction.RequestChanges] = ProductStatus.ChangesRequested,
        [WorkflowAction.Publish] = ProductStatus.Published,
        [WorkflowAction.Archive] = ProductStatus.Archived,
        [WorkflowAction.ReturnToDraft] = ProductStatus.Draft,
    };

    public static TheoryData<ProductStatus, WorkflowAction> AllPairs()
    {
        var data = new TheoryData<ProductStatus, WorkflowAction>();
        foreach (var status in Enum.GetValues<ProductStatus>())
            foreach (var action in Enum.GetValues<WorkflowAction>())
                data.Add(status, action);
        return data;
    }

    private static void Apply(Product product, WorkflowAction action)
    {
        switch (action)
        {
            case WorkflowAction.Submit:
                product.SubmitForReview();
                break;
            case WorkflowAction.Approve:
                product.Approve(AdminId, "Looks good");
                break;
            case WorkflowAction.Reject:
                product.Reject(AdminId, "Bad photos");
                break;
            case WorkflowAction.RequestChanges:
                product.RequestChanges(AdminId, "Fix the title");
                break;
            case WorkflowAction.Publish:
                product.Publish();
                break;
            case WorkflowAction.Archive:
                product.Archive();
                break;
            case WorkflowAction.ReturnToDraft:
                product.ReturnToDraft();
                break;
        }
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void EveryTransition_IsAllowedOnlyWhenListed(ProductStatus from, WorkflowAction action)
    {
        var product = ProductIn(from);
        var target = Target[action];
        var act = () => Apply(product, action);

        if (ProductStatusTransitions.CanTransition(from, target))
        {
            act.Should().NotThrow();
            product.Status.Should().Be(target);
            product.DomainEvents.Should().ContainSingle();
        }
        else
        {
            act.Should().Throw<InvalidProductStatusTransitionException>()
                .Which.Should().Match<InvalidProductStatusTransitionException>(e => e.From == from && e.To == target);
            product.Status.Should().Be(from);
            product.DomainEvents.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(ProductStatus.Draft, ProductStatus.PendingReview)]
    [InlineData(ProductStatus.ChangesRequested, ProductStatus.PendingReview)]
    [InlineData(ProductStatus.Rejected, ProductStatus.PendingReview)]
    [InlineData(ProductStatus.PendingReview, ProductStatus.Approved)]
    [InlineData(ProductStatus.PendingReview, ProductStatus.Rejected)]
    [InlineData(ProductStatus.PendingReview, ProductStatus.ChangesRequested)]
    [InlineData(ProductStatus.Approved, ProductStatus.Published)]
    [InlineData(ProductStatus.Published, ProductStatus.Archived)]
    [InlineData(ProductStatus.Archived, ProductStatus.Draft)]
    public void AllowList_ContainsTheDocumentedTransitions(ProductStatus from, ProductStatus to) =>
        ProductStatusTransitions.CanTransition(from, to).Should().BeTrue();

    [Fact]
    public void AllowList_HasExactlyNineTransitions() =>
        Enum.GetValues<ProductStatus>().Sum(s => ProductStatusTransitions.AllowedFrom(s).Count).Should().Be(9);

    [Fact]
    public void Draft_CannotBePublishedDirectly()
    {
        var product = ProductIn(ProductStatus.Draft);

        product.Invoking(p => p.Publish()).Should().Throw<InvalidProductStatusTransitionException>();
    }

    [Fact]
    public void Submit_StampsSubmittedAt_AndRaisesEvent()
    {
        var product = ProductIn(ProductStatus.Draft);

        product.SubmitForReview();

        product.SubmittedForReviewAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        product.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new ProductSubmittedForReviewEvent(product.Id, OwnerId));
    }

    [Fact]
    public void Approve_RecordsReviewer()
    {
        var product = ProductIn(ProductStatus.PendingReview);

        product.Approve(AdminId, "  ok  ");

        product.ReviewedBy.Should().Be(AdminId);
        product.ReviewedAt.Should().NotBeNull();
        product.ReviewNotes.Should().Be("ok");
        product.DomainEvents.Should().ContainSingle().Which.Should().Be(new ProductApprovedEvent(product.Id, AdminId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_RequiresReason(string reason)
    {
        var product = ProductIn(ProductStatus.PendingReview);

        product.Invoking(p => p.Reject(AdminId, reason)).Should().Throw<InvalidProductException>();
        product.Status.Should().Be(ProductStatus.PendingReview);
    }

    [Fact]
    public void Reject_RecordsReasonInEvent()
    {
        var product = ProductIn(ProductStatus.PendingReview);

        product.Reject(AdminId, "Blurry photos");

        product.ReviewNotes.Should().Be("Blurry photos");
        product.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new ProductRejectedEvent(product.Id, AdminId, "Blurry photos"));
    }

    [Fact]
    public void RequestChanges_RequiresNotes() =>
        ProductIn(ProductStatus.PendingReview).Invoking(p => p.RequestChanges(AdminId, " "))
            .Should().Throw<InvalidProductException>();

    [Fact]
    public void Approve_WithoutReviewer_Throws() =>
        ProductIn(ProductStatus.PendingReview).Invoking(p => p.Approve(Guid.Empty))
            .Should().Throw<InvalidProductException>();

    [Fact]
    public void Approve_WithTooLongNotes_Throws() =>
        ProductIn(ProductStatus.PendingReview)
            .Invoking(p => p.Approve(AdminId, new string('n', Product.ReviewNotesMaxLength + 1)))
            .Should().Throw<InvalidProductException>();

    [Fact]
    public void Publish_StampsPublishedAt_AndRaisesIdOnlyEvent()
    {
        var product = ProductIn(ProductStatus.Approved);

        product.Publish();

        product.PublishedAt.Should().NotBeNull();
        product.DomainEvents.Should().ContainSingle().Which.Should()
            .Be(new ProductPublishedEvent(product.Id, OwnerId, product.Sku, product.PublishedAt!.Value));
    }

    [Fact]
    public void FullLifecycle_DraftToArchivedAndBackToDraft()
    {
        var product = NewProduct();

        product.SubmitForReview();
        product.RequestChanges(AdminId, "Add a description");
        product.SubmitForReview();
        product.Approve(AdminId);
        product.Publish();
        product.Archive();
        product.ReturnToDraft();

        product.Status.Should().Be(ProductStatus.Draft);
    }

    [Theory]
    [InlineData(ProductStatus.Draft, true)]
    [InlineData(ProductStatus.ChangesRequested, true)]
    [InlineData(ProductStatus.Rejected, true)]
    [InlineData(ProductStatus.PendingReview, false)]
    [InlineData(ProductStatus.Approved, false)]
    [InlineData(ProductStatus.Published, false)]
    [InlineData(ProductStatus.Archived, false)]
    public void EditableByOwner_MatchesTheSpec(ProductStatus status, bool editable) =>
        ProductStatusTransitions.EditableByOwner.Contains(status).Should().Be(editable);
}
