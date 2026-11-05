namespace Identity.Options;

/// <summary>
/// External authentication providers settings
/// </summary>
public class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public GoogleOptions Google { get; init; } = new();
    public FacebookOptions Facebook { get; init; } = new();
    public bool SaveTokens { get; init; } = true;
}

public class GoogleOptions
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

public class FacebookOptions
{
    public string AppId { get; init; } = string.Empty;
    public string AppSecret { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}
