# Passkeys (WebAuthn)

Phishing-resistant, passwordless sign-in with platform authenticators (Touch ID, Face ID, Windows Hello, Android)
and synced passkeys (iCloud Keychain, Google Password Manager, 1Password, ...). Backlog item G16.
Decision record: [ADR 0014](../Decisions/0014-passkeys-on-built-in-identity.md).

## How it works

Verification is ASP.NET Core Identity's **built-in passkey handler** (.NET 10, `IPasskeyHandler<ApplicationUser>`):
challenge, origin and RP-id checks, signature, signature counter, backup flags. StarterKit adds what a token-based,
multi-client API needs, in [`PasskeyService`](../../src/Modules/Identity/Identity/Services/Passkeys/PasskeyService.cs):

| Concern | Rule |
|---|---|
| Ceremony state | Server-side, single-use, 128-bit `ceremonyId` in the distributed cache ([`PasskeyCeremonyStore`](../../src/Modules/Identity/Identity/Services/Passkeys/PasskeyCeremonyStore.cs)), bound to its kind and (registration) to the user. No cookies: SPAs and mobile apps on other origins work. |
| User verification | `required`: a passkey sign-in is possession + biometric/PIN, so the session is **MFA** (`amr=mfa`) and satisfies the privileged-role 2FA rule. |
| Discoverable credentials | `required`: usernameless sign-in and conditional UI (passkey autofill). |
| Attestation | `none`: any authenticator model, no device identifiers collected. |
| Step-up | Adding a passkey needs a session that signed in within `FreshAuthMinutes` (403 `reauthentication_required`). |
| Limits | `MaxPasskeysPerUser` (409 `passkey_limit_reached`). |
| Last way in | A passkey cannot be removed when the account has no password, no other passkey and no linked login (409 `last_sign_in_method`). |
| Ownership | A credential id belongs to one account forever; rename/delete of someone else's id answers 404. |
| SSO enforcement | When the user's domain enforces SSO, passkey sign-in is refused like a password (403 `sso_required`). |
| Notifications | Adding or removing a passkey sends a `SecurityAlert` email and writes the Identity admin audit (`passkey.added`, `passkey.renamed`, `passkey.removed`). |

Storage: `identity.user_passkeys` (Identity schema v3, `IdentitySchemaVersions.Version3`), mapped explicitly in
`ApplicationIdentityDbContext`. Rows cascade with the user (account erasure).

> **Deploy note.** Schema v3 also limits `user_logins.login_provider` and the `user_tokens` keys to 128 characters
> and `users.phone_number` to 256 (migration `AddEnterpriseIdentity`; `user_logins.provider_key` is kept at 256 for
> long IdP subjects). Existing values are far shorter; check custom data before migrating a long-lived database.

## API

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/auth/passkeys/registration/options` | user, fresh session, no API key, no impersonation | creation options + `ceremonyId` |
| POST | `/api/auth/passkeys/registration` | same | `{ ceremonyId, credential, name? }` → `PasskeyDto` |
| GET | `/api/auth/passkeys` | user, no API key | my passkeys |
| PATCH | `/api/auth/passkeys/{id}` | user, no API key, no impersonation | rename |
| DELETE | `/api/auth/passkeys/{id}` | same | remove (409 `last_sign_in_method`) |
| POST | `/api/auth/passkeys/assertion/options` | anonymous, `auth` rate limit | request options + `ceremonyId` |
| POST | `/api/auth/passkeys/assertion` | anonymous, `auth` rate limit | `{ ceremonyId, credential }` → `LoginResult` (MFA session) |

`credential` is the browser's `PublicKeyCredential` serialized with `credential.toJSON()`. `id` in the DTO is the
base64url credential id. Browser code: [Frontend integration](Frontend-Integration.md#passkeys).

## Configuration (`Identity:Passkeys`)

| Key | Default | Notes |
|---|---|---|
| `Enabled` | `true` | false = every route answers 404 `passkeys_disabled` |
| `ServerDomain` | request host | **RP ID**: the registrable domain, e.g. `example.com`. Changing it orphans every passkey. Docker: `example.com` placeholder. |
| `AllowedOrigins` | API origin only | exact frontend origins, e.g. `https://app.example.com`; cross-origin iframes are always refused |
| `CeremonyTimeoutMinutes` | 5 | 1-15 |
| `FreshAuthMinutes` | 15 | step-up window for adding a passkey |
| `MaxPasskeysPerUser` | 10 | |

## Tests

- Unit: [`Identity.Tests/Passkeys/PasskeyTests.cs`](../../src/Modules/Identity/Identity.Tests/Passkeys/PasskeyTests.cs) (ceremony single use, step-up window, limits, last-method rule, config mapping).
- E2E with a **software authenticator** (real ES256 signatures, real verifier): [`Api.E2ETests/Identity/PasskeyE2ETests.cs`](../../src/Api.E2ETests/Identity/PasskeyE2ETests.cs) - registration, MFA sign-in, replay, forged signature, foreign origin, cross-account ceremony, ownership, last method, deactivated account, API keys.
