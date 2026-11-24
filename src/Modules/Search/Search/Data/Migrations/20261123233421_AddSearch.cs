using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Search.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "search");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "search_documents",
                schema: "search",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_version = table.Column<long>(type: "bigint", nullable: false),
                    rebuild_stamp = table.Column<long>(type: "bigint", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false),
                    suggest_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    filters = table.Column<string>(type: "jsonb", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    indexed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_documents", x => new { x.tenant_id, x.index_key, x.document_id });
                });

            migrationBuilder.CreateTable(
                name: "search_index_states",
                schema: "search",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    index_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    definition_fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    active_stamp = table.Column<long>(type: "bigint", nullable: false),
                    building_stamp = table.Column<long>(type: "bigint", nullable: true),
                    build_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lease_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_built_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    document_count = table.Column<int>(type: "integer", nullable: false),
                    last_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    drift_checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    drift_missing = table.Column<int>(type: "integer", nullable: false),
                    drift_stale = table.Column<int>(type: "integer", nullable: false),
                    drift_orphaned = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_index_states", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_search_documents_filters",
                schema: "search",
                table: "search_documents",
                column: "filters")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_search_documents_search_vector",
                schema: "search",
                table: "search_documents",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_search_documents_suggest_text",
                schema: "search",
                table: "search_documents",
                column: "suggest_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_search_documents_tenant_id_index_key_rebuild_stamp",
                schema: "search",
                table: "search_documents",
                columns: new[] { "tenant_id", "index_key", "rebuild_stamp" });

            migrationBuilder.CreateIndex(
                name: "ix_search_index_states_tenant_id_index_key",
                schema: "search",
                table: "search_index_states",
                columns: new[] { "tenant_id", "index_key" },
                unique: true);

            // Database-level isolation (second layer under the EF Tenant filter and the explicit tenant of every
            // raw statement of the PostgreSQL engine).
            migrationBuilder.EnableTenantRowLevelSecurity("search", "search_documents");
            migrationBuilder.EnableTenantRowLevelSecurity("search", "search_index_states");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "search_documents",
                schema: "search");

            migrationBuilder.DropTable(
                name: "search_index_states",
                schema: "search");
        }
    }
}
