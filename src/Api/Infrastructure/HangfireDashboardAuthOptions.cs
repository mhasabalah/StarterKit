namespace Api.Infrastructure;

public class HangfireDashboardAuthOptions
{
    public const string SectionName = "Hangfire:DashboardAuth";

    public bool Enabled { get; set; } = true;

    public bool AllowLocalRequestsWithoutAuthentication { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }
}
