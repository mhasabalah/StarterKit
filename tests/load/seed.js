// Creates the load-test fixtures through the PUBLIC API only (no backdoor, no test auth handler):
//   - N plain users via POST /api/auth/register (their bearer token is kept for the CRUD and inbox scenarios),
//   - one Admin: registers like everyone else, then the seeded SuperAdmin (Identity:SeedAdmin, password from env)
//     assigns the Admin role; the admin gets an API key scoped to bulk import/export,
//   - P published products so the public catalog has data to browse (create -> submit -> approve -> publish).
// Output: $RESULTS_DIR/seed.json, read by main.js. Run once per stack:  k6 run /scripts/seed.js
import { fail } from 'k6';
import { RESULTS_DIR, USER_PASSWORD } from './lib/config.js';
import { authHeaders, data, login, post } from './lib/http.js';

const USERS = Number(__ENV.LOAD_USERS || '20');
const PRODUCTS = Number(__ENV.LOAD_PRODUCTS || '40');
const ADMIN_EMAIL = __ENV.LOAD_ADMIN_EMAIL || 'admin@starterkit.local';
const ADMIN_PASSWORD = __ENV.LOAD_ADMIN_PASSWORD || '';

export const options = {
  vus: 1,
  iterations: 1,
  setupTimeout: '10m',
};

function must(res, what) {
  if (res.status < 200 || res.status >= 300) {
    fail(`${what} failed: HTTP ${res.status} ${String(res.body).slice(0, 300)}`);
  }
  const d = data(res);
  if (d === null) fail(`${what}: no data in the response: ${String(res.body).slice(0, 300)}`);
  return d;
}

function register(email) {
  return must(
    post('/api/auth/register', {
      identifier: email,
      password: USER_PASSWORD,
      confirmPassword: USER_PASSWORD,
      firstName: 'Load',
      lastName: 'User',
    }),
    `register ${email}`,
  );
}

function createApiKey(accessToken, name, scopes) {
  const res = post('/api/v1/me/api-keys', { name, scopes, expiresInDays: 30 }, { headers: authHeaders({ accessToken }) });
  return must(res, `create API key ${name}`).apiKey;
}

export function setup() {
  if (!USER_PASSWORD) fail('LOAD_USER_PASSWORD is not set');
  if (!ADMIN_PASSWORD) fail('LOAD_ADMIN_PASSWORD (the Identity:SeedAdmin:Password of the stack) is not set');

  const runId = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4).toString(36)}`;

  const users = [];
  for (let i = 0; i < USERS; i++) {
    const email = `load-${runId}-${i}@loadtest.example.com`;
    register(email);
    // Plain users hold no platform permission, so they cannot mint scoped API keys (a key never exceeds its owner):
    // the CRUD and inbox scenarios sign in as these users (lib/session.js) and use bearer tokens.
    users.push({ email });
  }

  // Admin: a normal registered user promoted by the seeded SuperAdmin.
  const adminEmail = `load-admin-${runId}@loadtest.example.com`;
  const adminReg = register(adminEmail);
  // The seeded SuperAdmin must change its password on first login (MUST_CHANGE_PASSWORD blocks everything else). A
  // re-seed finds it already changed to the load password, so try that first.
  let superAdmin = null;
  const again = login(ADMIN_EMAIL, USER_PASSWORD);
  if (again.status === 200) {
    superAdmin = data(again);
  } else {
    const first = must(login(ADMIN_EMAIL, ADMIN_PASSWORD), 'SuperAdmin login');
    superAdmin = must(
      post(
        '/api/auth/change-password',
        { currentPassword: ADMIN_PASSWORD, newPassword: USER_PASSWORD, confirmPassword: USER_PASSWORD },
        { headers: authHeaders({ accessToken: first.accessToken }) },
      ),
      'SuperAdmin first-login password change',
    );
  }
  must(
    post(
      `/api/identity/users/${adminReg.user.id}/roles`,
      { roleName: 'Admin', reason: 'k6 load-test seed' },
      { headers: authHeaders({ accessToken: superAdmin.accessToken }) },
    ),
    'assign the Admin role',
  );
  // Log in again: roles travel in the token.
  const adminLogin = must(login(adminEmail, USER_PASSWORD), 'admin login');
  const adminKey = createApiKey(adminLogin.accessToken, 'load-admin', [
    'datatransfer.import',
    'datatransfer.export',
    'catalog.products.manage', // the products definition's own permission
  ]);
  const admin = { email: adminEmail, password: USER_PASSWORD, apiKey: adminKey };

  // Published products for the public catalog scenario (moderation chain, versions chained).
  const bearer = { headers: authHeaders({ accessToken: adminLogin.accessToken }) };
  const productIds = [];
  for (let i = 0; i < PRODUCTS; i++) {
    const sku = `SEED-${runId}-${i}`.toUpperCase();
    const created = must(
      post(
        '/api/v1/products',
        {
          name: { en: `Seed product ${i} ${runId}`, ar: `منتج ${i}` },
          description: 'Load-test seed product',
          sku,
          type: 'Physical',
          price: 10 + i,
          currency: 'USD',
        },
        bearer,
      ),
      `create product ${sku}`,
    );
    const id = created.id;
    const submitted = must(post(`/api/v1/products/${id}/submit-for-review`, {}, bearer), `submit ${sku}`);
    const approved = must(
      post(`/api/v1/products/${id}/approve`, { reviewNotes: 'seed', version: submitted.version }, bearer),
      `approve ${sku}`,
    );
    must(post(`/api/v1/products/${id}/publish`, { version: approved.version }, bearer), `publish ${sku}`);
    productIds.push(id);
  }

  return { runId, users, admin, productIds, searchTerm: 'Seed product' };
}

export default function () {}

export function handleSummary(summary) {
  const seed = summary.setup_data;
  if (!seed) fail('seed: no setup data in the summary');
  return {
    [`${RESULTS_DIR}/seed.json`]: JSON.stringify(seed),
    stdout: `seed written: ${seed.users.length} users, ${seed.productIds.length} products\n`,
  };
}
