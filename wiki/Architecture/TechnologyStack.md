# Technology Stack

## Core Framework

### .NET 10.0
- **Version**: .NET 10.0 (LTS)
- **Language**: C# 12+
- **Features Used**:
  - File-scoped namespaces
  - Primary constructors
  - Record types
  - Init-only properties
  - Pattern matching enhancements

## API Layer

### Carter
- **Purpose**: Minimal API endpoints
- **Usage**: Endpoint definitions with routing
- **Benefits**: 
  - Lightweight and fast
  - Clean endpoint organization
  - Automatic OpenAPI generation

### ASP.NET Core Minimal APIs
- **Purpose**: HTTP API framework
- **Features**:
  - Route-to-code pattern
  - Built-in DI
  - Model binding and validation

## Application Layer

### Mediator (owned port)
- **Purpose**: CQRS dispatch. `Shared.Contracts.Mediation` (`ISender`, `IPublisher`, `IPipelineBehavior`, ...) is
  implemented by `Shared.Core.Mediation.Mediator`; no third-party mediator ([ADR-0007](../Decisions/0007-owned-mediator-port.md))
- **Usage**: Command/Query handlers
- **Pattern**: Request/Response messaging
- **Benefits**:
  - Decoupled handlers
  - Cross-cutting concerns via behaviors
  - Testable business logic

### FluentValidation
- **Purpose**: Input validation
- **Usage**: Command/Query validators
- **Integration**: mediator pipeline behavior
- **Benefits**:
  - Fluent API
  - Reusable rules
  - Clear error messages

### Mapster
- **Purpose**: Object-to-object mapping
- **Usage**: Entity ↔ DTO conversions
- **Benefits**:
  - High performance
  - Simple configuration
  - Type-safe mappings

## Data Layer

### Entity Framework Core 10
- **Purpose**: ORM and data access
- **Features Used**:
  - Code-first approach
  - Fluent API configurations
  - Migrations
  - Interceptors
  - Query filters

### PostgreSQL
- **Version**: 16 (Docker images `postgres:16-alpine`)
- **Features Used**:
  - Schema support for modules
  - JSON/JSONB columns
  - Full-text search
  - Advanced indexing
- **Naming**: snake_case via EFCore.NamingConventions

### Npgsql
- **Purpose**: PostgreSQL provider for EF Core
- **Features**: Native PostgreSQL support

## Messaging & Events

### MassTransit
- **Purpose**: Message bus and event handling
- **Transport**: RabbitMQ / Azure Service Bus
- **Patterns**:
  - Publish/Subscribe
  - Request/Response
  - Saga pattern
- **Usage**: Domain events and inter-module communication

## Caching

### Redis
- **Purpose**: Distributed caching
- **Usage**:
  - Session storage
  - Response caching
  - Temporary data
- **Library**: StackExchange.Redis

## Logging & Observability

### Logging (Microsoft.Extensions.Logging + OpenTelemetry)
- **Purpose**: Structured logging with message templates (`ILogger<T>`); no third-party logging framework
- **Exported via OpenTelemetry** (`Shared.Core/OpenTelemetryCollector`, `OtlpExporterPlan`) to:
  - the Aspire dashboard (local dev) or the OTel collector → Loki (compose), via the standard `OTEL_EXPORTER_OTLP_*` settings
  - Seq (`AddSeqEndpoint("seq")`, `ConnectionStrings:seq`)
- **Correlation**: correlation id in the logging scope (`UseCorrelationId`) + trace/span ids on every record
- **Levels**: the `Logging:LogLevel` section (EF Core and `System` at Warning by default)

### Seq
- **Purpose**: Log aggregation and analysis
- **Features**:
  - Centralized logging
  - Query capabilities
  - Dashboards and alerts

### OpenTelemetry
- **Purpose**: Distributed tracing
- **Integration**: ASP.NET Core, EF Core, HTTP clients
- **Exporters**: Jaeger, Zipkin

## Orchestration & Hosting

### .NET Aspire
- **Purpose**: Cloud-ready app orchestration
- **Components**:
  - AppHost (orchestration)
  - ServiceDefaults (shared configs)
- **Benefits**:
  - Local development environment
  - Service discovery
  - Configuration management

## Security

### ASP.NET Core Identity
- **Purpose**: User management
- **Custom Implementation**: Extended for module requirements
- **Features**:
  - Password hashing
  - Role management
  - Claims-based auth

### JWT Bearer Authentication
- **Purpose**: API authentication
- **Token Type**: JSON Web Tokens
- **Storage**: HttpOnly cookies / Authorization header

## Testing

### xUnit
- **Purpose**: Unit testing framework
- **Usage**: All test projects

### FluentAssertions
- **Purpose**: Readable test assertions
- **Usage**: Unit and integration tests

### Testcontainers
- **Purpose**: Integration testing with real databases
- **Containers**: PostgreSQL, Redis, RabbitMQ

### Bogus
- **Purpose**: Test data generation
- **Usage**: Fake data for tests

## Build & Deployment

### MSBuild
- **Central Package Management**: Directory.Packages.props
- **Common Properties**: Directory.Build.props

### Docker
- **Purpose**: Containerization
- **Images**: API, databases, services

### GitHub Actions / Azure DevOps
- **Purpose**: CI/CD pipelines
- **Stages**: Build, test, deploy

## Development Tools

### Rider / Visual Studio 2022
- **Purpose**: Primary IDEs
- **Features**: .NET 10 support, debugging, refactoring

### Docker Desktop
- **Purpose**: Local container orchestration
- **Usage**: Running dependencies

### pgAdmin / DBeaver
- **Purpose**: Database management
- **Usage**: Query and schema inspection

## Package Management

### NuGet
- **Central Management**: Directory.Packages.props
- **Private Feed**: Optional Azure Artifacts

## Version Summary

| Component | Version |
|-----------|---------|
| .NET | 9.0 |
| C# | 12+ |
| EF Core | 9.x |
| PostgreSQL | 14+ |
| Carter | Latest |
| Mediator | Owned (`Shared.Contracts.Mediation`) |
| FluentValidation | Latest |
| Mapster | Latest |
| MassTransit | Latest |
| OpenTelemetry | 1.20 |
| Redis | 7+ |

## Next Steps
- [Design Patterns](DesignPatterns.md)
- [Coding Standards](CodingStandards.md)
- [Database Conventions](DatabaseConventions.md)
