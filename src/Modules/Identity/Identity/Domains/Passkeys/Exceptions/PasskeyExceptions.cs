using Identity.Resources;

namespace Identity.Domains.Passkeys.Exceptions;

public sealed class PasskeyNotFoundException(string passkeyId)
    : NotFoundException(IdentityText.Get("Passkey.NotFound", passkeyId));

/// <summary>The browser response did not verify (bad signature, wrong origin/RP id, expired or replayed ceremony).</summary>
public sealed class InvalidPasskeyException : BadRequestException
{
    public InvalidPasskeyException(string message) : base(message) => ErrorCode = "passkey_invalid";
}

/// <summary>Adding a passkey is a step-up action: the session must have signed in recently.</summary>
public sealed class PasskeyReauthenticationRequiredException : ForbiddenException
{
    public PasskeyReauthenticationRequiredException()
        : base(IdentityText.Get("Passkey.ReauthenticationRequired"))
        => ErrorCode = "reauthentication_required";
}

public sealed class PasskeysDisabledException : NotFoundException
{
    public PasskeysDisabledException() : base(IdentityText.Get("Passkey.Disabled")) => ErrorCode = "passkeys_disabled";
}

public sealed class PasskeyLimitReachedException : ConflictException
{
    public PasskeyLimitReachedException(int max)
        : base(IdentityText.Get("Passkey.LimitReached", max)) => ErrorCode = "passkey_limit_reached";
}

/// <summary>Removing the credential would leave the account without any way to sign in.</summary>
public sealed class LastSignInMethodException : ConflictException
{
    public LastSignInMethodException()
        : base(IdentityText.Get("Passkey.LastSignInMethod"))
        => ErrorCode = "last_sign_in_method";
}
