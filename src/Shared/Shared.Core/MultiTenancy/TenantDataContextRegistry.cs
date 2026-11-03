namespace Shared.Core.MultiTenancy;

/// <summary>
/// Every registered <see cref="ITenantDataContext"/> type (filled by <c>AddCustomDbContext</c>). Used to migrate and
/// seed a dedicated tenant database, and to purge a tenant's rows, without knowing the modules.
/// </summary>
public sealed class TenantDataContextRegistry
{
    private readonly List<Type> _types = [];

    public IReadOnlyList<Type> ContextTypes => _types;

    internal void Add(Type contextType)
    {
        if (!_types.Contains(contextType))
        {
            _types.Add(contextType);
        }
    }
}
