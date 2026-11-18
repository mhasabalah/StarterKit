import { cp, mkdir } from 'node:fs/promises';
import { spawn } from 'node:child_process';
await mkdir('.next/standalone/.next', { recursive: true });
await cp('.next/static', '.next/standalone/.next/static', { recursive: true });
await cp('public', '.next/standalone/public', { recursive: true });
const child = spawn(process.execPath, ['.next/standalone/server.js'], { stdio: 'inherit', env: { ...process.env, HOSTNAME: process.env.DOCS_BIND_HOST || 'localhost', PORT: process.env.PORT || '3000' } });
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill(signal));
child.on('exit', (code) => process.exit(code ?? 1));
