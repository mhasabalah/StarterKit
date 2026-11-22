using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentic.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentDelegation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_agent_runs_active_turn",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.AddColumn<Guid>(
                name: "parent_step_id",
                schema: "agentic",
                table: "agent_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_runs_parent_run_id",
                schema: "agentic",
                table: "agent_runs",
                column: "parent_run_id");

            migrationBuilder.CreateIndex(
                name: "ux_agent_runs_active_turn",
                schema: "agentic",
                table: "agent_runs",
                columns: new[] { "tenant_id", "conversation_id" },
                unique: true,
                filter: "conversation_id IS NOT NULL AND status IN ('Queued', 'Running', 'AwaitingApproval', 'AwaitingDelegate')");

            migrationBuilder.CreateIndex(
                name: "ux_agent_runs_parent_step",
                schema: "agentic",
                table: "agent_runs",
                columns: new[] { "tenant_id", "parent_step_id" },
                unique: true,
                filter: "parent_step_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_runs_agent_runs_parent_run_id",
                schema: "agentic",
                table: "agent_runs",
                column: "parent_run_id",
                principalSchema: "agentic",
                principalTable: "agent_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_agent_runs_agent_runs_parent_run_id",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.DropIndex(
                name: "ix_agent_runs_parent_run_id",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.DropIndex(
                name: "ux_agent_runs_active_turn",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.DropIndex(
                name: "ux_agent_runs_parent_step",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.DropColumn(
                name: "parent_step_id",
                schema: "agentic",
                table: "agent_runs");

            migrationBuilder.CreateIndex(
                name: "ux_agent_runs_active_turn",
                schema: "agentic",
                table: "agent_runs",
                columns: new[] { "tenant_id", "conversation_id" },
                unique: true,
                filter: "conversation_id IS NOT NULL AND status IN ('Queued', 'Running', 'AwaitingApproval')");
        }
    }
}
