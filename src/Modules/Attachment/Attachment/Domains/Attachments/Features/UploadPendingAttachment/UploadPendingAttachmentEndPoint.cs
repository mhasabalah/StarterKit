using Attachment.Contracts.Attachments.Commands;
using Attachment.Domains.Attachments.Features.UploadAttachment;
using Attachment.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Response;

namespace Attachment.Domains.Attachments.Features.UploadPendingAttachment;

public sealed class UploadPendingAttachmentRequest
{
    [FromForm(Name = "file")]
    public required IFormFile File { get; init; }

    /// <summary>Server-issued session id. Omit it on the first upload to get a new session.</summary>
    [FromForm(Name = "sessionId")]
    public Guid? SessionId { get; init; }

    [FromForm(Name = "type")]
    public AttachmentType? Type { get; init; }

    [FromForm(Name = "category")]
    public AttachmentCategory Category { get; init; } = AttachmentCategory.General;

    [FromForm(Name = "displayOrder")]
    public int DisplayOrder { get; init; }

    [FromForm(Name = "isPrimary")]
    public bool IsPrimary { get; init; }
}

public sealed record UploadPendingAttachmentResponse(
    Guid Id,
    Guid SessionId,
    string FileName,
    string FileUrl,
    DateTime ExpiresAt,
    int DisplayOrder,
    bool IsPrimary);

public sealed record CreatePendingSessionResponse(Guid SessionId);

public class UploadPendingAttachmentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var maxBodyBytes = UploadLimits.MaxRequestBodyBytes(app.ServiceProvider);

        app.MapPost(EndpointConfig.BaseApiPath + "/attachments/pending/sessions",
                (IPendingSessionService sessions) =>
                    Results.Ok(ApiResponseExtensions.Success(
                        new CreatePendingSessionResponse(sessions.IssueForCurrentCaller()),
                        "Upload session created")))
            .WithName("CreatePendingAttachmentSession")
            .WithTags("Pending Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<CreatePendingSessionResponse>>(StatusCodes.Status200OK)
            .WithSummary("Create an upload session")
            .WithDescription(
                "Issues a session id bound to the caller (the user, or the anonymous client fingerprint). " +
                "Client-chosen session ids are rejected.")
            .AllowAnonymous()
            .RequireRateLimiting("upload")
            .HasApiVersion(1.0);

        app.MapPost(EndpointConfig.BaseApiPath + "/attachments/pending/upload",
                async ([FromForm] UploadPendingAttachmentRequest request,
                       ISender sender,
                       IPendingSessionService sessions,
                       CancellationToken cancellationToken) =>
                {
                    if (request.File is null || request.File.Length == 0)
                    {
                        return Results.BadRequest(ApiResponseExtensions.BadRequest(
                            "File is required and cannot be empty"));
                    }

                    var sessionId = request.SessionId is { } id && id != Guid.Empty
                        ? id
                        : sessions.IssueForCurrentCaller();

                    await using var fileStream = request.File.OpenReadStream();

                    var command = new UploadPendingAttachmentCommand(
                        SessionId: sessionId,
                        FileName: request.File.FileName,
                        ContentType: request.File.ContentType,
                        FileSize: request.File.Length,
                        FileStream: fileStream,
                        Type: request.Type ?? AttachmentType.Other,
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
            .WithMetadata(new RequestSizeLimitAttribute(maxBodyBytes))
            .WithFormOptions(multipartBodyLengthLimit: maxBodyBytes)
            .Accepts<UploadPendingAttachmentRequest>("multipart/form-data")
            .Produces<ApiResponse<UploadPendingAttachmentResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)
            .WithSummary("Upload file to session (anonymous)")
            .WithDescription(
                "Uploads a file to a server-issued session before the owning entity exists. Omit sessionId to " +
                "start a new session (returned in the response).")
            .AllowAnonymous()
            .RequireRateLimiting("upload")
            .HasApiVersion(1.0);
    }
}
