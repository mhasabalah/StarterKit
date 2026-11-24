using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Search.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "search_daily_stats",
                schema: "search",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    searches = table.Column<int>(type: "integer", nullable: false),
                    zero_results = table.Column<int>(type: "integer", nullable: false),
                    fuzzy_rescues = table.Column<int>(type: "integer", nullable: false),
                    unanswered = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_daily_stats", x => new { x.tenant_id, x.index_key, x.day });
                });

            migrationBuilder.CreateTable(
                name: "search_zero_result_terms",
                schema: "search",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    term = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_zero_result_terms", x => new { x.tenant_id, x.index_key, x.day, x.term });
                });

            migrationBuilder.CreateIndex(
                name: "ix_search_daily_stats_tenant_id_day",
                schema: "search",
                table: "search_daily_stats",
                columns: new[] { "tenant_id", "day" });

            migrationBuilder.CreateIndex(
                name: "ix_search_zero_result_terms_tenant_id_day",
                schema: "search",
                table: "search_zero_result_terms",
                columns: new[] { "tenant_id", "day" });

            // Database-level isolation (second layer under the EF tenant filter).
            migrationBuilder.EnableTenantRowLevelSecurity("search", "search_daily_stats");
            migrationBuilder.EnableTenantRowLevelSecurity("search", "search_zero_result_terms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "search_daily_stats",
                schema: "search");

            migrationBuilder.DropTable(
                name: "search_zero_result_terms",
                schema: "search");
        }
    }
}
