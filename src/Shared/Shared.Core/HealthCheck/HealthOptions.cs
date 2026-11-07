namespace Shared.Core.HealthCheck;

public class HealthOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Connection string checked by the database readiness check (<c>ConnectionStrings:{name}</c>).</summary>
    public string DatabaseConnectionStringName { get; set; } = Shared.Core.Data.DatabaseDefaults.ConnectionStringName;
}
