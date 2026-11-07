import http from 'k6/http';
import { check } from 'k6';
import { BASE_URL, TENANT } from './config.js';

export function headers(extra) {
  const h = { 'Content-Type': 'application/json', Accept: 'application/json' };
  if (TENANT) h['X-Tenant'] = TENANT;
  return Object.assign(h, extra || {});
}

/** Credentials -> request headers: an API key (X-Api-Key) or a bearer access token. */
export function authHeaders(cred, extra) {
  if (cred.apiKey) return headers(Object.assign({ 'X-Api-Key': cred.apiKey }, extra));
  return headers(Object.assign({ Authorization: `Bearer ${cred.accessToken}` }, extra));
}

export function url(path) {
  return `${BASE_URL}${path}`;
}

export function get(path, params) {
  return http.get(url(path), params || { headers: headers() });
}

export function post(path, body, params) {
  return http.post(url(path), JSON.stringify(body), params || { headers: headers() });
}

/** The ApiResponse envelope's data, or null when the body is not a successful envelope. */
export function data(res) {
  try {
    const body = res.json();
    return body && body.isSuccess ? body.data : null;
  } catch (e) {
    return null;
  }
}

/** Records a check that the status is 2xx (plus any extra named checks). Returns true when all pass. */
export function ok(res, name, extra) {
  const checks = { [`${name}: status 2xx`]: (r) => r.status >= 200 && r.status < 300 };
  return check(res, Object.assign(checks, extra || {}));
}

export function login(identifier, password) {
  return post('/api/auth/login', { identifier, password });
}
