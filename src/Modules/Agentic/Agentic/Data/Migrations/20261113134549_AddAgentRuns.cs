using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Agentic.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_runs",
                schema: "agentic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    agent_version = table.Column<int>(type: "integer", nullable: false),
                    model_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    autonomy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    trigger_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trigger_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    initiated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_ceiling = table.Column<string[]>(type: "text[]", nullable: true),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: true),
                    input_json = table.Column<string>(type: "jsonb", nullable: true),
                    output_json = table.Column<string>(type: "jsonb", nullable: true),
                    output_text = table.Column<string>(type: "text", nullable: true),
                    max_model_turns = table.Column<int>(type: "integer", nullable: false),
                    max_tool_calls = table.Column<int>(type: "integer", nullable: false),
                    max_tokens = table.Column<long>(type: "bigint", nullable: false),
                    max_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    model_turns = table.Column<int>(type: "integer", nullable: false),
                    tool_calls = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    estimated_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    active_milliseconds = table.Column<long>(type: "bigint", nullable: false),
                    next_sequence = table.Column<int>(type: "integer", nullable: false),
                    tainted = table.Column<bool>(type: "boolean", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lease_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_enqueued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    trace_parent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_run_steps",
                schema: "agentic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    model_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    finish_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    turn_sequence = table.Column<int>(type: "integer", nullable: true),
                    call_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tool_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    tool_version = table.Column<int>(type: "integer", nullable: true),
                    arguments_json = table.Column<string>(type: "text", nullable: true),
                    result_json = table.Column<string>(type: "text", nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    executions = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_run_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_run_steps_agent_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "agentic",
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_run_steps_run_id",
                schema: "agentic",
                table: "agent_run_steps",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ux_agent_run_steps_sequence",
                schema: "agentic",
                table: "agent_run_steps",
                columns: new[] { "tenant_id", "run_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_tenant_id_created_at",
                schema: "agentic",
                table: "agent_runs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_tenant_id_initiated_by_created_at",
                schema: "agentic",
                table: "agent_runs",
                columns: new[] { "tenant_id", "initiated_by", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_tenant_id_status",
                schema: "agentic",
                table: "agent_runs",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_agent_runs_trigger",
                schema: "agentic",
                table: "agent_runs",
                columns: new[] { "tenant_id", "agent_key", "trigger_key" },
                unique: true,
                filter: "trigger_key IS NOT NULL");

            // Database-level isolation (second layer under the EF Tenant filter): forced RLS on every new table.
            migrationBuilder.EnableTenantRowLevelSecurity("agentic", "agent_runs");
            migrationBuilder.EnableTenantRowLevelSecurity("agentic", "agent_run_steps");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_run_steps",
                schema: "agentic");

            migrationBuilder.DropTable(
                name: "agent_runs",
                schema: "agentic");
        }
    }
}
