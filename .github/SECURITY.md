# Security policy

## Reporting a vulnerability

**Do not open a public issue.** Report it privately through GitHub:
[**Security → Report a vulnerability**](https://github.com/mhasabalah/StarterKit/security/advisories/new).

Include:
- the affected area (module, endpoint, configuration)
- reproduction steps and impact (for example: cross-tenant read, privilege escalation, auth bypass)
- the commit SHA you tested

We aim to acknowledge reports within 3 business days and to agree on a fix and disclosure timeline with you.

## Supported versions

Only the latest `master` of the template is maintained. Projects created from it own their own security
maintenance. Watch this repository's advisories and Dependabot PRs.

## Built-in safeguards

| Area | Safeguard | Docs |
|---|---|---|
| Authentication | JWT + rotating refresh tokens with reuse detection, token blacklist, 2FA for privileged roles | [Identity](../wiki/Identity/README.md) |
| Authorization | Explicit per-endpoint auth enforced by an architecture test, deny-by-default for undeclared endpoints, fine-grained permissions | [Permissions](../wiki/Identity/Permissions-2FA-and-Admin-Audit.md) |
| Tenant isolation | Named EF filters, Postgres row-level security, cross-tenant write blocking | [ADR-0005](../wiki/Decisions/0005-tenant-isolation-model.md) |
| Uploads | Magic-byte validation, blocked extensions, size limits, entity access policies | [Attachment security](../wiki/Attachment/Security.md) |
| Secrets | Docker secrets; no credentials in the repository; Data Protection keys encrypted at rest | [Secrets](../wiki/deployment/Secrets.md) |
| Abuse | Rate limiting per user or IP; PageGate for anonymous listing | [API guidelines](../wiki/Architecture/ApiGuidelines.md) |
| Supply chain | Central package versions, Dependabot | [`dependabot.yml`](dependabot.yml) |
