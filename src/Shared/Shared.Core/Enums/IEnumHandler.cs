using Shared.Contracts.Enums;

namespace Shared.Core.Enums;

/// <summary>
/// Custom handler for enums that need special logic (permissions, database lookups, filtering, etc.)
/// Implement this interface for enums that cannot use the generic reflection-based approach.
/// </summary>
public interface IEnumHandler
{
    /// <summary>
    /// The enum type name this handler processes (e.g., "ItemType", "MobileCode").
    /// </summary>
    string EnumName { get; }

    /// <summary>
    /// Handles the enum lookup with custom logic.
    /// </summary>
    /// <param name="context">Optional context (user, permissions, filters, etc.)</param>
    /// <returns>Enum response with items.</returns>
    Task<EnumResponseDto> HandleAsync(EnumLookupContext context);
}

/// <summary>
/// Context passed to custom enum handlers.
/// Contains user info, permissions, filters, and other contextual data.
/// </summary>
public class EnumLookupContext
{
    public string? UserId { get; set; }
    public List<string> UserPermissions { get; set; } = new();
    public Dictionary<string, object> Filters { get; set; } = new();
    public CancellationToken CancellationToken { get; set; }
}
