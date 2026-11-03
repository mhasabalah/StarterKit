# Observability for developers

This page covers how to see what the application is doing (logs, traces, metrics) and how to instrument new code.
Running the Grafana / Prometheus / Tempo / Loki / Jaeger stack in Docker is covered in
[../deployment/Observability.md](../deployment/Observability.md).

## Where to look

| Environment | Logs | Traces | Metrics |
|---|---|---|---|
| Aspire (`cd src/Aspire/AppHost && dotnet run`) | Aspire dashboard + Seq | Aspire dashboard | Aspire dashboard |
| Aspire full stack (`-- Observability:FullStack=true`) | + Loki/Grafana | + Tempo/Jaeger | + Prometheus/Grafana |
| Docker Compose | Seq (`SEQ_PORT`) | Jaeger/Tempo with the observability overlay | Prometheus/Grafana with the overlay |
| Production | Seq behind Nginx (IP-restricted) | Tempo/Jaeger | Prometheus/Grafana |

Every log line carries the `TraceId`, so you can jump from a Seq log entry to its trace. Error responses return
`traceId` in the `ApiResponse` envelope; ask users for it in bug reports.

## What is instrumented out of the box

The wiring lives in `Shared.Core/OpenTelemetryCollector/Extensions.cs` (`AddCustomObservability`) and
`src/Aspire/ServiceDefaults`. It covers:
- **Traces:** ASP.NET Core, HttpClient, EF Core/Npgsql, MassTransit (`DiagnosticHeaders.DefaultListenerName`), and the
  outbox relay (`StarterKit.Outbox`; the `outbox publish` producer span continues the originating request's trace).
  Every MediatR request gets a span `{InstrumentationName}.{command|query} {RequestName}` and a
  `mediatr.request.duration` histogram (tags: name, kind, outcome) from `RequestTelemetryBehavior`, the outermost
  pipeline behavior (registered by `AddCustomObservability`, host only).
- **Metrics:** ASP.NET Core and Kestrel, `System.Runtime`, the instrumentation meter, plus Prometheus scraping.
- **Logs:** structured `ILogger` output exported through OpenTelemetry (OTLP) to Seq, the Aspire dashboard or Loki.
- **Health:** `/health` checks dependencies (Postgres, Redis; Seq is deliberately excluded). `/alive` is liveness.

Configuration is the `Observability` section (`ObservabilityOptions`): `InstrumentationName`, `ServiceName`,
toggles for metrics, tracing and logging, and the exporter selection (OTLP, Prometheus, console, ...).
`OtlpExporterPlan` decides which OTLP endpoints are used.

## Instrumenting new code

- **Logs first.** Use structured message templates with the ids that matter: `logger.LogInformation("Subscription
  {SubscriptionId} renewed for {SubscriberId}", ...)`. Use `Warning` for handled anomalies, `Error` for failures
  with the exception. Never log secrets or OTP codes.
- **Spans for meaningful work.** Wrap long or external operations (provider calls, batch jobs) in an activity from
  the shared `IDiagnosticsProvider.ActivitySource`, and tag it with ids, not PII:

  ```csharp
  using var activity = diagnostics.ActivitySource.StartActivity("inventory.reconcile");
  activity?.SetTag("inventory.batch_size", ids.Count);
  ```

  A new `ActivitySource` with its own name must be added to the tracer's `AddSource(...)` list in
  `AddCustomObservability`, or its spans are dropped.
- **Metrics for things you'll alert on.** Use counters or histograms on the instrumentation meter (queue depth, job
  failures, payment approvals). Keep tag cardinality low: never put user or tenant ids in tags.
- **Background jobs and consumers** log a summary at the end of each run: processed, skipped, failed.

## Troubleshooting checklist

1. Find the request in Seq by `TraceId`, `RequestPath` or `UserId`.
2. Open the trace and look for the slow or failed span (DB query, HTTP call, outbox publish).
3. For async flows, check the outbox (`<schema>.outbox_messages`: `attempts`, `dead_lettered_at`) and the consumer
   logs. See [Messaging.md](Messaging.md).
4. For jobs, check the Hangfire dashboard (`/hangfire`, admin only) for failures and retries.
