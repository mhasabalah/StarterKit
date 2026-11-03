namespace Catalog.Contracts.Products.Commands;

public record UploadProductImageCommand(
    Guid ProductId,
    string FileName,
    string ContentType,
    long FileSize,
    Stream FileStream,
    string? Caption = null,
    bool IsPrimary = false)
    : ICommand<UploadProductImageResult>;

public record UploadProductImageResult(Guid ImageId, Guid AttachmentId, string Url, bool IsPrimary);

public record SetPrimaryProductImageCommand(Guid ProductId, Guid ImageId)
    : ICommand<SetPrimaryProductImageResult>;

public record SetPrimaryProductImageResult(bool Success);

public record DeleteProductImageCommand(Guid ProductId, Guid ImageId)
    : ICommand<DeleteProductImageResult>;

public record DeleteProductImageResult(bool Success);

/// <param name="OrderedImageIds">Every image id of the product, in the desired display order.</param>
public record ReorderProductImagesCommand(Guid ProductId, List<Guid> OrderedImageIds)
    : ICommand<ReorderProductImagesResult>;

public record ReorderProductImagesResult(bool Success);
