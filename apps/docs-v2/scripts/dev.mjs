import { watch } from 'node:fs';
import { spawn } from 'node:child_process';
import { sync, app } from './sync-content.mjs';
import path from 'node:path';
await sync();
let timer;
let running = Promise.resolve();
const watcher = watch(path.resolve(app, '../../wiki'), { recursive: true }, () => {
  clearTimeout(timer);
  timer = setTimeout(() => { running = running.then(sync).catch(console.error); }, 200);
});
const child = spawn(process.execPath, ['node_modules/next/dist/bin/next', 'dev', '--webpack', ...process.argv.slice(2)], { cwd: app, stdio: 'inherit' });
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => { watcher.close(); child.kill(signal); });
child.on('exit', (code) => { watcher.close(); process.exit(code ?? 1); });
