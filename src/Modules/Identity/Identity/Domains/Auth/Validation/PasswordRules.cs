namespace Identity.Domains.Auth.Validation;

public static class PasswordRules
{
    /// <summary>Password complexity used by reset and change-password (mirrors IdentitySettings:Password defaults).</summary>
    public static IRuleBuilderOptions<T, string> ApplyPasswordRules<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .Matches(@"[A-Z]").WithMessage("Password must contain an uppercase letter")
            .Matches(@"[a-z]").WithMessage("Password must contain a lowercase letter")
            .Matches(@"[0-9]").WithMessage("Password must contain a digit")
            .Matches(@"[\W_]").WithMessage("Password must contain a special character");
}
