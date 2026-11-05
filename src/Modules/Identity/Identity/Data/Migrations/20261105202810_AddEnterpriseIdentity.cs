using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnterpriseIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                schema: "identity",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "identity",
                table: "user_tokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "login_provider",
                schema: "identity",
                table: "user_tokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "provider_key",
                schema: "identity",
                table: "user_logins",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "login_provider",
                schema: "identity",
                table: "user_logins",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    changes_json = table.Column<string>(type: "jsonb", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    impersonator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scim_directories",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    token_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_request_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scim_directories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sso_connections",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    protocol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    oidc_authority = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    oidc_client_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    oidc_client_secret_protected = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    oidc_extra_scopes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    saml_idp_entity_id = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    saml_idp_sso_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    saml_idp_metadata_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    saml_idp_certificates = table.Column<List<string>>(type: "text[]", nullable: false),
                    saml_allow_idp_initiated = table.Column<bool>(type: "boolean", nullable: false),
                    email_claim = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    given_name_claim = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    family_name_claim = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    jit_provisioning = table.Column<bool>(type: "boolean", nullable: false),
                    default_role_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    enforce_sso = table.Column<bool>(type: "boolean", nullable: false),
                    trust_idp_mfa = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sso_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_passkeys",
                schema: "identity",
                columns: table => new
                {
                    credential_id = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_passkeys", x => x.credential_id);
                    table.ForeignKey(
                        name: "fk_user_passkeys_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scim_groups",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    directory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    external_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    tenant_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    members = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scim_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_scim_groups_scim_directories_directory_id",
                        column: x => x.directory_id,
                        principalSchema: "identity",
                        principalTable: "scim_directories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scim_users",
                schema: "identity",
                columns: table => new
                {
                    directory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    external_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scim_users", x => new { x.directory_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_scim_users_scim_directories_directory_id",
                        column: x => x.directory_id,
                        principalSchema: "identity",
                        principalTable: "scim_directories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scim_users_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sso_domains",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    verification_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sso_domains", x => x.id);
                    table.ForeignKey(
                        name: "fk_sso_domains_sso_connections_connection_id",
                        column: x => x.connection_id,
                        principalSchema: "identity",
                        principalTable: "sso_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity",
                schema: "identity",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at",
                schema: "identity",
                table: "audit_entries",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_scim_directories_tenant_id",
                schema: "identity",
                table: "scim_directories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_scim_directories_token_prefix",
                schema: "identity",
                table: "scim_directories",
                column: "token_prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_scim_groups_directory_display_name",
                schema: "identity",
                table: "scim_groups",
                columns: new[] { "directory_id", "display_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scim_users_user_id",
                schema: "identity",
                table: "scim_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_scim_users_directory_external_id",
                schema: "identity",
                table: "scim_users",
                columns: new[] { "directory_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_scim_users_directory_user_name",
                schema: "identity",
                table: "scim_users",
                columns: new[] { "directory_id", "user_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sso_connections_tenant_id",
                schema: "identity",
                table: "sso_connections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_sso_domains_connection_domain",
                schema: "identity",
                table: "sso_domains",
                columns: new[] { "connection_id", "domain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sso_domains_verified_domain",
                schema: "identity",
                table: "sso_domains",
                column: "domain",
                unique: true,
                filter: "verified_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_passkeys_user_id",
                schema: "identity",
                table: "user_passkeys",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "scim_groups",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "scim_users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "sso_domains",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_passkeys",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "scim_directories",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "sso_connections",
                schema: "identity");

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                schema: "identity",
                table: "users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "identity",
                table: "user_tokens",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "login_provider",
                schema: "identity",
                table: "user_tokens",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "provider_key",
                schema: "identity",
                table: "user_logins",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "login_provider",
                schema: "identity",
                table: "user_logins",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);
        }
    }
}
