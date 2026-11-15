import schema from '@/openapi/v1.json';
export const dynamic = 'force-static';
export function GET() { return Response.json(schema); }
