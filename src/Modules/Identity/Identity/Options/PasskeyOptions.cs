namespace Identity.Options;

/// <summary>
/// Passkey (WebAuthn / FIDO2) settings, section <c>Identity:Passkeys</c>. Verification itself is ASP.NET Core Identity's
/// built-in passkey handler (.NET 10); these values feed <see cref="Microsoft.AspNetCore.Identity.IdentityPasskeyOptions"/>
/// and our own ceremony/step-up rules.
/// </summary>
public sealed class PasskeyOptions
{
    public const string SectionName = "Identity:Passkeys";

    /// <summary>Master switch. When false every passkey endpoint returns 404 <c>passkeys_disabled</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Relying-party id (RP ID): the registrable domain the passkeys are bound to, e.g. <c>example.com</c> so
    /// <c>app.example.com</c> and <c>admin.example.com</c> share them. Never a URL, never a port. Changing it later
    /// orphans every registered passkey. Null = the request host (development only).
    /// </summary>
    public string? ServerDomain { get; set; }

    /// <summary>
    /// Exact origins (scheme + host + port) of the frontends allowed to run WebAuthn ceremonies, e.g.
    /// <c>https://app.example.com</c>. Empty = only the API's own origin (the built-in default).
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Lifetime of a registration/sign-in ceremony (options → browser prompt → verify). Default 5 min.</summary>
    public int CeremonyTimeoutMinutes { get; set; } = 5;

    /// <summary>
    /// Step-up rule for adding a passkey: the current session must have signed in within this many minutes
    /// (403 <c>reauthentication_required</c> otherwise). Stops a stolen long-lived session from planting a credential.
    /// </summary>
    public int FreshAuthMinutes { get; set; } = 15;

    /// <summary>Upper bound of passkeys per account (409 <c>passkey_limit_reached</c>).</summary>
    public int MaxPasskeysPerUser { get; set; } = 10;
}
