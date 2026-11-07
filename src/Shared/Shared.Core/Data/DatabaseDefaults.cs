namespace Shared.Core.Data;

/// <summary>
/// The shared database every module context uses by default: one source of truth for its connection-string name, so a
/// renamed product (<c>dotnet new starterkit -n Acme</c>) and a generated module (<c>dotnet new starterkit-module</c>)
/// agree without either knowing the other's name.
/// </summary>
public static class DatabaseDefaults
{
    /// <summary>
    /// <c>ConnectionStrings:{ConnectionStringName}</c>: the Aspire database resource, docker-compose and every
    /// <c>AddCustomDbContext</c> use it.
    /// </summary>
    public const string ConnectionStringName = "starterkit";

    /// <summary>
    /// Local-dev default for design-time factories (<c>dotnet ef migrations add</c>); never used at runtime and not a
    /// secret.
    /// </summary>
    public const string DesignTimeConnectionString =
        "Server=localhost;Port=5432;Database=starterkit;User Id=postgres;Password=postgres;Include Error Detail=true";
}
