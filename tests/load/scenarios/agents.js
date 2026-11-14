import { check, sleep } from 'k6';
import http from 'k6/http';
import { Trend } from 'k6/metrics';
import { USER_PASSWORD } from '../lib/config.js';
import { authHeaders, data, login, ok, url } from '../lib/http.js';
import { seedData } from '../lib/seed-data.js';

// The agent proof of concept under load, on the scripted model (no provider, no API key, no token cost): the Shipping
// sample's shipment preparation agent. Needs a stack with agents on (LOAD_AGENTS=true -> Agentic__Enabled) and the
// sample module. Opt-in: SCENARIOS=agents (or a list that names it).
//
// One iteration = the full demo over HTTP: create a shipment with its documents, hand it to customs preparation (the
// outbox event starts the run on the agentic Hangfire queue), wait for the declaration draft the agent prepares, find
// the approval it requests, approve the exact payload, and wait for the submission. agent_flow_ms measures hand-over to
// submitted (outbox + queue + two worker attempts + one human decision); http_req_duration measures the API only.

const DRAFT = 1; // DeclarationStatus.Draft (enums are numbers in JSON)
const SUBMITTED = 2; // DeclarationStatus.Submitted
const TIMEOUT_SECONDS = Number(__ENV.LOAD_AGENT_TIMEOUT || '90');
const POLL_SECONDS = 0.5;

const flow = new Trend('agent_flow_ms', true);
const untilDraft = new Trend('agent_draft_ms', true);

const DOCUMENTS = [
  { kind: 1, reference: 'INV-1', content: '10 laptops at 100 USD each.' }, // CommercialInvoice
  { kind: 2, reference: 'PL-1', content: '1 pallet, 120 kg.' }, // PackingList
  { kind: 3, reference: 'CO-1', content: 'Made in China.' }, // CertificateOfOrigin
];

// The seeded admin (Admin role: Shipping permissions, agentic.runs.start, agentic.approvals.decide), one bearer per VU.
let token = null;

function auth() {
  if (token === null) {
    const res = login(seedData().admin.email, USER_PASSWORD);
    const tokens = data(res);
    token = tokens ? tokens.accessToken : null;
  }
  return authHeaders({ accessToken: token });
}

function call(method, path, body, name) {
  let res = http.request(method, url(path), body === undefined ? null : JSON.stringify(body), { headers: auth(), tags: { name } });
  if (res.status === 401) {
    token = null; // expired during a long run: sign in again once
    res = http.request(method, url(path), body === undefined ? null : JSON.stringify(body), { headers: auth(), tags: { name } });
  }
  return res;
}

/** Polls until value() returns something other than undefined, or the timeout. */
function waitFor(value) {
  const deadline = Date.now() + TIMEOUT_SECONDS * 1000;
  while (Date.now() < deadline) {
    const found = value();
    if (found !== undefined) return found;
    sleep(POLL_SECONDS);
  }
  return undefined;
}

export function agentsScenario() {
  const reference = `LOAD-${__VU}-${__ITER}-${Date.now().toString(36)}`.toUpperCase();
  const created = call(
    'POST',
    '/api/v1/shipping/shipments',
    { reference, originCountry: 'CN', destinationCountry: 'AE', hsCode: '8471.30', declaredValue: 1000, currency: 'USD', weightKg: 120, documents: DOCUMENTS },
    'POST /shipping/shipments',
  );
  const shipment = data(created);
  if (!ok(created, 'create shipment') || !shipment) return;

  const started = Date.now();
  const ready = call('POST', `/api/v1/shipping/shipments/${shipment.id}/ready-for-clearance`, {}, 'POST /shipping/shipments/{id}/ready-for-clearance');
  if (!ok(ready, 'hand over')) return;

  // The agent prepares the draft without asking (low-risk change).
  const draft = waitFor(() => {
    const res = call('GET', `/api/v1/shipping/shipments/${shipment.id}/declaration`, undefined, 'GET /shipping/shipments/{id}/declaration');
    const d = res.status === 200 ? data(res) : null;
    return d && d.status === DRAFT ? d : undefined;
  });
  if (!check(draft, { 'agent prepared the draft': (d) => d !== undefined })) return;
  untilDraft.add(Date.now() - started);

  // ... and asks a person before the external submission. The approval names the declaration in its arguments.
  const approval = waitFor(() => {
    const res = call('GET', '/api/v1/agent-approvals?status=Pending&pageSize=100', undefined, 'GET /agent-approvals');
    const page = data(res);
    const match = page ? page.result.find((a) => a.argumentsJson.indexOf(draft.id) >= 0) : null;
    return match || undefined;
  });
  if (!check(approval, { 'agent asked for approval': (a) => a !== undefined })) return;

  const approve = call('POST', `/api/v1/agent-approvals/${approval.id}/approve`, { payloadHash: approval.payloadHash }, 'POST /agent-approvals/{id}/approve');
  if (!ok(approve, 'approve')) return;

  const submitted = waitFor(() => {
    const res = call('GET', `/api/v1/shipping/shipments/${shipment.id}/declaration`, undefined, 'GET /shipping/shipments/{id}/declaration');
    const d = res.status === 200 ? data(res) : null;
    return d && d.status === SUBMITTED ? d : undefined;
  });
  if (check(submitted, { 'agent submitted once approved': (d) => d !== undefined && d.submissionReference.indexOf('MOCK-') === 0 })) {
    flow.add(Date.now() - started);
  }
}
