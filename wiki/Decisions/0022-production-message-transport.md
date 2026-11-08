# 0022. PostgreSQL is the default production message transport

- **Status:** Accepted
- **Date:** 2026-11-08

## Context

[ADR 0004](0004-outbox-and-messaging-transports.md) made the transport pluggable but left two questions per deployment:
which transport, and who creates the SQL transport's schema and role, which needs DDL and `CREATEROLE` rights.

## Decision

- **PostgreSql is the default** (`MESSAGING_TRANSPORT`, `dotnet new starterkit --transport`). It is durable, adds no
  infrastructure, delayed redelivery works out of the box, and backups and restores include in-flight messages.
- **Choose RabbitMq** for sustained throughput the database should not carry, for routing or fan-out to non-.NET
  consumers, or when the database is already the bottleneck. In multi-tenant deployments that move tenants, also install
  the `rabbitmq_delayed_message_exchange` plugin and set `Messaging:RabbitMq:UseDelayedExchange=true`. The stock image
  has no plugin, and without it a migrating tenant's messages are retried in memory and can stall other tenants.
- **`InMemory` stays dev and tests only.** `ProductionConfigurationGuard` refuses to boot with it.
- **The one-shot migrator owns the transport schema in production.** `--migrate-only` runs
  `MigrateMessagingTransportAsync` with `Database:MigrationConnectionString`. App instances run that DDL only with
  `Database:MigrateOnStartup=true`. `CreateInfrastructure=false` is for a DBA who provisions it outside the deploy.

## Consequences

- One setting per deployment and no broker to operate by default.
- Every process of a deployment must use the same transport. The RabbitMQ overlay switches the API and the migrator;
  `docker-compose.rabbitmq-worker.yml` switches the worker, which `extends` the base service and is out of the overlay's
  reach.
- `ProductionConfigurationGuard` refuses `RabbitMq` outside development without a broker address or a password. In
  Docker this catches `MESSAGING_TRANSPORT=RabbitMq` without the overlay, which alone supplies the password secret.
- Queue load lands on the application database. Watch the `transport` schema, and move to RabbitMq if it shows up in
  database load.

## Alternatives considered

- **RabbitMq by default:** a broker on every deployment, including single-VPS ones, and a plugin for full tenant-move
  behaviour.
- **App instances migrate the transport at startup:** the runtime role would need DDL and `CREATEROLE`.
