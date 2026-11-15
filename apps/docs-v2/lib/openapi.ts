import { createOpenAPI } from 'fumadocs-openapi/server';
export const openapi = createOpenAPI({ input: { v1: './openapi/v1.json' }, includeDocument: false });
