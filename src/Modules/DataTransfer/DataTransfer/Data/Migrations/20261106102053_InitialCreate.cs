using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace DataTransfer.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "datatransfer");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "datatransfer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    host_scope = table.Column<bool>(type: "boolean", nullable: false),
                    trace_parent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lock_token = table.Column<Guid>(type: "uuid", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dead_lettered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfer_jobs",
                schema: "datatransfer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    definition_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    policy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    source_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_report_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dry_run_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    filter_json = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    total_rows = table.Column<long>(type: "bigint", nullable: true),
                    processed_rows = table.Column<long>(type: "bigint", nullable: false),
                    created_rows = table.Column<long>(type: "bigint", nullable: false),
                    updated_rows = table.Column<long>(type: "bigint", nullable: false),
                    unchanged_rows = table.Column<long>(type: "bigint", nullable: false),
                    failed_rows = table.Column<long>(type: "bigint", nullable: false),
                    stored_errors = table.Column<int>(type: "integer", nullable: false),
                    last_committed_row = table.Column<long>(type: "bigint", nullable: false),
                    validated = table.Column<bool>(type: "boolean", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lease_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_enqueued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfer_job_errors",
                schema: "datatransfer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<long>(type: "bigint", nullable: false),
                    column = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_job_errors", x => x.id);
                    table.ForeignKey(
                        name: "fk_transfer_job_errors_transfer_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "datatransfer",
                        principalTable: "transfer_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "datatransfer",
                table: "outbox_messages",
                column: "next_attempt_at",
                filter: "processed_at IS NULL AND dead_lettered_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "datatransfer",
                table: "outbox_messages",
                column: "processed_at",
                filter: "processed_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "datatransfer",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_job_errors_job_id",
                schema: "datatransfer",
                table: "transfer_job_errors",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_job_errors_tenant_id_job_id_row_number",
                schema: "datatransfer",
                table: "transfer_job_errors",
                columns: new[] { "tenant_id", "job_id", "row_number" });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_jobs_tenant_id_created_at",
                schema: "datatransfer",
                table: "transfer_jobs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_jobs_tenant_id_expires_at",
                schema: "datatransfer",
                table: "transfer_jobs",
                columns: new[] { "tenant_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_jobs_tenant_id_requested_by_created_at",
                schema: "datatransfer",
                table: "transfer_jobs",
                columns: new[] { "tenant_id", "requested_by", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_jobs_tenant_id_status",
                schema: "datatransfer",
                table: "transfer_jobs",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_transfer_jobs_active_import",
                schema: "datatransfer",
                table: "transfer_jobs",
                columns: new[] { "tenant_id", "definition_key" },
                unique: true,
                filter: "kind = 'Import' AND status IN ('Pending', 'Running')");

            // Row-level security (forced + tenant_isolation policy) on EVERY table of this tenant-data context.
            migrationBuilder.EnableTenantRowLevelSecurity("datatransfer", "transfer_jobs");
            migrationBuilder.EnableTenantRowLevelSecurity("datatransfer", "transfer_job_errors");
            migrationBuilder.EnableTenantRowLevelSecurity("datatransfer", "outbox_messages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "datatransfer");

            migrationBuilder.DropTable(
                name: "transfer_job_errors",
                schema: "datatransfer");

            migrationBuilder.DropTable(
                name: "transfer_jobs",
                schema: "datatransfer");
        }
    }
}
