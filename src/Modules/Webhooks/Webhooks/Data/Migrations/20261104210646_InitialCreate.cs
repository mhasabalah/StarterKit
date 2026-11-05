using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Webhooks.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "webhooks");

            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    event_types = table.Column<string[]>(type: "text[]", nullable: false),
                    secret_protected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    disabled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    disabled_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_success_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_failure_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_endpoints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_response_code = table.Column<int>(type: "integer", nullable: true),
                    last_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_response_snippet = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    redelivery_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_deliveries_endpoints_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "webhooks",
                        principalTable: "webhook_endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_endpoint_id",
                schema: "webhooks",
                table: "webhook_deliveries",
                column: "endpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_tenant_id_created_at",
                schema: "webhooks",
                table: "webhook_deliveries",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_tenant_id_endpoint_id_created_at",
                schema: "webhooks",
                table: "webhook_deliveries",
                columns: new[] { "tenant_id", "endpoint_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_tenant_id_status_next_attempt_at",
                schema: "webhooks",
                table: "webhook_deliveries",
                columns: new[] { "tenant_id", "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ux_webhook_deliveries_event",
                schema: "webhooks",
                table: "webhook_deliveries",
                columns: new[] { "tenant_id", "endpoint_id", "event_id" },
                unique: true,
                filter: "redelivery_of_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_tenant_id_enabled",
                schema: "webhooks",
                table: "webhook_endpoints",
                columns: new[] { "tenant_id", "enabled" });

            migrationBuilder.CreateIndex(
                name: "ux_webhook_endpoints_url",
                schema: "webhooks",
                table: "webhook_endpoints",
                columns: new[] { "tenant_id", "url" },
                unique: true);

            // Row-level security (forced + tenant_isolation policy) on EVERY table of this tenant-data context.
            migrationBuilder.EnableTenantRowLevelSecurity("webhooks", "webhook_endpoints");
            migrationBuilder.EnableTenantRowLevelSecurity("webhooks", "webhook_deliveries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "webhooks");

            migrationBuilder.DropTable(
                name: "webhook_endpoints",
                schema: "webhooks");
        }
    }
}
