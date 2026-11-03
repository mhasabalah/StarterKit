namespace Catalog.Contracts.Products.Dtos;

/// <summary>Owner view ("my products"): the public view plus workflow state and reviewer feedback.</summary>
public record MyProductDto : ProductDto
{
    public ProductStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public string? ReviewNotes { get; init; }
    public DateTime? SubmittedForReviewAt { get; init; }
}

/// <summary>Aggregates over all of the current user's non-deleted products.</summary>
/// <param name="InReview">PendingReview + Approved (waiting on an admin).</param>
/// <param name="NeedsAttention">ChangesRequested + Rejected (waiting on the owner).</param>
public record MyProductStatsDto(
    int Total,
    int Drafts,
    int InReview,
    int NeedsAttention,
    int Published,
    int Archived,
    int TotalViews);
