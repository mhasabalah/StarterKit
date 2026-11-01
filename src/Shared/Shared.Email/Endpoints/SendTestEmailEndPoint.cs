using Shared.Email.Options;

namespace Shared.Email.Endpoints;

public sealed record SendTestEmailRequest(
    string To,
    EmailTemplate Template,
    EmailCulture Culture = EmailCulture.En);

public sealed class SendTestEmailEndPoint : ICarterModule
{
    // Sample data per template — the only thing that lives here.
    // Subjects are owned by the model via IEmailTemplate.GetSubject().
    // Links are built from Email:AppBaseUrl / Email:SupportEmail so no environment URL is hardcoded.
    private static IReadOnlyDictionary<EmailTemplate, Func<Dictionary<string, object>>> BuildSampleData(
        EmailOptions options)
    {
        var appBaseUrl = options.AppBaseUrl.TrimEnd('/');

        return new Dictionary<EmailTemplate, Func<Dictionary<string, object>>>
        {
            [EmailTemplate.PasswordResetRequired] = () => new PasswordResetRequiredModel
            {
                CompanyName = options.BrandName,
                ResetUrl = $"{appBaseUrl}/reset-password?token=test-token-abc123",
                ExpiresInHours = 24
            }.ToDictionary(),

            [EmailTemplate.SubscriptionExpired] = () => new SubscriptionExpiredModel
            {
                SubscriberName = "Acme Corp",
                PlanName = "Professional",
                GracePeriodEndsAt = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd"),
                RenewalUrl = $"{appBaseUrl}/renew"
            }.ToDictionary(),

            [EmailTemplate.SubscriptionExpiringReminder] = () => new SubscriptionExpiringReminderModel
            {
                SubscriberName = "Acme Corp",
                PlanName = "Professional",
                ExpiryDate = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"),
                RenewalUrl = $"{appBaseUrl}/renew"
            }.ToDictionary(),

            [EmailTemplate.SubscriptionHardLocked] = () => new SubscriptionHardLockedModel
            {
                SubscriberName = "Acme Corp",
                SupportEmail = options.SupportEmail
            }.ToDictionary(),

            [EmailTemplate.WelcomeUser] = () => new WelcomeUserModel
            {
                FirstName = "Ahmed"
            }.ToDictionary(),
        };
    }

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/email/test",
                async (
                    SendTestEmailRequest request,
                    EmailTemplateRegistry registry,
                    IOptions<EmailOptions> emailOptions,
                    IEmailTemplateRenderer renderer,
                    IEmailService emailService,
                    CancellationToken ct) =>
                {
                    if (string.IsNullOrWhiteSpace(request.To))
                        return ApiResponseExtensions.BadRequest("Recipient email (To) is required.");

                    if (!BuildSampleData(emailOptions.Value).TryGetValue(request.Template, out var modelFactory))
                        return ApiResponseExtensions.BadRequest(
                            $"No sample data registered for template '{request.Template}'.");

                    var subject = registry.ResolveSubject(request.Template, request.Culture);
                    var html = await renderer.RenderAsync(request.Template.ToString(), modelFactory(), request.Culture, ct);
                    await emailService.SendAsync([request.To], $"[TEST] {subject}", html, ct: ct);

                    return Results.Ok(ApiResponseExtensions.Success(new
                    {
                        Message = $"Test email sent to {request.To}",
                        Template = request.Template.ToString(),
                        Culture = request.Culture.ToString()
                    }));
                })
            .WithName("SendTestEmail")
            .WithApiVersionSet(app.NewApiVersionSet("Email").Build())
            .WithTags("Email")
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Send a test email")
            .WithDescription("Send a test email using any available template with sample data. Admin only.")
            .WithOpenApi()
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
