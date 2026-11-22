# Agentic operations

Run the [Agentic module](../Modules/Agentic.md) in production: switch it on with a real model provider, size its
workers, read its signals, and answer each of its alerts.

## Goal

Agent runs execute promptly, stay within cost, and any run that needs a person is noticed, without an operator ever
editing agent data by hand.

## Prerequisites

- The `agentic` schema migrated (`--migrate-only`) on PostgreSQL with the `vector` extension (the image built from
  `infra/postgres`, or a managed service that offers pgvector).
- A model provider account and a key limited to this deployment.
- Telemetry exported (OTLP) and Prometheus loading `infra/observability/configs/alerts.yaml`
  ([Observability](Observability.md)).

## Steps

### 1. Switch agents on

1. Put the provider key in `secrets/agentic_openai_api_key.txt` (one line; git-ignored).
2. Set `AGENTIC_MODEL_ID` (and `AGENTIC_ENDPOINT` for Azure OpenAI or a self-hosted server) in `.env`.
3. Start with the overlay: `docker compose -f docker-compose.yml -f docker-compose.agents.yml up -d`.
   It sets `Agentic:Enabled=true`, an `OpenAiCompatible` provider named `openai` and the `default` model.
4. Set the model prices so runs carry a cost estimate: `AGENTIC_INPUT_PRICE_PER_MILLION` /
   `AGENTIC_OUTPUT_PRICE_PER_MILLION` in `.env` (the overlay maps them to
   `Agentic:Models:default:InputPricePerMillionTokens` / `OutputPricePerMillionTokens`; 0 = cost not tracked). Set the
   plan quotas `agent_runs` / `agent_tokens` for each plan, and a monthly budget at the provider.
5. Conversations: `Agentic:Conversations` bounds the history a turn is given (`MaxHistoryTurns`,
   `MaxHistoryCharacters`) and the messages per conversation (`MaxMessages`). Every turn is a run, so the quotas and
   capacity above count turns.
   Summaries of long conversations (`SummarizeAfterTurns`, `KeepRecentTurns`, `SummaryModel`) cost one model call per
   summary, charged to `agent_tokens`.
6. Knowledge with the provider's embeddings: set `AGENTIC_KNOWLEDGE_ENABLED=true`, `AGENTIC_EMBEDDING_MODEL` and
   `AGENTIC_EMBEDDING_DIMENSIONS`.
   - For a large corpus, also set `AGENTIC_VECTOR_INDEX=Hnsw`. The overlay gives the migrator the same settings, so
     the next deploy builds the HNSW index for that dimension (CONCURRENTLY; on a big table give PostgreSQL enough
     `maintenance_work_mem`).
   - After changing the dimension, drop the old `ix_knowledge_chunks_embedding_hnsw_<n>` and have the modules send
     their documents again.

For Anthropic, add a provider with `Kind=Anthropic` and the secret `agentic_anthropic_api_key`
(`agentic_<provider>_api_key`, provider name in lower case). The scripted provider is refused in Production.

Before choosing or changing the model, run the model evaluations against it:

```bash
AGENTIC_EVAL_PROVIDER=OpenAiCompatible AGENTIC_EVAL_MODEL=<model id> AGENTIC_EVAL_API_KEY=<key> \
  dotnet test src/Modules/Agentic/Agentic.Tests --filter "Category=ModelEvaluation"
```

Add `AGENTIC_EVAL_EMBEDDING_MODEL` (and `AGENTIC_EVAL_EMBEDDING_DIMENSIONS`) to evaluate the embedding model too; the
streaming evaluation checks that the provider reports token usage while streaming.

They are skipped when the variables are missing, so a green normal run says nothing about a model. Then drive the
real agents end to end as described in [Testing with a real model](../Modules/Agentic.md#testing-with-a-real-model).

### 2. Size the workers

Runs execute on the Hangfire queue `agentic` (`Hangfire:Servers:Agentic`, 4 workers per replica by default). A worker
is busy for the whole model request, so throughput is roughly `WorkerCount / (model latency x turns per run)`.

- Raise `Hangfire:Servers:Agentic:WorkerCount` when `agent.runs.queued.oldest.age` grows while CPU is idle (workers
  are waiting on the provider, not computing).
- Move agent runs off the API replicas with `docker-compose.worker.yml`: `Hangfire__Servers__Agentic__Enabled=false`
  on `api` and `true` on `worker`; give `worker` the same provider secret and `Agentic__*` variables.
- `Agentic:Limits:MaxConcurrentRunsPerTenant` (and the plan limit `max_concurrent_agent_runs`) keeps one tenant from
  taking every worker.

### 3. Watch the signals

| Signal | Where | Healthy |
|---|---|---|
| `agent.runs.finished{status,failure}` | Prometheus | Failures are mostly `initiator_no_longer_authorized` or business refusals, not `model_unavailable` |
| `agent.runs.requeued` | Prometheus | Close to zero; each one is a run whose worker died or whose job was lost |
| `agent.runs.queued.oldest.age` | Prometheus | Seconds, not minutes |
| `agent.approvals.pending`, `agent.approvals.oldest_pending.age` | Prometheus | Requests decided within hours; they expire after `Agentic:Approvals:ExpireAfterHours` (72) |
| `agent.model.tokens{direction}` | Prometheus | Matches the plan quotas and the provider invoice |
| Traces `agent.run` → `agent.model_turn` / `agent.tool` | Tempo, Jaeger or Seq | A run's spans join the trace of the request that started it |
| `GET /api/v1/agent-usage` | API (`agentic.runs.read`) | Per tenant and agent: runs, tokens, estimated cost |

The backlog gauges are refreshed by `RequeueStalledAgentRunsJob` (`Agentic:SweepCron`, every 5 minutes); a series
older than 20 minutes is dropped, so a silent gauge means the sweep stopped, not an empty backlog. Keep the sweep
more frequent than that. The queued age counts from when a run became ready for a worker (a retry still in its
back-off is not counted), and re-enqueueing by the sweep does not reset it.

Prompts, tool arguments and results are not in logs or traces unless `Agentic:Telemetry:CaptureContent=true`. They
live in the run's steps (`GET /api/v1/agent-runs/{id}/steps`) and are erased after `Agentic:Content:RetentionDays`.

### 4. Answer an alert

| Alert | Likely cause | Do |
|---|---|---|
| `AgentModelUnavailable` | Provider outage, expired or revoked key, wrong model id or endpoint | Check the provider status page and the API log `did not answer` (provider details are logged, never stored on the run). Replace the secret file and recreate the container if the key changed. Failed runs are not resumed: their users start new ones |
| `AgentRunFailureRateHigh` | One failure code dominates | `sum by (failure, agent) (increase(agent_runs_finished_total{status="Failed"}[30m]))`. `budget_exceeded`: a prompt loops or limits are too low for the agent (raise `AgentLimits` in its definition, never the tenant's budget blindly). `quota_exceeded`: plan limits. `output_too_large`: the model or provider ignores `MaxOutputTokens` |
| `AgentToolOutcomeUnknown` | A worker stopped while a non-idempotent tool ran | Open the run (`GET /api/v1/agent-runs?status=Failed`, then `/{id}/steps`), read the last tool step and check the business record it would have changed. Tell the user whether it took effect before anyone retries. Then make the tool idempotent if you can |
| `AgentRunsStalling` | Workers die mid-run (OOM kill, restarts, deploys without graceful shutdown) | Check container restarts and memory of the replicas running the `agentic` queue, and that deploys stop containers gracefully (a shutdown returns the run to the queue) |
| `AgentRunQueueBacklog` | No replica serves the `agentic` queue, or all its workers are busy | Hangfire dashboard → Servers: a server must list the `agentic` queue. Enable it or raise `WorkerCount` (step 2) |
| `AgentApprovalBacklog` | Nobody who may decide sees the requests | An approver needs `agentic.approvals.decide` AND the tool's own permission. Check that someone holds both and receives the in-app notices (category group `agents`). With `RequireDifferentApprover` on, the initiator cannot approve their own run |

### 5. Stop an agent quickly

- One tenant, one agent: `PUT /api/v1/agent-policies/{agentKey}` with `enabled: false` (`agentic.manage`). New runs
  are refused; cancel running ones with `POST /api/v1/agent-runs/{id}/cancel`.
- Everywhere: `Agentic:Enabled=false` and restart. Agent routes answer 404, queued runs fail with `agents_disabled`,
  and nothing new is started by triggers.

Never edit `agentic.*` rows by hand: runs, steps and approvals are checked against each other (leases, payload digests,
versions), and a hand edit can execute an action nobody approved.

## Verify

- `docker compose ... config` shows `Agentic__Enabled: "true"` and the secret mounted at
  `/run/secrets/agentic_openai_api_key`.
- `GET /api/v1/agents` lists the agents the caller may start; a run of a read-only agent completes.
- Prometheus `/alerts` lists the `agents` group, and `agent_runs_started_total` appears after the first run.

<!--#if (sample)-->

Under load: `SCENARIOS=agents` on a LoadTest stack started with `LOAD_AGENTS=true` runs the Shipping demo
([Load testing](../Testing/Load-Testing.md)).

<!--#endif-->

## Related

[Agentic module](../Modules/Agentic.md), [Agent security model](../Architecture/Agent-Security.md),
[Observability](Observability.md), [Secrets](Secrets.md), [Docker Compose](Docker-Compose.md).
