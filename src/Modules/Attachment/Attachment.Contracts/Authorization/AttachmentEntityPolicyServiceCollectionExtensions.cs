using Attachment.Contracts.Attachments.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace Attachment.Contracts.Authorization;

/// <summary>Registration helpers for <see cref="IAttachmentEntityAccessPolicy"/>.</summary>
public static class AttachmentEntityPolicyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TPolicy"/> as the access policy for <paramref name="entityType"/>
    /// (a scoped, keyed service). Registering a second policy for the same entity type replaces the first.
    /// </summary>
    public static IServiceCollection AddAttachmentEntityPolicy<TPolicy>(
        this IServiceCollection services,
        EntityType entityType)
        where TPolicy : class, IAttachmentEntityAccessPolicy
    {
        services.AddKeyedScoped<IAttachmentEntityAccessPolicy, TPolicy>(entityType);
        return services;
    }
}
