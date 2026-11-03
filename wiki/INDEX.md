# Wiki Index

Index of every document in the StarterKit wiki. Start with [README.md](README.md) if you are new.

## Directory Structure

```
wiki/
├── README.md                         # Wiki home
├── INDEX.md                          # This file
├── Architecture/                     # Patterns, conventions, structure
│   ├── Overview.md
│   ├── ProjectStructure.md
│   ├── TechnologyStack.md
│   ├── DesignPatterns.md
│   ├── CodingStandards.md
│   ├── DatabaseConventions.md
│   ├── ApiGuidelines.md
│   └── Messaging.md                  # Domain vs integration events, outbox, transports
├── Modules/
│   └── Catalog.md                    # Sample module + "how to add a new module" checklist
├── MultiTenancy/                     # Optional SaaS multi-tenancy (tenants, branches, isolation)
│   ├── Overview.md                   # Requirements, architecture, NFRs, threat model
│   ├── Isolation-and-Dedicated-Databases.md
│   ├── Branches-and-Scoping.md
│   ├── Adding-a-Tenant-Aware-Module.md
│   └── Operations.md                 # Enabling, provisioning, purge, runbooks, config reference
├── Identity/                         # Auth, users, admins, JWT
│   ├── README.md
│   ├── AdminManagement.md
│   ├── AdminManagementAPI.md
│   ├── AdminManagementImplementation.md
│   ├── AdminUserManagementSummary.md
│   ├── UserManagementAPI.md
│   ├── UserManagementImplementation.md
│   └── jwt/
│       ├── JWT-AUTH-TECHNICAL-GUIDE.md
│       ├── JWT-AUTH-FRONTEND-GUIDE.md
│       └── TokenRefreshStrategies.md
├── Attachment/                       # File uploads and storage providers
│   ├── README.md
│   ├── Configuration.md
│   ├── Implementation.md
│   ├── PENDING-ATTACHMENTS-API-GUIDE.md
│   ├── PENDING-ATTACHMENTS-IMPLEMENTATION.md
│   ├── STORAGE-PROVIDERS-COMPARISON.md
│   └── AWS-S3-STORAGE-IMPLEMENTATION.md
├── Subscriptions/
│   └── PaymentMethods-Frontend-Integration.md
├── Shared/                           # Shared.Core
│   ├── README.md
│   └── Examples.md
├── Infrastructure/
│   ├── Aspire.md
│   └── Redis.md
├── Reference/
│   ├── README.md
│   ├── Agents.md                     # Guidance for AI coding agents
│   ├── Migrations.md
│   └── DOCKER-MOUNT-EXPLAINED.md
├── deployment/                       # Docker, Nginx, CI/CD, secrets, backups
│   └── INDEX.md                      # Deployment docs index
├── Troubleshooting-PostgreSQL-Authentication.md
└── appsettings.Attachment.example.json
```

## By Topic

### Getting Started
1. [Wiki Home](README.md)
2. [Architecture Overview](Architecture/Overview.md)
3. [Project Structure](Architecture/ProjectStructure.md)
4. [Technology Stack](Architecture/TechnologyStack.md)
5. [Catalog sample module + adding a new module](Modules/Catalog.md)

### Development
- [Coding Standards](Architecture/CodingStandards.md)
- [Design Patterns](Architecture/DesignPatterns.md)
- [Database Conventions](Architecture/DatabaseConventions.md)
- [API Guidelines](Architecture/ApiGuidelines.md)
- [Messaging: domain vs integration events, outbox, transports](Architecture/Messaging.md)
- [EF Core Migrations](Reference/Migrations.md)

### Modules

#### Identity (authentication and authorization)
- [Overview](Identity/README.md)
- [Permissions, admin 2FA and admin audit](Identity/Permissions-2FA-and-Admin-Audit.md)
- [JWT technical guide](Identity/jwt/JWT-AUTH-TECHNICAL-GUIDE.md) | [JWT frontend guide](Identity/jwt/JWT-AUTH-FRONTEND-GUIDE.md) | [Token refresh strategies](Identity/jwt/TokenRefreshStrategies.md)
- [Admin Management](Identity/AdminManagement.md) | [Admin API](Identity/AdminManagementAPI.md) | [Admin implementation](Identity/AdminManagementImplementation.md)
- [User Management API](Identity/UserManagementAPI.md) | [User implementation](Identity/UserManagementImplementation.md) | [Summary](Identity/AdminUserManagementSummary.md)

#### Attachment (file management)
- [Overview](Attachment/README.md)
- [Configuration](Attachment/Configuration.md) | [Implementation](Attachment/Implementation.md)
- [Pending (anonymous) uploads API](Attachment/PENDING-ATTACHMENTS-API-GUIDE.md) | [Implementation](Attachment/PENDING-ATTACHMENTS-IMPLEMENTATION.md)
- [Storage providers comparison](Attachment/STORAGE-PROVIDERS-COMPARISON.md) | [AWS S3 storage](Attachment/AWS-S3-STORAGE-IMPLEMENTATION.md)
- [Security](Attachment/Security.md) | [Reliability](Attachment/Reliability.md) — two-phase upload, async deletion, reconciliation sweeper

#### Subscriptions (subscribers, plans, subscriptions, payments)
- [Billing rules](Subscriptions/Billing.md) — tax, proration, refunds, invoices and credit notes, renewal orchestrator
- [Payment Methods - frontend integration](Subscriptions/PaymentMethods-Frontend-Integration.md)

#### Catalog (sample module)
- [Catalog sample + new module checklist](Modules/Catalog.md)

### Shared Services
- [HttpContext Service](Shared/README.md) and [Examples](Shared/Examples.md)
- [Notification module (email, WhatsApp, OTP)](Modules/Notification.md)
- [Replacing the identity provider (Keycloak, ...)](Identity/Replacing-the-Identity-Provider.md)
- [Redis caching](Infrastructure/Redis.md)
- [.NET Aspire local development (AppHost, dashboard, observability stack)](Infrastructure/Aspire.md)

### Deployment and Operations
- **[Deployment docs index](deployment/INDEX.md)**
- [Production deployment (generic)](deployment/Production-Deployment.md) | [Complete guide](deployment/Production-Deployment-Complete-Guide.md)
- [Docker Compose](deployment/README.DockerCompose.md) | [Nginx](deployment/README.Nginx.md) | [Observability](deployment/README.Observability.md)
- [GitHub Actions CI/CD](deployment/GitHub-Actions-CI-CD.md) | [CI/CD overview](deployment/README.CICD.md) | [GitHub secrets](deployment/GITHUB-SECRETS-SETUP.md)
- [Secrets management](deployment/README.secrets.md) | [Production secrets](deployment/PRODUCTION-SECRETS-GUIDE.md)
- [Backups and restore](deployment/BACKUP-RESTORE-GUIDE.md) | [pgBackRest](deployment/PGBACKREST-SETUP.md) | [S3 backups](deployment/S3-BACKUP-SETUP.md)
- [Self-hosted runner](deployment/SELF-HOSTED-RUNNER-QUICKSTART.md) | [VPS runner](deployment/VPS-RUNNER-SETUP.md) | [Runner maintenance](deployment/RUNNER-MAINTENANCE.md)

### Reference and Troubleshooting
- [Agents (AI coding agent guidance)](Reference/Agents.md)
- [Docker mounts explained](Reference/DOCKER-MOUNT-EXPLAINED.md)
- [PostgreSQL authentication troubleshooting](Troubleshooting-PostgreSQL-Authentication.md)

## Quick Lookup

| I want to... | Read |
|--------------|------|
| Add a new module | [Modules/Catalog.md](Modules/Catalog.md) |
| Add a feature to an aggregate | [Architecture/CodingStandards.md](Architecture/CodingStandards.md) |
| Publish / consume an event across modules | [Architecture/Messaging.md](Architecture/Messaging.md) |
| Add an EF migration | [Reference/Migrations.md](Reference/Migrations.md) |
| Configure JWT / auth | [Identity/jwt/JWT-AUTH-TECHNICAL-GUIDE.md](Identity/jwt/JWT-AUTH-TECHNICAL-GUIDE.md) |
| Upload files | [Attachment/Implementation.md](Attachment/Implementation.md) |
| Cache with Redis | [Infrastructure/Redis.md](Infrastructure/Redis.md) |
| Run locally with Aspire | [Infrastructure/Aspire.md](Infrastructure/Aspire.md) |
| Deploy to a VPS | [deployment/Production-Deployment.md](deployment/Production-Deployment.md) |
| Manage secrets | [deployment/README.secrets.md](deployment/README.secrets.md) |

## Template Maintenance

- [Template Backlog](Reference/Template-Backlog.md) — prioritized fixes/upgrades for the StarterKit template itself

