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
| **SixLabors.ImageSharp 3.2** | Six Labors Split License 1.0 | Open-source use, transitive use, or the company's annual gross revenue is under USD 1M | Buy a Six Labors commercial license. Release builds also need a license key (`SIXLABORS_LICENSE_KEY`; see [Template-Backlog](Template-Backlog.md) 2b) | Attachment module only, in **one adapter**: `Infrastructure/ImageProcessing/ImageProcessingService.cs` behind `IImageProcessingService` | Write another `IImageProcessingService` (SkiaSharp, MIT; NetVips, MIT plus LGPL libvips). Nothing else changes |
| **QuestPDF 2026.9** | QuestPDF Community / Professional / Enterprise | Individuals, non-profits, academia, OSI open source, companies under USD 1M revenue. Public-sector bodies and publicly traded companies are **never** eligible | Buy Professional/Enterprise and set `Subscriptions:Pdf:License` | Subscriptions module only (invoice PDFs), `PdfConfiguration` + `InvoiceDocument` | Another renderer behind the invoice PDF service |
| **Seq** (`datalust/seq` container, production `docker-compose.yml`) | Datalust commercial EULA | One user (Individual license) | Team license | Logging sink only (`Seq:ServerUrl`); the app talks OTLP/Serilog-style HTTP | Remove the service and send logs to Loki/OTLP (`docker-compose.observability.yml`) |
| **Redis 7.4+** (`redis:7-alpine` resolves to 7.4) | RSALv2 / SSPLv1 | Using it as your own app's cache (StarterKit's case) | Only restricted if you sell Redis itself as a service | Cache + pub/sub through `IDistributedCache` / `IConnectionMultiplexer` | `valkey/valkey` (BSD-3), a wire-compatible drop-in |

**Copyleft that is fine as used:**
- Hangfire.Core / Hangfire.AspNetCore / Hangfire.PostgreSql are LGPL-3.0, used as unmodified NuGet libraries. If you
  patch Hangfire itself, publish the patch.
- Grafana, Loki and Tempo are AGPL-3.0, run as separate, unmodified containers.
- SonarAnalyzer.CSharp is LGPL-3.0 and build-time only.

## Pinned to the last free major (decided 2026-11-04)

| Package | Pinned | License of the pin | Newer major | Decision |
|---|---|---|---|---|
| ~~MediatR~~ | removed | n/a | 13+ commercial (Lucky Penny) | **Replaced** by the owned mediator port, `Shared.Contracts.Mediation` + `Shared.Core.Mediation.Mediator` ([ADR-0007](../Decisions/0007-owned-mediator-port.md)). `ThirdPartyIsolationTests` fails if MediatR comes back |
| MassTransit (+ Abstractions, RabbitMQ, SqlTransport.PostgreSql) | 8.5.12 | Apache-2.0, permanently for this version | 9+ commercial | **Keep 8.x.** Publishing is already behind our outbox (`AddIntegrationEvent`, with one `IPublishEndpoint` call in `OutboxRelay`). Only 6 consumers and the `[MessageUrn]` attribute touch MassTransit; contracts may use only `MassTransit.Abstractions` (guarded). **Deadline:** v8 is reported to get security fixes "through at least the end of 2026"; confirm on masstransit.io before then. Options at that point: buy v9, stay on unpatched 8.5.12 (an internal-only attack surface), or move to Wolverine/Rebus. That move is mechanical: 6 consumers, the bus configuration and one publish call, and `TenantMessagingBusTests` is the acceptance suite |
| FluentAssertions | 7.2.2 | Apache-2.0 | 8+ commercial (Xceed) | **Keep 7.x.** Test-only, never shipped. Exit: `AwesomeAssertions` (Apache-2.0 fork of 7.x): swap the package and the `using` |

Dependabot ignores major bumps of MassTransit, FluentAssertions and SixLabors (`.github/dependabot.yml`), so a
commercial major never arrives by an automated PR.

## Everything else: permissive

| License | Packages |
|---|---|
| MIT | ASP.NET Core / EF Core / Microsoft.Extensions.* 10.x, Aspire 13.x, Asp.Versioning, Carter, Swashbuckle, Scalar, Mapster, Scrutor, MailKit, Fluid.Core, System.IdentityModel.Tokens.Jwt, Azure.Storage.Blobs, Roslyn (Microsoft.CodeAnalysis.*), Testcontainers, coverlet, Microsoft.NET.Test.Sdk, Bogus |
| Apache-2.0 | FluentValidation, OpenTelemetry.*, Grafana.OpenTelemetry, AspNetCore.HealthChecks.*, EFCore.NamingConventions, libphonenumber-csharp, Figgle, AWSSDK.*, KubernetesClient, xunit |
| BSD-3-Clause | Polly, Moq, Google.Protobuf (transitive) |
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
