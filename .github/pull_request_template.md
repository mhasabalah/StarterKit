## What and why

<!-- One or two sentences: the problem, and what this change does about it. Link the issue: Closes #123 -->

## How

<!-- Notable design decisions. Did you follow a /starterkit:* skill? Anything a reviewer should look at first? -->

## Checklist

- [ ] `dotnet build StarterKit.slnx` passes; the relevant tests and `dotnet test src/Api.Tests` (architecture guardrails) pass
- [ ] Tests added at the right levels (unit / integration / E2E), including failure modes (not found, forbidden, conflict, validation, cross-tenant)
- [ ] Every new endpoint declares authorization explicitly (and `.DisallowImpersonation()` if destructive)
- [ ] Schema change → migration added; new tenant tables have `EnableTenantRowLevelSecurity`
- [ ] Cross-module facts go through `AddIntegrationEvent` (outbox), not direct publishing; consumers are idempotent
- [ ] New user-facing strings exist in both the `en` and `ar` resx files
- [ ] Docs updated where behaviour or structure changed (`wiki/`, `README.md`, `CLAUDE.md`); `bash scripts/check-docs.sh` is clean
- [ ] No secrets, real hostnames or IPs (`CHANGE_ME`, `api.example.com`, `YOUR_SERVER_IP`)
- [ ] Architectural decision? An ADR is added in `wiki/Decisions/`

## Deployment notes

<!-- Migrations, new config keys or secrets, feature flags, ordering constraints, rollback considerations. "None" is a valid answer. -->
