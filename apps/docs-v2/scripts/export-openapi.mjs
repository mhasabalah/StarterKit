import { spawn, execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdir, writeFile, readFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { once } from 'node:events';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const app = fileURLToPath(new URL('../', import.meta.url));
const root = path.resolve(app, '../..');
const target = path.join(app, 'openapi/v1.json');
const check = process.argv.includes('--check');
const containers = [];
let api;
const run = (command, args, options = {}) => execFileSync(command, args, { encoding: 'utf8', ...options }).trim();
function canonical(value) {
  if (Array.isArray(value)) return value.map(canonical);
  if (value && typeof value === 'object') return Object.fromEntries(Object.keys(value).sort().map((key) => [key, canonical(value[key])]));
  return value;
}
async function freePort() {
  const server = createServer(); server.listen(0, 'localhost'); await once(server, 'listening');
  const port = server.address().port; server.close(); await once(server, 'close'); return port;
}
try {
  const password = randomBytes(24).toString('hex');
  const name = `starterkit-docs-${randomBytes(5).toString('hex')}`;
  // This export owns its temporary database; it never connects to an existing deployment.
  run('docker', ['run', '--rm', '-d', '--name', name, '-e', 'POSTGRES_PASSWORD', '-e', 'POSTGRES_DB=docs_export', '-p', '127.0.0.1::5432', 'pgvector/pgvector:0.8.7-pg16'], { env: { ...process.env, POSTGRES_PASSWORD: password } });
  containers.push(name);
  const port = run('docker', ['port', name, '5432/tcp']).split(':').at(-1);
  const apiPort = await freePort();
  const origin = `http://localhost:${apiPort}`;
  await mkdir(path.join(app, '.generated'), { recursive: true });
  let output = '';
  api = spawn('dotnet', ['run', '--project', 'src/Api', '--no-build', '--no-launch-profile'], {
    cwd: root, windowsHide: true, env: {
      ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: origin,
      ConnectionStrings__starterkit: `Host=localhost;Port=${port};Database=docs_export;Username=postgres;Password=${password}`,
      JwtSettings__SecretKey: randomBytes(32).toString('hex'), Redis__Enabled: 'false',
      Notification__UseRedisBackplane: 'false', Messaging__Transport: 'InMemory',
      MultiTenancy__Enabled: 'false', Database__MigrateOnStartup: 'true',
      Logging__LogLevel__Default: 'Warning',
    }, stdio: ['ignore', 'pipe', 'pipe'],
  });
  api.stdout.on('data', (data) => { output += data; }); api.stderr.on('data', (data) => { output += data; });
  let schema;
  for (let attempt = 0; attempt < 120; attempt++) {
    if (api.exitCode !== null) throw new Error(`API export host exited (${api.exitCode}): ${output.replaceAll(password, '[redacted]').slice(-6000)}`);
    try {
      const response = await fetch(`${origin}/openapi/v1.json`, { signal: AbortSignal.timeout(2000) });
      if (response.ok) { schema = await response.json(); break; }
    } catch { /* Wait for isolated migrations and server startup. */ }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  if (!schema?.openapi || !Object.keys(schema.paths || {}).length) throw new Error(`No valid schema returned. ${output.replaceAll(password, '[redacted]').slice(-4000)}`);
  schema.servers = [{ url: 'https://api.example.com', description: 'Replace with your deployment URL' }];
  const serialized = JSON.stringify(canonical(schema), null, 2) + '\n';
  if (check) {
    if (serialized !== await readFile(target, 'utf8')) throw new Error('OpenAPI snapshot differs from the application. Run npm run export:openapi and review the contract changes.');
  } else {
    await mkdir(path.dirname(target), { recursive: true }); await writeFile(target, serialized);
    await writeFile(path.join(app, 'openapi/provenance.json'), JSON.stringify({ source: 'src/Api', endpoint: '/openapi/v1.json', command: 'npm run export:openapi', normalization: 'Sorted object keys; deployment server replaced by api.example.com', paths: Object.keys(schema.paths).length }, null, 2) + '\n');
  }
  console.log(`OpenAPI ${check ? 'verified' : 'exported'}: ${Object.keys(schema.paths).length} paths.`);
} finally {
  if (api && api.exitCode === null) {
    if (process.platform === 'win32') { try { run('taskkill', ['/PID', String(api.pid), '/T', '/F']); } catch { /* Already stopped. */ } }
    else { api.kill('SIGTERM'); await Promise.race([once(api, 'exit'), new Promise((resolve) => setTimeout(resolve, 5000))]); }
  }
  for (const container of containers) run('docker', ['rm', '-f', container]);
}
