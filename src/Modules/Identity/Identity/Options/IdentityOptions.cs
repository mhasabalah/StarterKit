namespace Identity.Options;

/// <summary>
/// ASP.NET Core Identity configuration options
/// </summary>
public class IdentityOptions
{
    public const string SectionName = "IdentitySettings";

    public PasswordOptions Password { get; init; } = new();
    public LockoutOptions Lockout { get; init; } = new();
    public UserOptions User { get; init; } = new();
    public SignInOptions SignIn { get; init; } = new();
}

public class PasswordOptions
{
    public bool RequireDigit { get; init; } = true;
    public int RequiredLength { get; init; } = 8;
    public bool RequireNonAlphanumeric { get; init; } = true;
    public bool RequireUppercase { get; init; } = true;
    public bool RequireLowercase { get; init; } = true;
    public int RequiredUniqueChars { get; init; } = 1;
}

public class LockoutOptions
{
    public int DefaultLockoutTimeSpanMinutes { get; init; } = 15;
    public int MaxFailedAccessAttempts { get; init; } = 5;
    public bool AllowedForNewUsers { get; init; } = true;
}

public class UserOptions
{
    public bool RequireUniqueEmail { get; init; } = true;
    public string AllowedUserNameCharacters { get; init; } =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
}

public class SignInOptions
{
    public bool RequireConfirmedEmail { get; init; }
    public bool RequireConfirmedPhoneNumber { get; init; }
}
