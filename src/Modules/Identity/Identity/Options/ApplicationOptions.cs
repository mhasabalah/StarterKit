namespace Identity.Options;

public class ApplicationOptions
{
    public const string SectionName = "Application";
    public string FrontendBaseUrl { get; init; } = "http://localhost:3000";
}
