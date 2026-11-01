/**
 * Credentials for the seeded super-admin. Override via env so CI and local
 * environments can use their own seed values (see Identity module seeding).
 */
export const ADMIN = {
  get email() { return process.env.E2E_ADMIN_EMAIL ?? 'admin@starterkit.local'; },
  get password() { return process.env.E2E_ADMIN_PASSWORD ?? ''; },
};

let seq = 0;

export function uniqueEmail(prefix = 'e2e'): string {
  seq++;
  return `${prefix}-${Date.now()}-${seq}@test.invalid`;
}

export function uniqueSku(prefix = 'E2E'): string {
  seq++;
  return `${prefix}-${Date.now()}-${seq}`;
}
