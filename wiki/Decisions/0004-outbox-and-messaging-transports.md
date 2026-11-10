# 0004. Transactional outbox with a pluggable MassTransit transport

## Status

- **Status:** Accepted
- **Date:** 2026-11-03

## Context

Cross-module reactions such as billing → tenant suspension must neither be lost on a crash nor be sent for changes
that rolled back. Deployments range from a single VPS to setups with a dedicated broker.

## Decision

- Aggregates raise integration events with `AddIntegrationEvent`. They are stored in the module's `outbox_messages`
  table in the same transaction, then relayed to MassTransit after commit, with a sweep as the safety net.
- Delivery is at-least-once (`MessageId` = `EventId`), with retries, back-off and dead-lettering.
- Consumers are idempotent and order-independent.
- The transport is configured with `Messaging:Transport`: `InMemory` (dev and tests), `PostgreSql`, or `RabbitMq`.

## Consequences

- No dual-write bugs, and you can choose a transport without code changes.
- Consumers must be written defensively: they re-read state through `ISender`.
- Each publishing context needs an outbox migration (plus RLS for tenant data).
- **Enforced by:** `MessagingArchitectureTests`, `BackgroundJobEventDispatchTests`, and the `add-integration-event`
  skill. Details: [Messaging](../Architecture/Messaging.md).

## Alternatives considered

- **Publishing directly from handlers:** loses messages on crash and sends them on rollback.
- **A broker-only design:** forces RabbitMQ on every deployment.

## Related

[Messaging](../Architecture/Messaging.md).
