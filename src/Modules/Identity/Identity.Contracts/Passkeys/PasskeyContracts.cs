using Identity.Contracts.Audit;
using Identity.Contracts.Auth.Commands;

namespace Identity.Contracts.Passkeys;

// Passkeys (WebAuthn / FIDO2) - phishing-resistant, passwordless sign-in.
// A ceremony is two calls: "options" returns the JSON for navigator.credentials.create()/get() plus a ceremonyId;
// the client sends the browser's result back with that ceremonyId. UserId is always the authenticated caller
// (set by the endpoint from the token), never taken from the body.

/// <param name="OptionsJson">
/// <c>PublicKeyCredentialCreationOptionsJSON</c> / <c>PublicKeyCredentialRequestOptionsJSON</c>: pass to
/// <c>PublicKeyCredential.parseCreationOptionsFromJSON</c> / <c>parseRequestOptionsFromJSON</c> unchanged.
/// </param>
/// <param name="CeremonyId">Opaque, single-use id to send back with the browser's response.</param>
public sealed record PasskeyOptionsResult(string CeremonyId, string OptionsJson, DateTime ExpiresAt);

/// <summary>Starts adding a passkey to the caller's account (step-up: the session must be fresh).</summary>
public sealed record BeginPasskeyRegistrationCommand(Guid UserId) : ICommand<PasskeyOptionsResult>;

/// <summary>Verifies the attestation from <c>navigator.credentials.create()</c> and stores the passkey.</summary>
/// <param name="CredentialJson">The JSON-serialized <c>PublicKeyCredential</c> (<c>credential.toJSON()</c>).</param>
/// <param name="Name">Friendly name ("MacBook Touch ID"); defaults to the authenticator's provider name.</param>
public sealed record CompletePasskeyRegistrationCommand(Guid UserId, string CeremonyId, string CredentialJson, string? Name)
    : ICommand<PasskeyDto>, IAuditedCommand;

/// <summary>Starts a passkey sign-in. Usernameless (discoverable credentials, also for conditional UI / autofill).</summary>
public sealed record BeginPasskeyLoginCommand : ICommand<PasskeyOptionsResult>;

/// <summary>
/// Verifies the assertion from <c>navigator.credentials.get()</c> and signs the owner in. A passkey with user
/// verification is two factors in one (possession + biometric/PIN): the session is MFA, no TOTP challenge follows.
/// </summary>
public sealed record CompletePasskeyLoginCommand(string CeremonyId, string CredentialJson) : ICommand<LoginResult>;

/// <summary>The caller's passkeys (never the public key or attestation).</summary>
public sealed record GetMyPasskeysQuery(Guid UserId) : IQuery<List<PasskeyDto>>;

public sealed record RenamePasskeyCommand(Guid UserId, string PasskeyId, string Name) : ICommand<PasskeyDto>, IAuditedCommand;

/// <summary>Removes a passkey. Refused (409 <c>last_sign_in_method</c>) when it is the account's only way in.</summary>
public sealed record DeletePasskeyCommand(Guid UserId, string PasskeyId) : ICommand<DeletePasskeyResult>, IAuditedCommand;

public sealed record DeletePasskeyResult(bool Deleted);

/// <param name="Id">Base64url credential id - the handle for rename/delete.</param>
/// <param name="IsBackedUp">Synced passkey (iCloud Keychain, Google Password Manager, 1Password ...).</param>
/// <param name="IsBackupEligible">Can be synced; false for device-bound security keys.</param>
/// <param name="Transports">Hints such as <c>internal</c>, <c>hybrid</c>, <c>usb</c>, <c>nfc</c>, <c>ble</c>.</param>
public sealed record PasskeyDto(
    string Id,
    string? Name,
    DateTime CreatedAt,
    bool IsBackedUp,
    bool IsBackupEligible,
    bool IsUserVerified,
    IReadOnlyList<string> Transports);
