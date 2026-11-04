-- StarterKit: database roles for hardened tenant row-level security (RLS).
-- Run ONCE per cluster as a superuser (BYPASSRLS can only be granted by one), e.g.:
--   psql -h <host> -U postgres -v db=starterkit -v runtime_pw="'CHANGE_ME'" -v host_pw="'CHANGE_ME'" -f init-rls-roles.sql
-- Idempotent. Never commit real passwords; pass them from ./secrets/*.txt or the environment.
--
-- Roles
--   starterkit_app     runtime role: NOSUPERUSER NOBYPASSRLS, not the table owner. The API connects as this role.
--   starterkit_host    host/bypass role: BYPASSRLS, used only for host-scope work when
--                      MultiTenancy:Database:RlsBypass=Role (MultiTenancy:Database:HostConnectionString).
--   tenant_rls_bypass  NOLOGIN group role. Its EXISTENCE switches the policy helper tenant_rls_bypass() into
--                      hardened mode: the app.bypass_rls session variable is then honored only for members of
--                      this role. starterkit_app must NOT be a member.
--
-- Migrations: run them with a separate owner/migration role (it owns the tables; FORCE RLS still applies to it, so
-- migrate-time data seeding needs the host role or membership of tenant_rls_bypass). The runtime role only needs DML.

\set ON_ERROR_STOP on

SELECT 'CREATE ROLE tenant_rls_bypass NOLOGIN'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tenant_rls_bypass') \gexec

SELECT format('CREATE ROLE starterkit_app LOGIN PASSWORD %s NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE', :runtime_pw)
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'starterkit_app') \gexec

SELECT format('CREATE ROLE starterkit_host LOGIN PASSWORD %s NOSUPERUSER BYPASSRLS NOCREATEDB NOCREATEROLE', :host_pw)
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'starterkit_host') \gexec

-- Enforce the attributes on pre-existing roles too.
ALTER ROLE starterkit_app NOSUPERUSER NOBYPASSRLS;
ALTER ROLE starterkit_host NOSUPERUSER BYPASSRLS;
REVOKE tenant_rls_bypass FROM starterkit_app;

-- Privileges (adjust schemas to the modules you run). 'hangfire' (Hangfire.PostgreSql) and 'transport'
-- (Messaging:PostgreSql:Schema, MassTransit SQL transport) are created by the migration role too.
-- Re-run this script whenever a migration adds a NEW schema (see wiki/deployment/Database-Migrations.md); the
-- default privileges at the end cover new tables/sequences in schemas that already exist (they assume the
-- migration role is named starterkit_migrator).
\connect :db

DO $$
DECLARE s text;
BEGIN
  FOREACH s IN ARRAY ARRAY['identity','tenancy','attachment','catalog','subscriptions','notification','hangfire','transport','public'] LOOP
    IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = s) THEN
      EXECUTE format('GRANT USAGE ON SCHEMA %I TO starterkit_app, starterkit_host', s);
      EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO starterkit_app, starterkit_host', s);
      EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO starterkit_app, starterkit_host', s);
    END IF;
  END LOOP;
END $$;

-- Default privileges: objects the MIGRATION role creates later (new tables/sequences in these schemas) become usable
-- by the runtime role without re-running the grants above. Skipped when the migration role does not exist.
DO $$
DECLARE s text;
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'starterkit_migrator') THEN
    FOREACH s IN ARRAY ARRAY['identity','tenancy','attachment','catalog','subscriptions','notification','hangfire','transport'] LOOP
      IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = s) THEN
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE starterkit_migrator IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO starterkit_app, starterkit_host', s);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE starterkit_migrator IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO starterkit_app, starterkit_host', s);
      END IF;
    END LOOP;
  END IF;
END $$;
