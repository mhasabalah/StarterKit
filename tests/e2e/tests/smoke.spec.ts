import { test, expect } from '@playwright/test';
import { ApiClient } from '../helpers/api-client';
import { ADMIN, uniqueSku } from '../helpers/seed';

/**
 * Smoke suite against a running API (BASE_URL, default http://localhost:5000).
 * Copy this file as the starting point for feature specs.
 */
test.describe('Smoke', () => {
  let api: ApiClient;

  test.beforeEach(({ request }) => {
    api = new ApiClient(request);
  });

  test('health endpoint responds 200', async () => {
    const res = await api.health();
    expect(res.status()).toBe(200);
  });

  test('OpenAPI document is served', async ({ request }) => {
    const res = await request.get('/openapi/v1.json');
    expect(res.status()).toBe(200);
  });

  test('login with wrong password returns 401', async () => {
    const { status, body } = await api.login('nobody@example.com', 'wrong-password');
    expect(status).toBe(401);
    expect(body.isSuccess).toBe(false);
  });

  test('product list is public', async () => {
    const { status, body } = await api.getProducts({ pageNumber: 1, pageSize: 5 });
    expect(status).toBe(200);
    expect(body.isSuccess).toBe(true);
  });

  test('creating a product without a token returns 401', async () => {
    const { status } = await api.createProduct({ name: 'x', sku: uniqueSku(), price: 1 });
    expect(status).toBe(401);
  });

  test('admin can create a product and find it in the list', async () => {
    test.skip(!ADMIN.password, 'Set E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD to run admin flows.');

    const login = await api.login(ADMIN.email, ADMIN.password);
    expect(login.status).toBe(200);
    const admin = api.loginAs(login.body.data.accessToken!);

    const sku = uniqueSku('SMOKE');
    const created = await admin.createProduct({ name: 'Smoke product', sku, price: 9.99 });
    expect(created.status).toBe(201);

    const list = await admin.getProducts({ search: sku });
    expect(list.body.data.result.map((p) => p.id)).toContain(created.body.data.id);
  });
});
