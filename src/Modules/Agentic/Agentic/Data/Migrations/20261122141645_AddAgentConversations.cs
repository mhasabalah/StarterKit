using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Agentic.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "session_id",
                schema: "agentic",
                table: "agent_runs",
                newName: "conversation_id");

            // session_id only grouped runs and was never checked: its values name no conversation, would break the
            // foreign key below and could hold several active runs (the active-turn index). Runs keep everything else.
            migrationBuilder.Sql("UPDATE \"agentic\".\"agent_runs\" SET conversation_id = NULL WHERE conversation_id IS NOT NULL;");

            migrationBuilder.AddColumn<int>(
                name: "conversation_turn",
                schema: "agentic",
                table: "agent_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_conversations",
                schema: "agentic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    message_count = table.Column<int>(type: "integer", nullable: false),
                    last_message_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_conversations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_conversation_id",
                schema: "agentic",
                table: "agent_runs",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ux_agent_runs_active_turn",
                schema: "agentic",
                table: "agent_runs",
                columns: new[] { "tenant_id", "conversation_id" },
                unique: true,
                filter: "conversation_id IS NOT NULL AND status IN ('Queued', 'Running', 'AwaitingApproval')");

            migrationBuilder.CreateIndex(
                name: "ux_agent_runs_conversation_turn",
                schema: "agentic",
                table: "agent_runs",
                columns: new[] { "tenant_id", "conversation_id", "conversation_turn" },
                unique: true,
                filter: "conversation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_agent_conversations_tenant_id_owner_id_last_message_at",
                schema: "agentic",
                table: "agent_conversations",
                columns: new[] { "tenant_id", "owner_id", "last_message_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_agent_runs_agent_conversations_conversation_id",
                schema: "agentic",
                table: "agent_runs",
                column: "conversation_id",
                principalSchema: "agentic",
                principalTable: "agent_conversations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Database-level isolation (second layer under the EF Tenant filter).
            migrationBuilder.EnableTenantRowLevelSecurity("agentic", "agent_conversations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_agent_runs_agent_conversations_conversation_id",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.DropTable(
                name: "agent_conversations",
                schema: "agentic");

            migrationBuilder.DropIndex(
                name: "ix_agent_runs_conversation_id",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.DropIndex(
                name: "ux_agent_runs_active_turn",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.DropIndex(
                name: "ux_agent_runs_conversation_turn",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.DropColumn(
                name: "conversation_turn",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.RenameColumn(
                name: "conversation_id",
                schema: "agentic",
                table: "agent_runs",
                newName: "session_id");
        }
    }
}
