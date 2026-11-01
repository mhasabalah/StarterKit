using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Attachment.Domains.Attachments.Features.UploadAttachment;

/// <summary>
/// Form-based request for file upload with automatic model binding
/// </summary>
public sealed class UploadAttachmentRequest
{
    [FromForm(Name = "file")]
    public required IFormFile File { get; init; }
    
    [FromForm(Name = "type")]
    public AttachmentType Type { get; init; }
    
    [FromForm(Name = "entityType")]
    public EntityType EntityType { get; init; }
    
    [FromForm(Name = "entityId")]
    public Guid EntityId { get; init; }
    
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
    string ContentType);

public class UploadAttachmentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/attachments/upload",
                async ([FromForm] UploadAttachmentRequest request, 
                       ISender sender,
                       IHttpContextService httpContextService,
                       CancellationToken cancellationToken) =>
                {
                    // Validate file presence
                    if (request.File == null || request.File.Length == 0)
                    {
                        return Results.BadRequest(ApiResponseExtensions.BadRequest(
                            "File is required and cannot be empty"));
                    }

                    // Validate file size (basic check, detailed validation in handler)
                    if (request.File.Length > 500 * 1024 * 1024) // 500MB hard limit
                    {
                        return Results.BadRequest(ApiResponseExtensions.BadRequest(
                            "File size exceeds maximum allowed size of 500MB"));
                    }

                    // Determine attachment type if not provided
                    var attachmentType = request.Type != default
                        ? request.Type
                        : DetermineAttachmentType(request.File.ContentType);

                    // Open file stream
                    using var fileStream = request.File.OpenReadStream();

                    // Build command
                    // Category is automatically calculated from EntityType + Type in domain layer
                    var command = new UploadAttachmentCommand(
                        FileName: request.File.FileName,
                        ContentType: request.File.ContentType,
                        FileSize: request.File.Length,
                        FileStream: fileStream,
                        Type: attachmentType,
                        EntityType: request.EntityType,
                        EntityId: request.EntityId,
                        Description: request.Description,
                        IsPublic: request.IsPublic,
                        GenerateThumbnail: request.GenerateThumbnail && attachmentType == AttachmentType.Image);

                    // Execute command
                    var result = await sender.Send(command, cancellationToken);

                    // Map to response
                    var response = new UploadAttachmentResponse(
                        result.Id,
                        result.FileName,
                        result.FileUrl,
                        result.ThumbnailUrl,
                        result.FileSize,
                        result.ContentType);

                    return Results.Ok(ApiResponseExtensions.Success(response, "File uploaded successfully"));
                })
            .WithName("UploadAttachment")
            .WithTags("Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .DisableAntiforgery()
            .Accepts<UploadAttachmentRequest>("multipart/form-data")
            .Produces<ApiResponse<UploadAttachmentResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithSummary("Upload file attachment")
            .WithDescription("Uploads a file attachment for an entity with optional thumbnail generation for images")
            .WithOpenApi()
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }

    /// <summary>
    /// Determines attachment type based on MIME type
    /// </summary>
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
