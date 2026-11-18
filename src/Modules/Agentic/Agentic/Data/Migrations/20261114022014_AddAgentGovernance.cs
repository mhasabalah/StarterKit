using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Agentic.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "approve_all_changes",
                schema: "agentic",
                table: "agent_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "agent_approvals",
                schema: "agentic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tool_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tool_version = table.Column<int>(type: "integer", nullable: false),
                    risk = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    required_permission = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    arguments_json = table.Column<string>(type: "text", nullable: false),
                    payload_digest = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    model_rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_approvals", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_approvals_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "agentic",
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_policies",
                schema: "agentic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    autonomy_ceiling = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    allow_automatic_triggers = table.Column<bool>(type: "boolean", nullable: false),
                    require_approval_for_changes = table.Column<bool>(type: "boolean", nullable: false),
                    max_model_turns = table.Column<int>(type: "integer", nullable: true),
                    max_tool_calls = table.Column<int>(type: "integer", nullable: true),
                    max_tokens = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "agentic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "ix_agent_approvals_run_id",
                schema: "agentic",
                table: "agent_approvals",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_approvals_tenant_id_requested_by_requested_at",
                schema: "agentic",
                table: "agent_approvals",
                columns: new[] { "tenant_id", "requested_by", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_approvals_tenant_id_run_id",
                schema: "agentic",
                table: "agent_approvals",
                columns: new[] { "tenant_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_approvals_tenant_id_status_expires_at",
                schema: "agentic",
                table: "agent_approvals",
                columns: new[] { "tenant_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_agent_approvals_step",
                schema: "agentic",
                table: "agent_approvals",
                columns: new[] { "tenant_id", "step_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_agent_policies_agent",
                schema: "agentic",
                table: "agent_policies",
                columns: new[] { "tenant_id", "agent_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity",
                schema: "agentic",
                table: "audit_entries",
                columns: new[] { "tenant_id", "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at",
                schema: "agentic",
                table: "audit_entries",
                column: "occurred_at");

            // Database-level isolation (second layer under the EF Tenant filter): forced RLS on every new table.
            migrationBuilder.EnableTenantRowLevelSecurity("agentic", "agent_approvals");
            migrationBuilder.EnableTenantRowLevelSecurity("agentic", "agent_policies");
            migrationBuilder.EnableTenantRowLevelSecurity("agentic", "audit_entries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_approvals",
                schema: "agentic");

            migrationBuilder.DropTable(
                name: "agent_policies",
                schema: "agentic");

            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "agentic");

            migrationBuilder.DropColumn(
                name: "approve_all_changes",
                schema: "agentic",
                table: "agent_runs");
        }
    }
}
