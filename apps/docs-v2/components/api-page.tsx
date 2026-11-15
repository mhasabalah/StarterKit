'use client';
import { createOpenAPIPage } from 'fumadocs-openapi/ui';
import type { Document } from 'fumadocs-openapi';

// A shared lazy chunk avoids embedding the complete schema into every operation's page payload.
export const OpenAPIPage = createOpenAPIPage({
  playground: { enabled: false },
  loadDocument: async (id) => {
    if (id !== 'v1') throw new Error(`Unknown API schema: ${id}`);
    // JSON imports widen OpenAPI enum literals to string; the exported contract is validated at generation.
    const schema: unknown = (await import('../openapi/v1.json')).default;
    return schema as Document;
  },
});
