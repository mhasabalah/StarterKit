namespace Identity.Services.Passkeys;

/// <summary>Maps <c>Identity:Passkeys</c> onto ASP.NET Core Identity's <see cref="IdentityPasskeyOptions"/>.</summary>
public static class PasskeyConfiguration
{
    public static void Apply(IdentityPasskeyOptions options, Options.PasskeyOptions settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ServerDomain))
        {
            options.ServerDomain = settings.ServerDomain.Trim();
        }

        options.AuthenticatorTimeout = TimeSpan.FromMinutes(Math.Clamp(settings.CeremonyTimeoutMinutes, 1, 15));

        // Biometric/PIN on every ceremony: this is what makes a passkey sign-in multi-factor.
        options.UserVerificationRequirement = "required";

        // Discoverable credentials enable usernameless sign-in and autofill (conditional UI).
        options.ResidentKeyRequirement = "required";

        // No attestation: we do not restrict authenticator models, and "none" avoids collecting device identifiers.
        options.AttestationConveyancePreference = "none";

        var allowed = settings.AllowedOrigins
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim().TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allowed.Count > 0)
        {
            // Our frontends run on their own origins (SPA, admin portal); the API origin alone would reject them.
            // Cross-origin iframes are never accepted.
            options.ValidateOrigin = context => ValueTask.FromResult(
                !context.CrossOrigin && allowed.Contains(context.Origin.TrimEnd('/')));
        }
    }
}
