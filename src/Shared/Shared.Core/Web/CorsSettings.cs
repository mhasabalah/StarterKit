namespace Shared.Core.Web;

public class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "DefaultCorsPolicy";

    public string[] AllowedOrigins { get; set; } = [];
    public bool AllowCredentials { get; set; } = true;
    public string[] AllowedMethods { get; set; } = ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"];
    public string[] AllowedHeaders { get; set; } = ["*"];
}
