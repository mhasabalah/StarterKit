using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Shared.Core.Modularity;

[assembly: ModuleManifest(typeof(Attachment.AttachmentModuleManifest))]

namespace Attachment;

/// <summary>The Attachment module's manifest: discovered by the host (ADR-0019), delegates to <see cref="AttachmentModule"/>.</summary>
public sealed class AttachmentModuleManifest : ModuleBase
{
    public override int Order => ModuleOrder.Attachment;

    public override IEnumerable<Assembly> EnumAssemblies => [typeof(Attachment.Contracts.Attachments.Dtos.AttachmentType).Assembly];

    public override void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddAttachmentModule(builder.Configuration);

    public override void Initialize(WebApplication app) => app.UseAttachmentModule();
}
