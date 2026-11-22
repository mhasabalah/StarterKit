using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Agentic.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentModelUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_model_usages",
                schema: "agentic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    agent_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    estimated_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_model_usages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_model_usages_tenant_id_created_at",
                schema: "agentic",
                table: "agent_model_usages",
                columns: new[] { "tenant_id", "created_at" });

            // Database-level isolation (second layer under the EF Tenant filter).
            migrationBuilder.EnableTenantRowLevelSecurity("agentic", "agent_model_usages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_model_usages",
                schema: "agentic");
        }
    }
}
