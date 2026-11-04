using Attachment.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;

namespace Attachment.Domains.Attachments.Features.DownloadAttachment;

/// <summary>
/// <c>GET /attachments/{id}/download[?thumbnail=true]</c>: access check, then a 302 to a short-lived signed URL
/// (S3/Azure) or the bytes streamed by the API (Local). Anonymous callers can read public files.
/// </summary>
public class DownloadAttachmentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + AttachmentUrls.DownloadRouteTemplate,
                async (Guid id, [FromQuery] bool? thumbnail, ISender sender, HttpContext http,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new DownloadAttachmentQuery(id, thumbnail == true), cancellationToken);

                    if (result.RedirectUrl is not null)
                    {
                        http.Response.Headers.CacheControl = "no-store";
                        return Results.Redirect(result.RedirectUrl);
                    }

                    return FileStreaming.Stream(http, result.Content!, result.ContentType, result.FileName,
                        result.Inline, result.IsPublic ? "public, max-age=300" : "private, no-store");
                })
            .WithName("DownloadAttachment")
            .WithTags("Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status302Found)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithSummary("Download an attachment")
            .WithDescription(
                "Checks access (public, owner, admin, or entity policy) and then redirects to a short-lived URL " +
                "or streams the file. Files are served with nosniff and Content-Disposition: attachment " +
                "(inline only for safe image types).")
            .AllowAnonymous()
            .DisableRequestTimeout()
            .HasApiVersion(1.0);
    }
}

/// <summary>Safe response headers for files served by the API itself.</summary>
public static class FileStreaming
{
    public static IResult Stream(HttpContext http, Stream content, string contentType, string fileName,
        bool inline, string cacheControl)
    {
        var headers = http.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentDisposition = FileResponseHeaders.ContentDisposition(fileName, inline);
        headers.CacheControl = cacheControl;
        // Even if a browser renders the file, it gets no script, no plugins and an opaque origin.
        headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";

        return Results.Stream(content, contentType, enableRangeProcessing: true);
    }
}
