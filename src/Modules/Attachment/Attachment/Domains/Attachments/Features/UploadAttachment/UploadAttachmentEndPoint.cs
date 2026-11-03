using Attachment.Contracts.Attachments.Commands;
using Attachment.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Response;

namespace Attachment.Domains.Attachments.Features.UploadAttachment;

/// <summary>
/// Form-based request for file upload with automatic model binding
/// </summary>
public sealed class UploadAttachmentRequest
{
    [FromForm(Name = "file")]
    public required IFormFile File { get; init; }

    /// <summary>Optional; when omitted the server derives it from the file content.</summary>
    [FromForm(Name = "type")]
    public AttachmentType? Type { get; init; }

    [FromForm(Name = "entityType")]
    public EntityType? EntityType { get; init; }

    [FromForm(Name = "entityId")]
    public Guid? EntityId { get; init; }

    [FromForm(Name = "description")]
    public string? Description { get; init; }

    [FromForm(Name = "isPublic")]
    public bool IsPublic { get; init; }

    [FromForm(Name = "generateThumbnail")]
    public bool GenerateThumbnail { get; init; } = true;
}

public sealed record UploadAttachmentResponse(
    Guid Id,
    string FileName,
    string FileUrl,
    string? ThumbnailUrl,
    long FileSize,
    string ContentType,
    bool ThumbnailPending = false);

public class UploadAttachmentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var maxBodyBytes = UploadLimits.MaxRequestBodyBytes(app.ServiceProvider);

        app.MapPost(EndpointConfig.BaseApiPath + "/attachments/upload",
                async ([FromForm] UploadAttachmentRequest request,
                       ISender sender,
                       CancellationToken cancellationToken) =>
                {
                    if (request.File is null || request.File.Length == 0)
                    {
                        return Results.BadRequest(ApiResponseExtensions.BadRequest(
                            "File is required and cannot be empty"));
                    }

                    await using var fileStream = request.File.OpenReadStream();

                    var command = new UploadAttachmentCommand(
                        FileName: request.File.FileName,
                        ContentType: request.File.ContentType,
                        FileSize: request.File.Length,
                        FileStream: fileStream,
                        Type: request.Type ?? AttachmentType.Other, // Other = server decides from content
                        EntityType: request.EntityType,
                        EntityId: request.EntityType.HasValue ? request.EntityId : null,
                        Description: request.Description,
                        IsPublic: request.IsPublic,
                        GenerateThumbnail: request.GenerateThumbnail)
                    {
                        EnforceEntityAccessPolicy = true
                    };

                    var result = await sender.Send(command, cancellationToken);

                    var response = new UploadAttachmentResponse(
                        result.Id,
                        result.FileName,
                        result.FileUrl,
                        result.ThumbnailUrl,
                        result.FileSize,
                        result.ContentType,
                        result.ThumbnailPending);

                    return Results.Ok(ApiResponseExtensions.Success(response, "File uploaded successfully"));
                })
            .WithName("UploadAttachment")
            .WithTags("Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(maxBodyBytes))
            .WithFormOptions(multipartBodyLengthLimit: maxBodyBytes)
            .Accepts<UploadAttachmentRequest>("multipart/form-data")
            .Produces<ApiResponse<UploadAttachmentResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .WithSummary("Upload file attachment")
            .WithDescription(
                "Uploads a file. The real type is detected from the content; attaching to an entity requires " +
                "the entity type's access policy to allow the caller.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}

/// <summary>Request body limits for the upload endpoints, derived from the configured max file size.</summary>
public static class UploadLimits
{
    /// <summary>Multipart framing + other form fields.</summary>
    private const long MultipartOverheadBytes = 64 * 1024;

    public static long MaxRequestBodyBytes(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<AttachmentOptions>>().Value;
        return options.Validation.MaxUploadBytes + MultipartOverheadBytes;
    }
}
