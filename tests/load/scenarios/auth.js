import { sleep } from 'k6';
import { USER_PASSWORD } from '../lib/config.js';
import { data, headers, login, ok, post } from '../lib/http.js';
import { pick, seedData } from '../lib/seed-data.js';

// Real credential flow: POST /api/auth/login, then POST /api/auth/refresh with the rotated refresh token.
export function authScenario() {
  const user = pick(seedData().users);
  const loginRes = login(user.email, USER_PASSWORD);
  const tokens = data(loginRes);
  ok(loginRes, 'login', { 'login: access + refresh token': () => tokens !== null && !!tokens.accessToken && !!tokens.refreshToken });
  if (tokens === null) return;

  const refreshRes = post('/api/auth/refresh', { refreshToken: tokens.refreshToken }, { headers: headers() });
  const rotated = data(refreshRes);
  ok(refreshRes, 'refresh', { 'refresh: new refresh token': () => rotated !== null && rotated.refreshToken !== tokens.refreshToken });
  sleep(0.1);
}
