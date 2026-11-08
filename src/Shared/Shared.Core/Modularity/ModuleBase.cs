using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Shared.Core.Modularity;

/// <summary>Default <see cref="IModule"/> implementation; override what differs.</summary>
public abstract class ModuleBase : IModule
{
    /// <inheritdoc />
    public virtual string Name => GetType().Assembly.GetName().Name!;

    /// <inheritdoc />
    public virtual int Order => ModuleOrder.Default;

    /// <inheritdoc />
    public virtual IEnumerable<Assembly> ConsumerAssemblies => [GetType().Assembly];

    /// <inheritdoc />
    public virtual IEnumerable<Assembly> EnumAssemblies => [];

    /// <inheritdoc />
    public abstract void AddServices(IHostApplicationBuilder builder);

    /// <inheritdoc />
    public virtual void Initialize(WebApplication app)
    {
    }
}
