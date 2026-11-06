# Feature catalog

Everything StarterKit ships today, by area, with the page that owns it. Use it to answer "do we already have X?"
before building it, and to brief a product owner. Backlog and status: [Template backlog](Template-Backlog.md).

## Identity and access

| Feature | Highlights | Docs |
|---|---|---|
| Accounts | Email or phone registration, phone onboarding, email confirmation, profile completion, self-service account deletion and data export | [Identity](../Identity/README.md) |
| Sessions | JWT access tokens + rotating refresh-token families (reuse detection), `sid` claim, list/revoke my sessions, revoke others, admin revoke, signing-key rotation | [Identity](../Identity/README.md), [JWT guide](../Identity/jwt/JWT-AUTH-TECHNICAL-GUIDE.md) |
| One-time codes | One OTP system (HMAC, attempt caps, cooldowns, daily caps) for reset, phone, onboarding; delivery by WhatsApp **or email** (`OneTimeCode` template) | [Identity](../Identity/README.md#one-time-codes-otp) |
| Two-factor | TOTP with encrypted keys, hashed single-use recovery codes, replay guard, challenge tokens, enforced for privileged roles | [Permissions, 2FA and admin audit](../Identity/Permissions-2FA-and-Admin-Audit.md) |
| **Passkeys** | WebAuthn on .NET 10 Identity, usernameless + autofill, MFA sessions, step-up to add, last-method guard, security emails | [Passkeys](../Identity/Passkeys.md) |
| **Enterprise SSO** | Per-tenant OIDC and SAML 2.0, DNS-verified domains, home-realm discovery, JIT provisioning, enforced SSO with break-glass, test mode | [Enterprise SSO](../Identity/Enterprise-SSO.md) |
| **SCIM 2.0** | Users + Groups, Okta/Entra compatible, deprovision = suspend + revoke sessions, group → tenant-role mapping | [SCIM](../Identity/SCIM.md) |
| Social login | Google, Facebook (never bypasses 2FA or SSO enforcement) | [Identity](../Identity/README.md) |
| Authorization | Roles → fine-grained permissions per request, tenant permissions, deny-by-default endpoints, `DisallowImpersonation`, `DisallowApiKey` | [Permissions](../Identity/Permissions-2FA-and-Admin-Audit.md), [API guidelines](../Architecture/ApiGuidelines.md) |
| API keys | Personal access tokens, scopes ∩ owner permissions, tenant-scoped | [Users and admins API](../Identity/Users-and-Admins-API.md) |
| Admin | User/admin management with role hierarchy, audited impersonation, append-only Identity admin audit | [Users and admins API](../Identity/Users-and-Admins-API.md) |
| Bot protection | `.RequireHumanVerification` (Turnstile adapter) on anonymous account flows | [Bot protection](../Identity/Bot-Protection.md) |
| Key management | Data Protection keys in the DB, encrypted by certificate or KMS (Azure Key Vault, AWS KMS) | [Identity](../Identity/README.md) |

## Multi-tenancy

| Feature | Highlights | Docs |
|---|---|---|
| Tenants | Optional multi-tenancy, subdomain / custom domain (DNS-verified) resolution, suspension, deletion, export | [Overview](../MultiTenancy/Overview.md), [Tenancy](../Modules/Tenancy.md) |
| Isolation | Named EF filters + Postgres RLS, shared or dedicated databases, automated moves | [Isolation](../MultiTenancy/Isolation-and-Dedicated-Databases.md) |
| Organization | Branch trees with scoped access, memberships, tenant roles, invitations, tenant audit | [Branches](../MultiTenancy/Branches-and-Scoping.md) |
| Directory provisioning | Membership provisioning for SSO JIT and SCIM (`ProvisionMemberCommand`, seat limits, owner-safe) | [Tenancy](../Modules/Tenancy.md), [SCIM](../Identity/SCIM.md) |
| Feature flags | Per tenant/user, percentage rollout, kill switch, `.RequireFlag` | [Feature flags](../Architecture/Feature-Flags.md) |

## Billing

| Feature | Highlights | Docs |
|---|---|---|
| Plans and subscriptions | Lifecycle state machine, upgrades/downgrades with proration, renewals, trials, coupons, dunning | [Subscriptions](../Modules/Subscriptions.md), [Billing](../Subscriptions/Billing.md) |
| Payments | Proof-based payments, Stripe and Paymob adapters, idempotent webhook inbox, refunds to the ledger | [Online payments](../Subscriptions/Online-Payments.md) |
| Money integrity | Append-only ledger, gapless invoice numbers, immutable invoices + credit notes, tax snapshot | [Billing](../Subscriptions/Billing.md) |
| Entitlements | `.RequireFeature`, `.RequireQuota`, usage metering per plan | [Subscriptions](../Modules/Subscriptions.md) |

## Communication and integration

| Feature | Highlights | Docs |
|---|---|---|
| Email | Liquid templates en/ar, **design system** (tokens, components, dark mode, branding, plain-text part), fallback chain, delivery log | [Notification](../Modules/Notification.md), [Email design system](../Modules/Notification-Email-Design-System.md) |
| WhatsApp | Cloud API templates, fallback with email | [Notification](../Modules/Notification.md) |
| In-app + push | Inbox, SignalR hub (Redis backplane), FCM push to session-bound devices | [Realtime and push](../Modules/Notification-Realtime-and-Push.md) |
| Outbound webhooks | Standard Webhooks signatures, retries, delivery log, SSRF guard, auto-disable | [Webhooks](../Modules/Webhooks.md) |
| Messaging | Domain + integration events, transactional outbox, dead letters admin, InMemory / PostgreSQL / RabbitMQ | [Messaging](../Architecture/Messaging.md) |

## Files

| Feature | Highlights | Docs |
|---|---|---|
| Attachments | Magic-byte validation, ClamAV malware scanning (fail-closed, `docker-compose.clamav.yml`), signed URLs, pending uploads, thumbnails (SkiaSharp), reconciliation; Local / Azure Blob / S3 | [Attachment](../Attachment/README.md) |

## Platform

| Feature | Highlights | Docs |
|---|---|---|
| API conventions | Versioned minimal APIs (Carter), one error envelope, idempotency keys, rate limiting, request timeouts | [API guidelines](../Architecture/ApiGuidelines.md) |
| Audit trail | Same-transaction `audit_entries` for `IAuditable` entities | [ADR 0011](../Decisions/0011-audit-trail.md) |
| Observability | OpenTelemetry traces/metrics/logs, Seq, Grafana stack, outbox alerts | [Observability](../Architecture/Observability.md) |
| Operations | Deploy-time migrator, health/readiness, Docker secrets, Nginx, CI/CD with approval, backups, rollback | [Deployment](../deployment/README.md) |
| Guardrails | Architecture tests (boundaries, naming, tenant isolation, auth, messaging, migrations) | [Testing strategy](../Testing/Testing-Strategy.md) |
