# Frontend integration: SSO and passkeys

What a web app has to do to offer "Continue with SSO" and passkeys. API details: [Enterprise SSO](Enterprise-SSO.md),
[Passkeys](Passkeys.md). All responses use the usual `ApiResponse` envelope (`data`, `errorCode`).

## Email-first sign-in

```ts
const { data } = await api.post('/api/auth/sso/discover', { email });
if (data.ssoAvailable) {
  // ssoRequired = password sign-in will be refused; hide the password field.
  const returnUrl = `${location.origin}/auth/sso`;   // must be in Identity:Sso:AllowedReturnOrigins
  location.assign(`${API}/api/auth/sso/${data.connectionId}/start?returnUrl=${encodeURIComponent(returnUrl)}`
                  + `&loginHint=${encodeURIComponent(email)}`);
} else {
  showPasswordAndPasskeyOptions();
}
```

## The return page (`/auth/sso`)

```ts
const params = new URLSearchParams(location.search);
history.replaceState(null, '', location.pathname);       // drop the code from the address bar
if (params.get('sso_error')) return showSsoError(params.get('sso_error'));   // e.g. domain_not_allowed
const { data } = await api.post('/api/auth/sso/exchange', { code: params.get('sso_code') });
if (data.requiresTwoFactor) return goToTotpStep(data.twoFactorChallengeToken);
storeSession(data.accessToken, data.refreshToken);
```

The code is single use and lives 60 seconds: exchange it immediately, never store it. A password or passkey login
that answers 403 with `errorCode: "sso_required"` means: send the user to SSO.

## Passkeys

Uses the WebAuthn JSON helpers (Chrome 129+, Safari 18+, Firefox 119+); add a polyfill such as
`@github/webauthn-json` for older browsers.

### Sign in (with autofill)

```ts
// <input type="email" autocomplete="username webauthn">
const { data } = await api.post('/api/auth/passkeys/assertion/options', {});
const options = PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(data.optionsJson));
const credential = await navigator.credentials.get({ publicKey: options, mediation: 'conditional' });
const login = await api.post('/api/auth/passkeys/assertion',
  { ceremonyId: data.ceremonyId, credential: credential.toJSON() });
storeSession(login.data.accessToken, login.data.refreshToken);   // an MFA session
```

Use `mediation: 'conditional'` on page load for autofill, or call it without `mediation` from a
"Sign in with a passkey" button.

### Add a passkey (signed in)

```ts
const { data } = await api.post('/api/auth/passkeys/registration/options', {});   // 403 reauthentication_required → sign in again
const options = PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(data.optionsJson));
const credential = await navigator.credentials.create({ publicKey: options });
await api.post('/api/auth/passkeys/registration',
  { ceremonyId: data.ceremonyId, credential: credential.toJSON(), name: 'MacBook' });
```

List, rename and delete with `GET/PATCH/DELETE /api/auth/passkeys[/{id}]`; show `isBackedUp` as "synced".

### Requirements

- HTTPS (or `http://localhost` in development).
- The page origin must be in `Identity:Passkeys:AllowedOrigins`, and its host must be `ServerDomain` or a
  subdomain of it (e.g. `app.example.com` for RP ID `example.com`).
- Native apps need the platform association files on the RP domain (`/.well-known/apple-app-site-association`,
  `/.well-known/assetlinks.json`); serve them from the web frontend.
