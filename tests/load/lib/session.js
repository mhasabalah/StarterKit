import encoding from 'k6/encoding';
import { USER_PASSWORD } from './config.js';
import { authHeaders, data, headers, login, post } from './http.js';
import { seedData } from './seed-data.js';

// A per-VU bearer session. Seeded tokens are never reused (they would expire during a long run and are shared by
// every run): each VU signs in as its own user on first use, refreshes through POST /api/auth/refresh shortly before
// the access token expires or after a 401, and signs in again if the refresh itself fails.
const SKEW_SECONDS = 60;
let state = null;

function expiry(token) {
  try {
    return JSON.parse(encoding.b64decode(token.split('.')[1], 'rawurl', 's')).exp;
  } catch (e) {
    return 0;
  }
}

function adopt(tokens) {
  if (!tokens || !tokens.accessToken) return false;
  state.accessToken = tokens.accessToken;
  state.refreshToken = tokens.refreshToken;
  state.exp = expiry(tokens.accessToken);
  return true;
}

function signIn() {
  return adopt(data(login(state.email, USER_PASSWORD)));
}

function renew() {
  const res = post('/api/auth/refresh', { refreshToken: state.refreshToken }, { headers: headers() });
  return (res.status === 200 && adopt(data(res))) || signIn();
}

/** Headers of this VU's session, renewed when the token is about to expire. */
export function sessionHeaders() {
  if (state === null) {
    const users = seedData().users;
    state = { email: users[(__VU - 1) % users.length].email, accessToken: '', refreshToken: '', exp: 0 };
    signIn();
  } else if (state.exp - Date.now() / 1000 < SKEW_SECONDS) {
    renew();
  }
  return { headers: authHeaders({ accessToken: state.accessToken }) };
}

/** Call after a 401: renews the session so the next request carries a valid token. */
export function onUnauthorized(res) {
  if (res.status === 401 && state !== null) renew();
}
