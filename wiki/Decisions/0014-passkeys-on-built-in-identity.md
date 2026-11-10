# 0014. Build passkeys on ASP.NET Core Identity's built-in WebAuthn, with server-side ceremony state

## Status

- **Status:** Accepted
- **Date:** 2026-11-05

## Context

Passkeys are the phishing-resistant default of modern SaaS sign-in. .NET 10 ships WebAuthn verification in ASP.NET
Core Identity (`IPasskeyHandler<TUser>`, schema v3 `user_passkeys`). Its `SignInManager.MakePasskey*` helpers keep
ceremony state in an authentication cookie, but StarterKit clients are token-based SPAs and mobile apps on other
origins.

## Decision

- Use the built-in `PasskeyHandler<ApplicationUser>` for all cryptography and storage (`IdentitySchemaVersions.Version3`).
- Keep ceremony state **server-side** (distributed cache, random id, single use, bound to kind and user) instead of
  the cookie.
- Require user verification and discoverable credentials; attestation `none`.
- A user-verified passkey sign-in is an MFA session; adding a passkey needs a fresh session; the last sign-in method
  cannot be removed; SSO enforcement applies to passkeys too.

## Consequences

- No third-party FIDO2 library to license or patch; verification follows the framework's security updates.
- The `user_passkeys` migration also applies schema v3 key-length changes to Identity tables (documented).
- E2E tests drive a software authenticator against the real verifier.

## Alternatives considered

- **Fido2NetLib** (MIT): mature, but redundant now that the framework ships the same verification.
- **Cookie-based built-in flow**: does not fit cross-origin token clients.

## Related

[Passkeys](../Identity/Passkeys.md).
