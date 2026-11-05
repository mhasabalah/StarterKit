using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Shared.Core.Exceptions;
using Shared.Core.Resources;

namespace Shared.Core.BotProtection;

public static class BotProtectionExtensions
{
    /// <summary>Header carrying the widget token (keeps request DTOs unchanged).</summary>
    public const string TokenHeader = "X-Captcha-Token";

    public const string CaptchaRequiredCode = "captcha_required";
    public const string CaptchaInvalidCode = "captcha_invalid";
    public const string CaptchaUnavailableCode = "captcha_unavailable";

    /// <summary>
    /// Registers options (validated on start) and the <see cref="IHumanVerifier"/> selected by
    /// <c>BotProtection:Enabled</c> / <c>BotProtection:Provider</c>. A new provider is one more case in the switch.
    /// </summary>
    public static IServiceCollection AddBotProtection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BotProtectionOptions>()
            .Bind(configuration.GetSection(BotProtectionOptions.SectionName))
            .Validate(o => !o.Enabled || o.Provider != BotProtectionProvider.None,
                "BotProtection:Provider must not be None when BotProtection:Enabled is true.")
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.SecretKey),
                "BotProtection:SecretKey is required when BotProtection:Enabled is true.")
            .Validate(o => o.TimeoutSeconds is >= 1 and <= 30,
                "BotProtection:TimeoutSeconds must be between 1 and 30.")
            .ValidateOnStart();

        services.TryAddSingleton<BotProtectionMetrics>();

        var section = configuration.GetSection(BotProtectionOptions.SectionName);
        var enabled = section.GetValue<bool>(nameof(BotProtectionOptions.Enabled));
        var provider = section.GetValue<BotProtectionProvider?>(nameof(BotProtectionOptions.Provider)) ?? BotProtectionProvider.Turnstile;

        if (!enabled)
        {
            services.TryAddSingleton<IHumanVerifier, NoOpHumanVerifier>();
            return services;
        }

        switch (provider)
        {
            case BotProtectionProvider.Turnstile:
                services.AddHttpClient<IHumanVerifier, TurnstileHumanVerifier>(c => c.Timeout = TimeSpan.FromSeconds(35));
                break;
            default:
                services.TryAddSingleton<IHumanVerifier, NoOpHumanVerifier>();
                break;
        }

        return services;
    }

    /// <summary>
    /// Endpoint requires a verified human: the <c>X-Captcha-Token</c> header is checked through
    /// <see cref="IHumanVerifier"/> before the handler runs. Missing token: 400 <c>captcha_required</c>; rejected token:
    /// 400 <c>captcha_invalid</c>; provider down with <c>FailOpen=false</c>: 503 <c>captcha_unavailable</c>. A no-op
    /// while <c>BotProtection:Enabled=false</c> or when <paramref name="action"/> is not in <c>BotProtection:Actions</c>.
    /// Use on anonymous endpoints that create accounts, send mail/SMS or check passwords; complements rate limiting.
    /// </summary>
    public static TBuilder RequireHumanVerification<TBuilder>(this TBuilder builder, string action) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var services = http.RequestServices;

            var options = services.GetRequiredService<IOptions<BotProtectionOptions>>().Value;
            if (!options.Protects(action))
                return await next(context);

            var verifier = services.GetRequiredService<IHumanVerifier>();
            var token = http.Request.Headers[TokenHeader].ToString();
            var remoteIp = http.Connection.RemoteIpAddress?.ToString();

            var result = await verifier.VerifyAsync(string.IsNullOrWhiteSpace(token) ? null : token.Trim(), remoteIp, action, http.RequestAborted);
            if (result.Success)
                return await next(context);

            var localizer = services.GetService<IStringLocalizer<SharedResource>>();
            string Text(string key, string fallback) => localizer?[key].Value ?? fallback;

            if (result.IsMissingToken)
                throw new BadRequestException(Text("BotProtection.Required", "Please complete the human verification and try again."))
                {
                    ErrorCode = CaptchaRequiredCode
                };

            if (result.ProviderUnavailable)
                throw new CustomException(Text("BotProtection.Unavailable", "Human verification is temporarily unavailable. Please try again shortly."),
                    HttpStatusCode.ServiceUnavailable)
                {
                    ErrorCode = CaptchaUnavailableCode
                };

            throw new BadRequestException(Text("BotProtection.Invalid", "Human verification failed. Please try again."))
            {
                ErrorCode = CaptchaInvalidCode
            };
        });
}
