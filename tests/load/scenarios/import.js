import { sleep } from 'k6';
import http from 'k6/http';
import { authHeaders, data, get, headers, ok, url } from '../lib/http.js';
import { seedData } from '../lib/seed-data.js';

const ROWS = Number(__ENV.LOAD_IMPORT_ROWS || '200');
const DONE = [3, 4, 5, 6]; // Completed, CompletedWithErrors, Failed, Cancelled (enums are numbers in JSON)

// Small CSV generated per iteration with unique SKUs. Headers: ProductTransferColumns.Import (catalog.products).
function csv(tag) {
  const lines = ['sku,name_en,name_ar,type,price,currency'];
  for (let i = 0; i < ROWS; i++) lines.push(`IMP-${tag}-${i},Imported ${i},مستورد ${i},Physical,${(10 + (i % 90)).toFixed(2)},USD`);
  return lines.join('\n') + '\n';
}

// Bulk import: multipart submit (202 + job), then poll GET /jobs/{id} until the background job is done. Exactly one
// VU: the API allows one active import per definition and tenant.
export function importScenario() {
  const admin = seedData().admin;
  const auth = authHeaders({ apiKey: admin.apiKey });
  delete auth['Content-Type']; // multipart boundary is set by k6

  const tag = `${__ITER}${Date.now().toString(36)}`.toUpperCase();
  const submit = http.post(
    url('/api/v1/data-transfer/imports'),
    {
      file: http.file(csv(tag), `products-${tag}.csv`, 'text/csv'),
      definitionKey: 'catalog.products',
      mode: 'Commit',
      policy: 'BestEffort',
    },
    { headers: auth, tags: { name: 'POST /data-transfer/imports' } },
  );
  const job = data(submit);
  if (!ok(submit, 'submit import', { 'submit import: 202': (r) => r.status === 202 }) || !job) return;

  let status = job.status;
  for (let i = 0; i < 120 && !DONE.includes(status); i++) {
    sleep(0.5);
    const poll = http.get(url(`/api/v1/data-transfer/jobs/${job.id}`), { headers: auth, tags: { name: 'GET /data-transfer/jobs/{id}' } });
    ok(poll, 'poll import');
    const detail = data(poll);
    status = detail && detail.job ? detail.job.status : status;
  }
  ok(submit, 'import finished', { 'import finished: Completed': () => status === 3 });
}
