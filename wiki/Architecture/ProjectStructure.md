# Project Structure

## Solution Organization

```
StarterKit/
├── src/
│   ├── Api/                                 # ASP.NET Core host (Program.cs, appsettings, Dockerfile)
│   ├── Api.Tests/                           # Architecture tests (guardrails)
│   ├── Api.E2ETests/                        # HTTP end-to-end tests (+ multi-tenant, pending-model-changes)
│   ├── Aspire/
│   │   ├── AppHost/                         # .NET Aspire orchestration (Postgres, Redis, Seq, observability)
│   │   ├── ServiceDefaults/                 # Shared OpenTelemetry / health defaults
│   │   └── AppHost.Tests/                   # Aspire orchestration tests
│   ├── Modules/
│   │   ├── Identity/                        # Auth, users, roles, JWT (+ UserDirectory.Contracts port)
│   │   ├── Tenancy/                         # Tenants, branches, memberships (multi-tenancy control plane)
│   │   ├── Notification/                    # Email + WhatsApp delivery (incl. OTP), delivery log
│   │   ├── Attachment/                      # File uploads and storage providers
│   │   ├── Subscriptions/                   # Subscribers, plans, subscriptions, payments
│   │   └── Catalog/                         # Sample vertical-slice module (Product)
│   └── Shared/
│       ├── Shared.Contracts/                # CQRS interfaces, pagination, enums
│       ├── Shared.Core/                     # Base classes, behaviors, EF/Redis/web helpers
│       └── Shared.Messaging/                # MassTransit configuration
├── tests/e2e/                               # Playwright browser smoke tests (TypeScript)
├── tools/claude/starterkit-plugin/          # Claude Code plugin: skills, reviewer agent, hooks
├── infra/                                   # Nginx, observability, pgAdmin configs
├── deployment/                              # VPS deploy/rollback/backup scr## Module Structure Template

Every module follows the Catalog layout. To create one, use `/starterkit:new-module`.

```
src/Modules/<Module>/
├── <Module>.Contracts/                      # Public surface: Shared.Contracts only
│   ├── <Area>s/Commands/  Queries/  Dtos/  Enums/  IntegrationEvents/
│   └── <Module>ContractsRoot.cs             # assembly marker (enum lookup)
├── <Module>/                                # Implementation
│   ├── Domains/<Aggregate>/
│   │   ├── Models/                          # aggregate + children, factories, invariants
│   │   ├── Features/<Feature>/              # <Feature>Handler.cs (validator + handler) + <Feature>EndPoint.cs
│   │   ├── Events/  EventHandlers/  Exceptions/
│   │   ├── Consumers/  Jobs/  Services/  Mappings/
│   ├── Data/
│   │   ├── <Module>DbContext.cs  <Module>DbContextFactory.cs (design time)
│   │   ├── Configurations/                  # IEntityTypeConfiguration<T>
│   │   ├── Repository/                      # I<X>Repository + <X>Repository (one file)
│   │   ├── Migrations/  Seed/
│   ├── Options/  Resources/ (<Module>Resource.resx + .ar.resx)  Common/
│   ├── <Module>Module.cs                    # Add<Module>Module / Use<Module>Module
│   └── GlobalUsings.cs                      # + <Module>Root assembly marker
├── <Module>.Tests/                          # unit: Domain/, Features/, Services/, TestSupport.cs
└── <Module>.IntegrationTests/               # Testcontainers Postgres + real Add<Module>Module
```

### Contracts rules
- Contracts hold no implementation logic: records, enums, integration events.
- They depend only on `Shared.Contracts` (architecture test). The one whitelisted exception is
  `Tenancy.Contracts → Shared.Core`.
- Other modules may reference them. Identity is reached only through `UserDirectory.Contracts`.

HTTP route

### Contracts (`*.Contracts/`)
**Purpose**: Public API contracts

**Contains**:
- Command DTOs (requests)
- Query DTOs (requests)
- Response DTOs
- Shared contracts between modules

**Rules**:
- No implementation logic
- DTOs only
- Versioning when needed
- Shared across modules

## Naming Conventions

### Files
- `{Feature}Handler.cs` - validator + mediator handler
- `{Feature}EndPoint.cs` - Carter endpoint (or grouped `{X}sModule.cs`)
- `{Entity}Configuration.cs` - EF Core configuration
- `{Entity}Repository.cs` - Repository implementation
- `I{Entity}Repository.cs` - Repository interface

### Namespaces
```csharp
{Module}.Domains.{Aggregate}.Models
{Module}.Domains.{Aggregate}.Features.{Action}
{Module}.Data.Repository.{Aggregate}
{Module}.Contracts.{Aggregate}.Commands
```

## Dependency Flow

```
API → Application (Features) → Domain ← Data
                ↓
            Contracts
```

**Rules**:
- Domain has no dependencies on other layers
- Data depends on Domain
- Features depend on Domain and Data
- Contracts are independent
- API orchestrates everything

## Next Steps
- [Technology Stack](TechnologyStack.md)
- [Design Patterns](DesignPatterns.md)
- [Coding Standards](CodingStandards.md)
