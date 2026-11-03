# Architecture Overview

## System Architecture

StarterKit is built as a **Modular Monolith** using **Clean Architecture** and **Domain-Driven Design (DDD)** principles.

## Core Principles

### 1. Modular Monolith
- Self-contained modules with clear boundaries
- Each module has its own DbContext and database schema
- Modules communicate via domain events (MassTransit)
- Independent deployment capability (future microservices migration)

### 2. Clean Architecture Layers

```
┌─────────────────────────────────────┐
│         API Layer (Carter)          │
├─────────────────────────────────────┤
│     Application Layer (MediatR)     │
│  - Commands/Queries                 │
│  - Handlers                         │
│  - Validators (FluentValidation)    │
├─────────────────────────────────────┤
│       Domain Layer (DDD)            │
│  - Entities                         │
│  - Value Objects                    │
│  - Domain Events                    │
│  - Aggregates                       │
├─────────────────────────────────────┤
│    Infrastructure Layer             │
│  - EF Core DbContext                │
│  - Repositories                     │
│  - External Services                │
└─────────────────────────────────────┘
```

### 3. Domain-Driven Design
- **Aggregates**: Core business entities (Product, Subscription, User)
- **Entities**: Domain objects with identity
- **Value Objects**: Immutable descriptive objects
- **Domain Events**: Business events that trigger side effects
- **Repositories**: Data access abstraction

### 4. CQRS Pattern
- **Commands**: Write operations (Create, Update, Delete)
- **Queries**: Read operations (Get, List, Search)
- Separation of read and write models
- MediatR for command/query handling

## Architectural Patterns

### Vertical Slice Architecture
Each feature is self-contained:
```
Features/
├── CreateProduct/
│   ├── CreateProductHandler.cs    # Business logic
│   ├── CreateProductEndpoint.cs   # API endpoint
│   └── Validator (inline)       # Validation rules
```

### Repository Pattern
- Interface in Domain layer
- Implementation in Data layer
- Generic base repository for common operations

### Factory Pattern
Domain entities use static factory methods:
```csharp
public static Product Create(Guid id, string name, Guid profileId)
```

### Event-Driven Architecture
- Domain events for cross-module communication
- MassTransit for message bus
- Event handlers for side effects

## Technology Decisions

| Aspect | Technology | Reason |
|--------|-----------|--------|
| Framework | .NET 10.0 | Latest features, performance |
| Database | PostgreSQL | ACID compliance, JSON support |
| ORM | EF Core 10 | Code-first, migrations |
| API | Carter (Minimal APIs) | Lightweight, fast |
| Messaging | MassTransit | Reliable, patterns support |
| Caching | Redis | Distributed caching |
| Logging | Microsoft.Extensions.Logging + OpenTelemetry → Seq / Aspire / Loki | Structured logging |
| Orchestration | .NET Aspire | Dev/prod consistency |

## Module Independence

Each module:
- Has its own database schema (PostgreSQL schema)
- Can be extracted to microservice
- Publishes domain events
- Subscribes to relevant events
- Does not directly reference other modules

## Next Steps
- [Project Structure](ProjectStructure.md)
- [Technology Stack](TechnologyStack.md)
- [Design Patterns](DesignPatterns.md)
