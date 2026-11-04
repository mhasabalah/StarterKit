namespace Subscriptions.Domains.OnlinePayments.Features.ReceivePaymentWebhook;

public class ReceivePaymentWebhookEndPoint : ICarterModule
{
    private const int MaxBodyBytes = 256 * 1024;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/webhooks/payments/{provider}",
                async (string provider, HttpRequest request, ISender sender, CancellationToken ct) =>
                {
                    var rawBody = await ReadBodyAsync(request, ct);
                    var headers = request.Headers.ToDictionary(
                        x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);

                    var result = await sender.Send(new ReceivePaymentWebhookCommand(provider, rawBody, headers), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            // Called by the provider, not a user: authenticity is the signature, checked in the handler.
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicyNames.Webhook)
            .WithName("ReceivePaymentWebhook")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<ReceivePaymentWebhookResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .HasApiVersion(1.0);
    }

    /// <summary>The body EXACTLY as sent (the signature covers these bytes), capped.</summary>
    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > MaxBodyBytes)
            throw new BadRequestException("Webhook payload is too large.");

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBodyBytes)
                throw new BadRequestException("Webhook payload is too large.");
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
