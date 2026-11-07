import { sleep } from 'k6';
import { authHeaders, get, ok } from '../lib/http.js';
import { pick, seedData } from '../lib/seed-data.js';

// In-app notification inbox of a member: list page 1 and the unread badge count.
export function inboxScenario() {
  const params = { headers: authHeaders({ accessToken: pick(seedData().users).accessToken }) };
  ok(get('/api/v1/notifications/inbox/?pageNumber=1&pageSize=20', params), 'inbox list');
  ok(get('/api/v1/notifications/inbox/unread-count', params), 'inbox unread count');
  sleep(0.2);
}
