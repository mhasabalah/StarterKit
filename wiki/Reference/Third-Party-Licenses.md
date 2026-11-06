# Third-party licenses

What StarterKit depends on, under which license, and what a product built from it must do. The licenses come from
each package's `.nuspec` (versions pinned in `Directory.Packages.props`), checked 2026-11-04. Re-check when you
bump a major or add a package. This page is engineering guidance, not legal advice: have someone qualified confirm
the commercial items for your company.

## Action needed per product

These are the only dependencies whose terms depend on **who you are** (revenue, company type) rather than simply
being permissive.

| Dependency | License | Free when | Otherwise | Where it lives | Exit path |
|---|---|---|---|---|---|
| **QuestPDF 2026.9** | QuestPDF Community / Professional / Enterprise | Individuals, non-profits, academia, OSI open source, companies under USD 1M revenue. Public-sector bodies and publicly traded companies are **never** eligible | Buy Professional/Enterprise and set `Subscriptions:Pdf:License` | Subscriptions module only (invoice PDFs), `PdfConfiguration` + `InvoiceDocument` | Another renderer behind the invoice PDF service |
| **Seq** (`datalust/seq` container, production `docker-compose.yml`) | Datalust commercial EULA | One user (Individual license) | Team license | Logging sink only (`Seq:ServerUrl`); the app talks OTLP/Serilog-style HTTP | Remove the service and send logs to Loki/OTLP (`docker-compose.observability.yml`) |
| **Firebase Cloud Messaging** (the Google service behind `docker-compose.push.yml`; the FirebaseAdmin SDK itself is Apache-2.0) | Google APIs Terms + Firebase Terms of Service | No charge for FCM sends | n/a (no paid tier); accept Google's data-processing terms in the Firebase console, since push titles/bodies transit Google (and Apple for iOS). Use `PushContent.Sensitive` for anything personal | Notification module only, behind `IPushProvider` (`FcmPushProvider`); guarded by `ThirdPartyIsolationTests` | Another `IPushProvider` (direct APNs, OneSignal, ...) or `Push:Provider=NoOp` |
| **Redis 7.4+** (`redis:7-alpine` resolves to 7.4) | RSALv2 / SSPLv1 | Using it as your own app's cache (StarterKit's case) | Only restricted if you sell Redis itself as a service | Cache + pub/sub through `IDistributedCache` / `IConnectionMultiplexer`, and the SignalR backplane | `valkey/valkey` (BSD-3), a wire-compatible drop-in |

**Copyleft that is fine as used:**
- Hangfire.Core / Hangfire.AspNetCore / Hangfire.PostgreSql are LGPL-3.0, used as unmodified NuGet libraries. If you
  patch Hangfire itself, publish the patch.
- Grafana, Loki and Tempo are AGPL-3.0, run as separate, unmodified containers.
- SonarAnalyzer.CSharp is LGPL-3.0 and build-time only.

## Replaced (decided 2026-11-04)

| Was | License problem | Now |
|---|---|---|
| SixLabors.ImageSharp 3.2 | Six Labors Split License: revenue-gated, and Release builds fail without a license key | **SkiaSharp 4.153 (MIT)** behind `IImageProcessingService` ([Image processing](../Attachment/Image-Processing.md)). An ImageSharp adapter is documented there for teams that buy a license |

## Pinned to the last free major (decided 2026-11-04)

| Package | Pinned | License of the pin | Newer major | Decision |
|---|---|---|---|---|
| ~~MediatR~~ | removed | n/a | 13+ commercial (Lucky Penny) | **Replaced** by the owned mediator port, `Shared.Contracts.Mediation` + `Shared.Core.Mediation.Mediator` ([ADR-0007](../Decisions/0007-owned-mediator-port.md)). `ThirdPartyIsolationTests` fails if MediatR comes back |
| MassTransit (+ Abstractions, RabbitMQ, SqlTransport.PostgreSql) | 8.5.12 | Apache-2.0, permanently for this version | 9+ commercial | **Keep 8.x.** Publishing is already behind our outbox (`AddIntegrationEvent`, with one `IPublishEndpoint` call in `OutboxRelay`). Only 6 consumers and the `[MessageUrn]` attribute touch MassTransit; contracts may use only `MassTransit.Abstractions` (guarded). **Deadline:** v8 is reported to get security fixes "through at least the end of 2026"; confirm on masstransit.io before then. Options at that point: buy v9, stay on unpatched 8.5.12 (an internal-only attack surface), or move to Wolverine/Rebus. That move is mechanical: 6 consumers, the bus configuration and one publish call, and `TenantMessagingBusTests` is the acceptance suite |
| FluentAssertions | 7.2.2 | Apache-2.0 | 8+ commercial (Xceed) | **Keep 7.x.** Test-only, never shipped. Exit: `AwesomeAssertions` (Apache-2.0 fork of 7.x): swap the package and the `using` |

Dependabot ignores major bumps of MassTransit and FluentAssertions (`.github/dependabot.yml`), so a
commercial major never arrives by an automated PR.

## Everything else: permissive

| License | Packages |
|---|---|
| MIT | SkiaSharp (+ `NativeAssets.Linux.NoDependencies`), Sylvan.Data.Csv + Sylvan.Data.Excel (DataTransfer only), ASP.NET Core / EF Core / Microsoft.Extensions.* 10.x (incl. `Microsoft.AspNetCore.SignalR.StackExchangeRedis`, `.SignalR.Client`, `.TestHost`), Aspire 13.x, Asp.Versioning, Carter, Swashbuckle, Scalar, Mapster, Scrutor, MailKit, Fluid.Core, System.IdentityModel.Tokens.Jwt, Microsoft.IdentityModel.Protocols.OpenIdConnect, Azure.Storage.Blobs, Roslyn (Microsoft.CodeAnalysis.*), Testcontainers, coverlet, Microsoft.NET.Test.Sdk, Bogus |
| Apache-2.0 | FirebaseAdmin, Google.Apis / Google.Apis.Auth / Google.Apis.Core (FCM, Notification module only), FluentValidation, OpenTelemetry.*, Grafana.OpenTelemetry, AspNetCore.HealthChecks.*, EFCore.NamingConventions, libphonenumber-csharp, Figgle, AWSSDK.*, KubernetesClient, xunit |
| BSD-3-Clause | ITfoxtec.Identity.Saml2 (SAML 2.0 SP, Identity module only), Polly, Moq, Google.Protobuf, Google.Api.Gax / Google.Api.Gax.Rest (transitive, via FirebaseAdmin) |
| PostgreSQL | Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL, Npgsql.OpenTelemetry |
| Apache-2.0 OR MPL-2.0 | RabbitMQ.Client (transitive) |
| Microsoft EULA | Microsoft.VisualStudio.Azure.Containers.Tools.Targets (Visual Studio container tooling, design-time only) |

Container images: postgres (PostgreSQL License), rabbitmq (MPL-2.0), nginx (BSD-2), pgbackrest (MIT), Prometheus,
node-exporter, postgres-exporter, cAdvisor, Jaeger and the OTel Collector (Apache-2.0), redis_exporter,
redis-commander and MailHog (MIT), pgAdmin (PostgreSQL License), .NET base images (MIT).

Of the 341 resolved packages (including transitive ones), 314 are MIT or Apache-2.0. The rest are listed above.
`Grafana.OpenTelemetry` pulls in many instrumentations you may not use (Cassandra, AWS Lambda, ...). Switch to
`Grafana.OpenTelemetry.Base` if image size matters.

## How to re-run the audit

After `dotnet restore`, every resolved package is in `src/**/obj/project.assets.json`, and its license is in
`~/.nuget/packages/<id>/<version>/<id>.nuspec` (`<license type="expression">`, or a license file for the custom ones
above). Anything that is not MIT, Apache-2.0, BSD or PostgreSQL gets a row in the tables above before it merges.
