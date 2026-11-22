using Agentic.Contracts.Agents;
using Agentic.Contracts.Scripting;
using Agentic.Contracts.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.Messaging.Outbox;
using Shipping.Domains.Agents;
using Shipping.Domains.Declarations.Services;

namespace Shipping;

/// <summary>
/// The Shipping sample (optional: the template's <c>sample</c> switch): shipments, customs declarations, and the
/// proof-of-concept agent that prepares a shipment for customs through typed tools (wiki/Modules/Shipping.md). The host
/// discovers it through <see cref="ShippingModuleManifest"/> (ADR-0019).
/// </summary>
public static class ShippingModule
{
    public static IServiceCollection AddShippingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // One repository per aggregate root (command handlers); query handlers use the DbContext directly.
        services.AddScoped<IShipmentRepository, ShipmentRepository>();
        services.AddScoped<IDeclarationDraftRepository, DeclarationDraftRepository>();

        // The customs authority: a MOCK in the sample. A product registers its real adapter before this module.
        services.TryAddSingleton<MockCustomsGateway>();
        services.TryAddSingleton<ICustomsGateway>(sp => sp.GetRequiredService<MockCustomsGateway>());

        // The agent and its tools (Agentic.Contracts only; inert while Agentic:Enabled is false). The script is the
        // deterministic stand-in model of the scripted provider (tests and demos; refused in Production).
        services.AddAgent(ShipmentPreparationAgent.Definition);
        services.AddAgentTool<GetShipmentTool>();
        services.AddAgentTool<EstimateDutiesTool>();
        services.AddAgentTool<PrepareDeclarationTool>();
        services.AddAgentTool<CheckDocumentsTool>();
        services.AddAgentTool<SubmitDeclarationTool>();
        services.AddAgentScript<ShipmentPreparationScript>();

        // The conversational sample: read-only answers about shipments over several messages.
        services.AddAgent(ShippingAssistantAgent.Definition);
        services.AddAgentTool<FindShipmentsTool>();
        services.AddAgentScript<ShippingAssistantScript>();

        services.AddMediator(ShippingRoot.AssemblyName);
        services.AddCarterWithAssemblies(ShippingRoot.AssemblyName);

        services.AddCustomDbContext<ShippingDbContext>(configuration, DatabaseDefaults.ConnectionStringName);
        services.AddOutbox<ShippingDbContext>(); // delivers the integration events raised by Shipping aggregates

        return services;
    }

    public static IApplicationBuilder UseShippingModule(this IApplicationBuilder app)
    {
        app.UseMigration<ShippingDbContext>(); // migrations (dev/tests; production migrates at deploy) + seeders
        return app;
    }
}
