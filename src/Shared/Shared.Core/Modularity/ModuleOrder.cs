namespace Shared.Core.Modularity;

/// <summary>
/// The module startup order as data. Registration (<see cref="IModule.AddServices"/>) and initialization
/// (<see cref="IModule.Initialize"/>) both run in ascending order. The constraints behind the numbers:
/// <list type="bullet">
/// <item>Identity first: users, roles and the auth services everything else builds on.</item>
/// <item>Tenancy before every tenant-data module and before Subscriptions: it overrides the subscriber defaults, and
/// the Default tenant must exist before tenant-data seeding.</item>
/// <item>Webhooks after the modules whose public events it forwards (Catalog, Subscriptions).</item>
/// <item>Notification last: it may override other modules' defaults and maps the realtime hub.</item>
/// </list>
/// New modules use <see cref="Default"/> unless they must run before one of the above. Guarded by
/// <c>ModuleDiscoveryTests</c>.
/// </summary>
public static class ModuleOrder
{
    public const int Identity = 0;
    public const int Tenancy = 100;
    public const int Attachment = 200;
    public const int Catalog = 300;
    public const int Subscriptions = 400;
    public const int Webhooks = 500;
    public const int DataTransfer = 600;

    /// <summary>Where new business modules go: after the platform modules, before Notification.</summary>
    public const int Default = 700;

#if (AGENTIC)
    /// <summary>
    /// AI agents: after every business module (whose agents and tools it runs), before Notification. Order matters only
    /// for readability here: the agent registry is built when the host starts, from every module's registrations.
    /// </summary>
    public const int Agentic = 800;
#endif

    public const int Notification = 900;
}
