namespace Notification.Features.Deliveries;

public sealed record SendTestEmailRequest(
    string To,
    EmailTemplate Template,
    NotificationCulture Culture = NotificationCulture.En);

public sealed class SendTestEmailEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/notifications/email/test",
                async (
                    SendTestEmailRequest request,
                    EmailTemplateRegistry registry,
                    EmailTemplateSamples samples,
                    IEmailTemplateRenderer renderer,
                    IEmailService emailService,
                    CancellationToken ct) =>
                {
                    if (string.IsNullOrWhiteSpace(request.To))
                        return ApiResponseExtensions.BadRequest("Recipient email (To) is required.");

                    if (!Enum.IsDefined(request.Template))
                        return ApiResponseExtensions.BadRequest($"Unknown template '{request.Template}'.");

                    var subject = registry.ResolveSubject(request.Template, request.Culture);
                    var html = await renderer.RenderAsync(
                        FluidTemplateRenderer.GetTemplateFileName(request.Template),
                        samples.Build(request.Template),
                        request.Culture,
                        ct);
                    await emailService.SendAsync([request.To], $"[TEST] {subject}", html, ct: ct);

                    return Results.Ok(ApiResponseExtensions.Success(new
                    {
                        Message = $"Test email sent to {PiiMask.Email(request.To)}",
                        Template = request.Template.ToString(),
                        Culture = request.Culture.ToString()
                    }));
                })
            .WithName("SendTestEmail")
            .WithApiVersionSet(app.NewApiVersionSet("Notifications").Build())
            .WithTags("Notifications")
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Send a test email")
            .WithDescription("Admin: render any email template with sample data and send it synchronously through the configured transport (bypasses the dispatch queue and the delivery log).")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
