# Project Structure

## Solution Organization

```
StarterKit/
├── src/
│   ├── Api/                                 # ASP.NET Core host (Program.cs, appsettings, Dockerfile)
│   ├── Api.E2ETests/                        # End-to-end API tests
│   ├── Aspire/
│   │   ├── AppHost/                         # .NET Aspire orchestration (Postgres, Redis, Seq, observability)
│   │   └── ServiceDefaults/                 # Shared OpenTelemetry / health defaults
│   ├── Modules/
│   │   ├── Identity/                        # Auth, users, roles, JWT
│   │   ├── Attachment/                      # File uploads and storage providers
│   │   ├── Subscriptions/                   # Subscribers, plans, subscriptions, payments
│   │   └── Catalog/                         # Sample vertical-slice module (Product)
│   └── Shared/
│       ├── Shared.Contracts/                # CQRS interfaces, pagination, enums
│       ├── Shared.Core/                     # Base classes, behaviors, EF/Redis/web helpers
│       ├── Shared.Messaging/                # MassTransit configuration
│       ├── Shared.Email/                    # Email sending + EmailDbContext
│       └── Shared.Notification/             # WhatsApp/Email notifications + NotificationDbContext
├── tests/                                   # Cross-cutting test projects (Shared, e2e)
├── infra/                                   # Nginx, observability, pgAdmin configs
├── deployment/                              # VPS deploy/rollback/backup scripts, prod Nginx
├── wiki/                                    # This documentation
├── StarterKit.slnx                          # Solution
├── Directory.Build.props                    # Global MSBuild properties
└── Directory.Packages.props                 # Centralized package versions
```

## Module Structure Template

Each module follows this consistent structure:

```
ModuleName/
├── ModuleName/                              # Main implementation
│   ├── Domains/                            # Domain layer
│   │   ├── Aggregates/
│   │   │   ├── Models/                     # Entity models
│   │   │   ├── Features/                   # Vertical slices
│   │   │   │   ├── CreateX/
│   │   │   │   │   ├── CreateXHandler.cs
│   │   │   │   │   └── CreateXEndpoint.cs
│   │   │   │   ├── UpdateX/
│   │   │   │   ├── DeleteX/
│   │   │   │   └── GetX/
│   │   │   ├── Events/                     # Domain events
│   │   │   ├── EventHandler/               # Event handlers
│   │   │   ├── Exceptions/                 # Domain exceptions
│   │   │   └── Mappings/                   # Mapster configs
│   ├── Data/                               # Data layer
│   │   ├── Repository/
│   │   │   ├── Interfaces/
│   │   │   └── Implementations/
│   │   ├── Configurations/                 # EF Core configs
│   │   ├── Migrations/                     # EF migrations
│   │   ├── Seed/                           # Data seeding
│   │   └── ModuleDbContext.cs
│   ├── ModuleModule.cs                     # Module registration
│   ├── GlobalUsings.cs                     # Global using directives
│   └── ModuleName.csproj
└── ModuleName.Contracts/                   # Public contracts
    ├── Commands/                           # Command DTOs
    ├── Queries/                            # Query DTOs
    ├── Dtos/                               # Response DTOs
    └── ModuleName.Contracts.csproj
```

## Layer Responsibilities

### Domain Layer (`Domains/`)
**Purpose**: Core business logic and rules

**Contains**:
- Entity models with business logic
- Domain events
- Domain exceptions
- Business validations
- Aggregate roots

**Rules**:
- No infrastructure dependencies
- Pure business logic
- Entity factory methods for creation
- Domain events for significant changes

### Data Layer (`Data/`)
**Purpose**: Data access and persistence

**Contains**:
- EF Core DbContext
- Entity configurations
- Repository implementations
- Migrations
- Data seeding

**Rules**:
- Implements repository interfaces from Domain
- EF Core configurations (Fluent API)
- Database-specific logic

### Features (`Features/`)
**Purpose**: Use case implementations (Vertical Slices)

**Contains**:
- Command/Query handlers
- Validators (FluentValidation)
- API endpoints (Carter)
- Mapster mapping configurations

**Rules**:
- Each feature is self-contained
- Handler contains business orchestration
- Validator in same file as handler
- Endpoint defines HTTP route

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
- `{Entity}Handler.cs` - MediatR handler
- `{Entity}Endpoint.cs` - Carter endpoint
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
