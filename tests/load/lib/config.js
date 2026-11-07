// Shared configuration for every k6 script. All values come from environment variables (k6 -e / compose env), so the
// same scripts run against a local stack, the CI stack or any other LoadTest-profile deployment.

export const BASE_URL = (__ENV.BASE_URL || 'http://api:80').replace(/\/$/, '');
export const SEED_FILE = __ENV.SEED_FILE || '/results/seed.json';
export const RESULTS_DIR = (__ENV.RESULTS_DIR || '/results').replace(/\/$/, '');

// Password of every load user created by seed.js (the Identity password rules need upper, lower, digit, special).
// Comes from the environment (.env.loadtest); never committed.
export const USER_PASSWORD = __ENV.LOAD_USER_PASSWORD || '';

// Optional X-Tenant header, for a stack running with MultiTenancy:Enabled=true.
export const TENANT = __ENV.LOAD_TENANT || '';

const DURATION = __ENV.LOAD_DURATION || '30s';
const SCALE = Number(__ENV.LOAD_VU_SCALE || '1');

// Per-scenario virtual users (scaled by LOAD_VU_SCALE) and p95 budgets in ms. The budgets are deliberately wide: they
// are a regression tripwire for a CI runner / developer machine, not an SLA. Tighten them against real baselines.
export const SCENARIOS = {
  auth: { vus: 4, p95: 1500, exec: 'authScenario' },
  catalog: { vus: 10, p95: 500, exec: 'catalogScenario' },
  products: { vus: 6, p95: 800, exec: 'productsScenario' },
  inbox: { vus: 6, p95: 400, exec: 'inboxScenario' },
  // One active import per definition and tenant (409 otherwise): exactly one VU.
  import: { vus: 1, p95: 1000, exec: 'importScenario', fixed: true },
};

export function selectedScenarios() {
  const wanted = (__ENV.SCENARIOS || Object.keys(SCENARIOS).join(',')).split(',').map((s) => s.trim());
  return Object.keys(SCENARIOS).filter((name) => wanted.includes(name));
}

export function buildOptions() {
  const scenarios = {};
  const thresholds = { http_req_failed: ['rate<0.01'], checks: ['rate>0.99'] };
  for (const name of selectedScenarios()) {
    const s = SCENARIOS[name];
    scenarios[name] = {
      executor: 'constant-vus',
      vus: s.fixed ? s.vus : Math.max(1, Math.round(s.vus * SCALE)),
      duration: DURATION,
      exec: s.exec,
      tags: { load_scenario: name },
      gracefulStop: '60s',
    };
    thresholds[`http_req_duration{load_scenario:${name}}`] = [`p(95)<${s.p95}`];
    thresholds[`http_req_failed{load_scenario:${name}}`] = ['rate<0.01'];
    thresholds[`http_reqs{load_scenario:${name}}`] = ['count>0']; // a scenario that sent nothing must fail the run
  }
  return { scenarios, thresholds, summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'max'] };
}
