import { get, ok } from '../lib/http.js';
import { onUnauthorized, sessionHeaders } from '../lib/session.js';

// In-app notification inbox of a member: list page 1 and the unread badge count.
export function inboxScenario() {
  const params = sessionHeaders();
  const list = get('/api/v1/notifications/inbox/?pageNumber=1&pageSize=20', params);
  onUnauthorized(list);
  ok(list, 'inbox list');
  ok(get('/api/v1/notifications/inbox/unread-count', params), 'inbox unread count');
}
