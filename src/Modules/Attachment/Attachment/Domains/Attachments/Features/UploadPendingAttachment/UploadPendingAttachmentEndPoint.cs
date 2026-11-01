using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Attachment.Domains.Attachments.Features.UploadPendingAttachment;

public sealed class UploadPendingAttachmentRequest
{
    [FromForm(Name = "file")]
    public required IFormFile File { get; init; }

    [FromForm(Name = "sessionId")]
    public required Guid SessionId { get; init; }

    [FromForm(Name = "type")]
    public AttachmentType Type { get; init; }

    [FromForm(Name = "category")]
    public AttachmentCategory Category { get; init; } = AttachmentCategory.General;

    [FromForm(Name = "displayOrder")]
    public int DisplayOrder { get; init; } = 0;

    [FromForm(Name = "isPrimary")]
    public bool IsPrimary { get; init; } = false;
}

public sealed record UploadPendingAttachmentResponse(
    Guid Id,
    Guid SessionId,
    string FileName,
    string FileUrl,
    DateTime ExpiresAt,
    int DisplayOrder,
    bool IsPrimary);

public class UploadPendingAttachmentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/attachments/pending/upload",
                async ([FromForm] UploadPendingAttachmentRequest request,
                       ISender sender,
                       CancellationToken cancellationToken) =>
                {
                    if (request.File == null || request.File.Length == 0)
                    {
                        return Results.BadRequest(ApiResponseExtensions.BadRequest(
                            "File is required and cannot be empty"));
                    }

                    if (request.File.Length > 500 * 1024 * 1024)
                    {
                        return Results.BadRequest(ApiResponseExtensions.BadRequest(
                            "File size exceeds maximum allowed size of 500MB"));
                    }

                    var attachmentType = request.Type != default
                        ? request.Type
                        : DetermineAttachmentType(request.File.ContentType);

                    using var fileStream = request.File.OpenReadStream();

                    var command = new UploadPendingAttachmentCommand(
                        SessionId: request.SessionId,
                        FileName: request.File.FileName,
                        ContentType: request.File.ContentType,
                        FileSize: request.File.Length,
                        FileStream: fileStream,
                        Type: attachmentType,
                        Category: request.Category,
                        DisplayOrder: request.DisplayOrder,
                        IsPrimary: request.IsPrimary);

                    var result = await sender.Send(command, cancellationToken);

                    var response = new UploadPendingAttachmentResponse(
                        result.Id,
                        result.SessionId,
                        result.FileName,
                        result.FileUrl,
                        result.ExpiresAt,
                        result.DisplayOrder,
                        request.IsPrimary);

                    return Results.Ok(ApiResponseExtensions.Success(response, "File uploaded successfully to session"));
                })
            .WithName("UploadPendingAttachment")
            .WithTags("Pending Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .DisableAntiforgery()
            .Accepts<UploadPendingAttachmentRequest>("multipart/form-data")
            .Produces<ApiResponse<UploadPendingAttachmentResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithSummary("Upload file to session (anonymous)")
            .WithDescription("Uploads a file to a temporary session before the owning entity is created. Max 20 files per session.")
            .WithOpenApi()
            .AllowAnonymous()
            .RequireRateLimiting("upload")
            .HasApiVersion(1.0);
    }

    private static AttachmentType DetermineAttachmentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            var ct when ct.StartsWith("image/") => AttachmentType.Image,
            var ct when ct.StartsWith("video/") => AttachmentType.Video,
            var ct when ct.StartsWith("audio/") => AttachmentType.Audio,
            "application/pdf" or
            "application/msword" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
            "application/vnd.ms-excel" or
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or
            "application/vnd.ms-powerpoint" or
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => AttachmentType.Document,
            _ => AttachmentType.Other
        };
    }
}
