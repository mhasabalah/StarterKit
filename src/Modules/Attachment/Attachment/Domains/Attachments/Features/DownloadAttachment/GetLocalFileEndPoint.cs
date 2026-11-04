using Attachment.Infrastructure.Storage;
using Shared.Core.MultiTenancy.Web;

namespace Attachment.Domains.Attachments.Features.DownloadAttachment;

/// <summary>
/// Target of Local storage signed read URLs (<see cref="LocalReadUrlSigner"/>). The HMAC signature and expiry
/// are the authorization; it only exists for the Local provider (404 otherwise).
/// </summary>
public class GetLocalFileEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/attachments/files",
                async (HttpContext http, ILocalReadUrlSigner signer, IStorageService storage,
                    CancellationToken cancellationToken) =>
                {
                    if (!storage.ServesThroughApplication)
                        return Results.NotFound();

                    var q = http.Request.Query;
                    var request = signer.Verify(q["p"], q["e"], q["n"], q["t"], q["i"], q["s"], DateTimeOffset.UtcNow);
                    if (request is null)
                        return Results.StatusCode(StatusCodes.Status403Forbidden);

                    var stream = await storage.OpenReadAsync(request.StoragePath, cancellationToken);
                    return FileStreaming.Stream(http, stream,
                        request.ContentType ?? "application/octet-stream",
                        request.FileName ?? Path.GetFileName(request.StoragePath),
                        request.Inline, "private, no-store");
                })
            .WithName("GetLocalAttachmentFile")
            .WithTags("Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .ExcludeFromDescription()
            .AllowAnonymous()
            // The HMAC-signed path (already under the tenant's t/{id}/ root) is the authorization: no tenant lookup.
            .HostOnly()
            .DisableRequestTimeout()
            .HasApiVersion(1.0);
    }
}
