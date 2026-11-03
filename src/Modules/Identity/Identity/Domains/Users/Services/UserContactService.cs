using Identity.Services.Phone;

namespace Identity.Domains.Users.Services;

/// <summary>
/// Applies an admin change of a user's email/phone. A changed contact is unverified again
/// (EmailConfirmed / IsPhoneNumberVerified = false), so password reset can never be redirected to an
/// address the account owner has not proven. Callers must revoke the target's sessions when something changed.
/// </summary>
public interface IUserContactService
{
    Task<ContactChange> ApplyAsync(
        ApplicationUser user, string email, string? phoneNumber, CancellationToken ct = default);
}

public sealed record ContactChange(bool EmailChanged, bool PhoneChanged)
{
    public bool Any => EmailChanged || PhoneChanged;
}

public sealed class UserContactService(
    UserManager<ApplicationUser> userManager,
    IPhoneNumberNormalizer phoneNormalizer) : IUserContactService
{
    public async Task<ContactChange> ApplyAsync(
        ApplicationUser user, string email, string? phoneNumber, CancellationToken ct = default)
    {
        var emailChanged = !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase);
        if (emailChanged)
        {
            var existing = await userManager.FindByEmailAsync(email);
            if (existing is not null && existing.Id != user.Id)
            {
                throw new ConflictException("Email is already in use.");
            }

            // SetEmailAsync also sets EmailConfirmed = false and rotates the security stamp.
            EnsureSucceeded(await userManager.SetEmailAsync(user, email));
        }

        var normalizedPhone = phoneNormalizer.Normalize(phoneNumber);
        var phoneChanged = !string.Equals(user.PhoneNumber ?? string.Empty, normalizedPhone ?? string.Empty,
            StringComparison.Ordinal);

        if (phoneChanged)
        {
            if (normalizedPhone is not null)
            {
                var existing = await userManager.FindByPhoneAsync(normalizedPhone, ct);
                if (existing is not null && existing.Id != user.Id)
                {
                    throw new ConflictException("Phone number is already in use.");
                }
            }

            EnsureSucceeded(await userManager.SetPhoneNumberAsync(user, normalizedPhone));
            user.IsPhoneNumberVerified = false;
            user.UpdateProfileCompletion();
        }

        return new ContactChange(emailChanged, phoneChanged);
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new BadRequestException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}
