using Shared.Core.Phone;

namespace Identity.Services.Phone;

/// <summary>
/// The one place phone numbers are normalized before they are stored or looked up. Everything in the Identity
/// module keeps phones in E.164 (e.g. <c>+201001234567</c>), which the unique index on users.phone_number relies on.
/// </summary>
public interface IPhoneNumberNormalizer
{
    /// <summary>E.164 form of <paramref name="phoneNumber"/>, the trimmed input if it cannot be parsed, or null.</summary>
    string? Normalize(string? phoneNumber);
}

public sealed class PhoneNumberNormalizer(IPhoneValidationService phoneValidationService) : IPhoneNumberNormalizer
{
    // "ZZ" = unknown region: the number must carry its +country code (all validators require that format).
    private const string InternationalRegion = "ZZ";

    public string? Normalize(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        var trimmed = phoneNumber.Trim();
        return phoneValidationService.ToE164(trimmed, InternationalRegion) ?? trimmed;
    }
}

public static class UserManagerPhoneExtensions
{
    /// <summary>Finds a user by an already normalized (E.164) phone number. Uses ix_users_phone_number.</summary>
    public static Task<ApplicationUser?> FindByPhoneAsync(
        this UserManager<ApplicationUser> userManager, string normalizedPhone, CancellationToken ct = default)
        => userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone, ct);
}
