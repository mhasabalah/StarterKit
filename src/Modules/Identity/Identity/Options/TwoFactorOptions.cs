namespace Identity.Options;

/// <summary>How the "privileged roles must use 2FA" rule is applied by the permission/admin policies.</summary>
public enum TwoFactorEnforcementMode
{
    /// <summary>Requests from a privileged role without an MFA session are allowed, logged and flagged with a header.</summary>
    Warn,

    /// <summary>Requests from a privileged role without an MFA session get 403 <c>mfa_required</c>.</summary>
    Enforce
}

/// <summary>Admin two-factor authentication settings (section <c>Identity:TwoFactor</c>).</summary>
public class TwoFactorOptions
{
    public const string SectionName = "Identity:TwoFactor";

    public static readonly string[] DefaultRequiredForRoles =
        [RoleConstants.SuperAdmin, RoleConstants.Admin, RoleConstants.Finance];

    /// <summary>
    /// Roles whose callers must present an MFA session (token with <c>amr=mfa</c>) on permission and admin
    /// policies. Default: SuperAdmin, Admin, Finance. An empty array turns the rule off.
    /// (Null in config = default; arrays are not pre-filled because the config binder appends to them.)
    /// </summary>
    public string[]? RequiredForRoles { get; set; }

    /// <summary>Default: <see cref="TwoFactorEnforcementMode.Enforce"/> outside Development/Testing, Warn there.</summary>
    public TwoFactorEnforcementMode? EnforcementMode { get; set; }

    /// <summary>Lifetime of the login challenge token returned when a password login needs a second factor.</summary>
    public int ChallengeTokenLifetimeMinutes { get; set; } = 5;

    /// <summary>Recovery codes issued on enable/regenerate. Each is single-use; only hashes are stored.</summary>
    public int RecoveryCodeCount { get; set; } = 10;

    /// <summary>Issuer label shown in authenticator apps (<c>otpauth://totp/{Issuer}:{email}</c>).</summary>
    public string Issuer { get; set; } = "StarterKit";

    public IReadOnlyList<string> EffectiveRequiredForRoles => RequiredForRoles ?? DefaultRequiredForRoles;

    public TwoFactorEnforcementMode EffectiveEnforcementMode => EnforcementMode ?? TwoFactorEnforcementMode.Enforce;
}
