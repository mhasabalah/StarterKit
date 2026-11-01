# StarterKit — Template Backlog

Prioritized list of things to fix / upgrade in the template itself. Created when StarterKit was extracted from
BateCom (2026-11-01). Tick items off (or delete them) as they are handled.

## P0 — Correctness / security

1. **Optimistic concurrency check is silently disabled.**
   `src/Shared/Shared.Core/Data/Interceptors/AuditableEntityInterceptor.cs:56` sets
   `versionProp.OriginalValue = entry.Entity.Version` on every `Modified` save. This overwrites the
   client-supplied `OriginalValue` that handlers set (the pattern documented in CLAUDE.md), so a stale
   `Version` from the client never produces a 409. Only true races between load and save are caught.
   - Workaround in use: `Catalog/UpdateProductHandler` compares `command.Version` to `product.Version` explicitly.
   - Fix: stop overwriting `OriginalValue` in the interceptor; just `entry.Entity.Version++` (EF keeps the
     loaded/handler-set original). Add an integration test (Testcontainers) proving stale version → 409 and
     double-SaveChanges in one scope → no spurious 409. Then update CLAUDE.md's concurrency section.
   - The same bug exists in BateCom.
2. **Rotate the BateCom secrets.** BateCom tracks `.env`, `.env.dev`, `.env.prod` and `src/Api/appsettings*.json`
   in git, with real Postgres, Redis, AWS, JWT, SMTP, WhatsApp and Hangfire credentials, and a wiki page with an AWS key.
   Rotate all of them, untrack the files, and consider purging history (git filter-repo). StarterKit holds placeholders only.
3. **Production JWT secret / Hangfire password are placeholders (`CHANGE_ME`).** Add a startup guard that refuses to
   boot in `Production` when any secret still equals `CHANGE_ME` or is empty.

## P1 — Platform

4. **Upgrade .NET 9 → .NET 10 (LTS).** .NET 9 (STS) support ends Nov 2026. Update `Directory.Build.props`,
   `Directory.Packages.props` (EF Core, ASP.NET, Aspire, Npgsql), Dockerfile base images and CI `setup-dotnet`.
5. **Enable analyzers.** Turn on the analyzer and `TreatWarningsAsErrors` settings that are commented out in `Directory.Build.props`
   (at least for `src/`), and clean up the ~180 current warnings.
6. **NuGet feed noise.** `NU1900` warnings come from a private Azure DevOps feed (Microtec) in the user-level
   NuGet config. Add a repo `nuget.config` with `<clear/>` + nuget.org so the template restores identically anywhere.

## P1 — Tests

7. **Architecture tests** (NetArchTest / ArchUnitNET): modules may only reference other modules' `.Contracts`;
   Contracts reference only `Shared.Contracts`; Shared never references modules.
8. **Identity and Attachment have zero tests.** Add unit tests (register/login/OTP/refresh token, attachment
   authorization) and E2E flows (register → verify → login; upload → confirm → download).
9. **E2E tests need Docker** (Testcontainers). Document this in the README and make CI run them (`services: docker`).
   Verify the Catalog E2E flow once Docker is available: `dotnet test src/Api.E2ETests`.

## P2 — Template ergonomics

10. **`dotnet new` template packaging.** Add `.template.config/template.json` (sourceName `StarterKit`) so
    `dotnet new starterkit -n MyApp` renames slnx, docker names, DB names, and the brand automatically.
11. **Module scaffolding.** Add a script or `dotnet new starterkit-module -n Orders` item template based on Catalog.
12. **Multi-tenancy decision.** Subscriptions treats the subscriber as a user (`ISubscriberInfoProvider` default).
    If projects need organizations or tenants, extract a generic `Organizations` module (the old BateCom Developers
    concept: org, members, invites, audit log) and plug it into `ISubscriberInfoProvider`.
    `Shared.Notification.For(subscriberId)` also assumes user ids.
13. **Attachment enum renumbering.** `EntityType` / `AttachmentCategory` were reset (User/Product/Company/Other).
    Each new project should edit these for its own domain *before* its first production migration.
14. **Subscriber endpoints** now use `.RequireAuthorization()` (any signed-in user) where BateCom used `OwnerOnly`.
    Revisit per project.
15. **WhatsApp templates** in `appsettings.json` are Arabic-language Meta template names. Make them per-project config.

## P2 — Ops

16. **CI variables.** The workflows read `vars.APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_*` and `BACKUP_BUCKET`.
    Document the required repo variables and secrets in `wiki/deployment/GITHUB-SECRETS-SETUP.md`, and fail fast when they are missing.
17. **Health / readiness.** Separate liveness and readiness (`/health/live`, `/health/ready` with DB, Redis and Seq checks)
    for compose and K8s, and wire them into the deploy health check.
18. **Nginx admin allow-list.** `deployment/nginx/conf.d/app-production.conf` allows only `127.0.0.1` for admin paths. Set it per deployment.
19. **Observability stack** is heavy (otel-collector, prometheus, grafana, tempo, loki, jaeger, exporters). Consider an
    "observability-lite" compose profile.
