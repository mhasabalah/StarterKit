// k6 entry point: every scenario runs concurrently against the stack seeded by seed.js.
//   k6 run /scripts/main.js                        (all scenarios)
//   SCENARIOS=auth,catalog k6 run /scripts/main.js  (a subset)
//   SCENARIOS=agents k6 run /scripts/main.js        (opt-in: the agent demo; stack started with LOAD_AGENTS=true)
// Env: BASE_URL, LOAD_DURATION (30s), LOAD_VU_SCALE (1), LOAD_USER_PASSWORD, RESULTS_DIR. See wiki/Testing/Load-Testing.md.
import { RESULTS_DIR, buildOptions, selectedScenarios } from './lib/config.js';

export { authScenario } from './scenarios/auth.js';
export { catalogScenario } from './scenarios/catalog.js';
export { productsScenario } from './scenarios/products.js';
export { importScenario } from './scenarios/import.js';
export { inboxScenario } from './scenarios/inbox.js';
export { agentsScenario } from './scenarios/agents.js';

export const options = buildOptions();

function metric(data, name) {
  return data.metrics[name] ? data.metrics[name].values : undefined;
}

// Compact console report (no remote imports, so the run works offline) + the full JSON for CI artifacts.
function report(data) {
  const lines = ['', 'scenario      reqs     rps    avg ms   p95 ms  failed  dropped'];
  for (const name of selectedScenarios()) {
    const dur = metric(data, `http_req_duration{load_scenario:${name}}`);
    const reqs = metric(data, `http_reqs{load_scenario:${name}}`);
    const failed = metric(data, `http_req_failed{load_scenario:${name}}`);
    const dropped = metric(data, `dropped_iterations{load_scenario:${name}}`);
    if (!dur || !reqs) continue;
    lines.push(
      `${name.padEnd(12)} ${String(reqs.count).padStart(6)} ${reqs.rate.toFixed(1).padStart(7)} ` +
        `${dur.avg.toFixed(1).padStart(9)} ${dur['p(95)'].toFixed(1).padStart(8)} ${((failed ? failed.rate : 0) * 100).toFixed(2).padStart(6)}% ${String(dropped ? dropped.count : 0).padStart(7)}`,
    );
  }
  const all = metric(data, 'http_reqs');
  const failedAll = metric(data, 'http_req_failed');
  const checks = metric(data, 'checks');
  lines.push(
    `total: ${all ? all.count : 0} requests (${all ? all.rate.toFixed(1) : 0}/s), ` +
      `failed ${failedAll ? (failedAll.rate * 100).toFixed(2) : 0}%, checks ${checks ? (checks.rate * 100).toFixed(2) : 'n/a'}%`,
    '',
  );
  return lines.join('\n');
}

export function handleSummary(data) {
  return {
    [`${RESULTS_DIR}/summary.json`]: JSON.stringify(data, null, 2),
    stdout: report(data),
  };
}
