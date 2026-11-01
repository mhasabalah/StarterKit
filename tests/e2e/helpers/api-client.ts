import { APIRequestContext, APIResponse } from '@playwright/test';

const BASE = process.env.BASE_URL ?? 'http://localhost:5000';

/** Standard envelope returned by every endpoint (Shared.Core ApiResponse<T>). */
export interface ApiBody<T> {
  isSuccess: boolean;
  statusCode: number;
  data: T;
  errors: string[];
}

export interface LoginResult {
  accessToken: string | null;
  refreshToken: string | null;
  expiresAt: string | null;
  user: { id: string; email: string | null; roles: string[] };
  message: string;
}

export interface ProductDto {
  id: string;
  name: string;
  description: string | null;
  sku: string;
  price: number;
  isActive: boolean;
  imageAttachmentId: string | null;
  version: number;
}

export interface PaginatedResult<T> {
  pageInfoResult: { currentPage: number; pageSize: number; totalItems: number };
  result: T[];
}

async function parseBody<T>(res: APIResponse): Promise<{ status: number; body: ApiBody<T> }> {
  const text = await res.text();
  let body: ApiBody<T>;
  try {
    body = JSON.parse(text);
  } catch {
    body = { isSuccess: false, statusCode: res.status(), data: null as unknown as T, errors: [text] };
  }
  return { status: res.status(), body };
}

function sleep(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Retries once after the rate-limit window when the dev server answers 429. */
async function fetchWithRetry(fn: () => Promise<APIResponse>): Promise<APIResponse> {
  const res = await fn();
  if (res.status() !== 429) return res;
  await sleep(65_000);
  return fn();
}

/**
 * Thin typed wrapper over the API. Add one method per endpoint your specs need;
 * keep assertions in the specs, not here.
 */
export class ApiClient {
  constructor(private readonly request: APIRequestContext, private readonly token?: string) {}

  private authHeaders(): Record<string, string> | undefined {
    return this.token ? { Authorization: `Bearer ${this.token}` } : undefined;
  }

  loginAs(token: string): ApiClient {
    return new ApiClient(this.request, token);
  }

  // ── Infrastructure ──────────────────────────────────────────────────────

  async health() {
    return this.request.get(`${BASE}/health`);
  }

  // ── Auth ────────────────────────────────────────────────────────────────

  async login(identifier: string, password: string) {
    const res = await fetchWithRetry(() =>
      this.request.post(`${BASE}/api/auth/login`, { data: { identifier, password } })
    );
    return parseBody<LoginResult>(res);
  }

  // ── Catalog (sample module) ─────────────────────────────────────────────

  async getProducts(params: { search?: string; pageNumber?: number; pageSize?: number } = {}) {
    const res = await this.request.get(`${BASE}/api/v1/products`, {
      params: params as Record<string, string | number>,
      headers: this.authHeaders(),
    });
    return parseBody<PaginatedResult<ProductDto>>(res);
  }

  async createProduct(data: {
    name: string;
    description?: string | null;
    sku: string;
    price: number;
    isActive?: boolean;
  }) {
    const res = await this.request.post(`${BASE}/api/v1/products`, {
      data,
      headers: this.authHeaders(),
    });
    return parseBody<{ id: string }>(res);
  }
}
