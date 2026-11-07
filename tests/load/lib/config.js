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
const RATE_SCALE = Number(__ENV.LOAD_RATE_SCALE || '1');
// Multiplies every p95 budget below. The budgets are calibrated on a developer machine (API container on 2 of 16
// CPUs); a 2-CPU hosted CI runner shares its cores between the API, Postgres, Redis and k6 and measures ~3x slower,
// so the workflow sets LOAD_P95_SCALE=3. The tripwire still catches a regression, relative to the box it runs on.
const P95_SCALE = Number(__ENV.LOAD_P95_SCALE || '1');

// Open model: each scenario starts `rate` ITERATIONS per second whatever the latency (a slow server cannot throttle the
// load, which a closed VU loop would do). `vus` pre-allocates the VU pool, `maxVus` caps it; an iteration that finds no
// free VU is counted in dropped_iterations, which fails the run (the target rate was not sustained).
// p95 budgets are in ms and deliberately wide: a regression tripwire for a CI runner / developer machine, not an SLA.
export const SCENARIOS = {
  auth: { rate: 5, vus: 8, maxVus: 30, p95: 1500, exec: 'authScenario' },
  catalog: { rate: 20, vus: 10, maxVus: 40, p95: 500, exec: 'catalogScenario' },
  products: { rate: 8, vus: 8, maxVus: 30, p95: 800, exec: 'productsScenario' },
  inbox: { rate: 12, vus: 6, maxVus: 30, p95: 400, exec: 'inboxScenario' },
  // One active import per definition and tenant (409 otherwise): exactly one VU, closed loop (submit, then poll).
  import: { vus: 1, p95: 1000, exec: 'importScenario', closed: true },
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
    const common = { duration: DURATION, exec: s.exec, tags: { load_scenario: name }, gracefulStop: '60s' };
    if (s.closed) {
      scenarios[name] = Object.assign({ executor: 'constant-vus', vus: s.vus }, common);
    } else {
      scenarios[name] = Object.assign(
        {
          executor: 'constant-arrival-rate',
          rate: Math.max(1, Math.round(s.rate * RATE_SCALE)),
          timeUnit: '1s',
          preAllocatedVUs: s.vus,
          maxVUs: s.maxVus,
        },
        common,
      );
      thresholds[`dropped_iterations{load_scenario:${name}}`] = ['count<1'];
    }
    thresholds[`http_req_duration{load_scenario:${name}}`] = [`p(95)<${Math.round(s.p95 * P95_SCALE)}`];
    thresholds[`http_req_failed{load_scenario:${name}}`] = ['rate<0.01'];
    thresholds[`http_reqs{load_scenario:${name}}`] = ['count>0']; // a scenario that sent nothing must fail the run
  }
  return { scenarios, thresholds, summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'max'] };
}
