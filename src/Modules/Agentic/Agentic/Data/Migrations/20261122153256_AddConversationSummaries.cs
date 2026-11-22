using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentic.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "summary",
                schema: "agentic",
                table: "agent_conversations",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "summary_cost",
                schema: "agentic",
                table: "agent_conversations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "summary_input_tokens",
                schema: "agentic",
                table: "agent_conversations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "summary_output_tokens",
                schema: "agentic",
                table: "agent_conversations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "summary_through_turn",
                schema: "agentic",
                table: "agent_conversations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "summary_updated_at",
                schema: "agentic",
                table: "agent_conversations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "summary",
                schema: "agentic",
                table: "agent_conversations");

            migrationBuilder.DropColumn(
                name: "summary_cost",
                schema: "agentic",
                table: "agent_conversations");

            migrationBuilder.DropColumn(
                name: "summary_input_tokens",
                schema: "agentic",
                table: "agent_conversations");

            migrationBuilder.DropColumn(
                name: "summary_output_tokens",
                schema: "agentic",
                table: "agent_conversations");

            migrationBuilder.DropColumn(
                name: "summary_through_turn",
                schema: "agentic",
                table: "agent_conversations");

            migrationBuilder.DropColumn(
                name: "summary_updated_at",
                schema: "agentic",
                table: "agent_conversations");
        }
    }
}
