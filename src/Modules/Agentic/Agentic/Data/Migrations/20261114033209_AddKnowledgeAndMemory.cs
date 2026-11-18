using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Agentic.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeAndMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pgvector (ADR-0028). IF NOT EXISTS: a no-op when an administrator already created it, so a migration role
            // that may not create extensions works too (see wiki/deployment/Database-Migrations.md).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.AddColumn<DateTime>(
                name: "content_erased_at",
                schema: "agentic",
                table: "agent_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_memories",
                schema: "agentic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    author_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_memories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                schema: "agentic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_text = table.Column<string>(type: "text", nullable: true),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    indexed_revision = table.Column<int>(type: "integer", nullable: true),
                    chunk_count = table.Column<int>(type: "integer", nullable: false),
                    embedding_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    indexed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_enqueued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                schema: "agentic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    start_offset = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_chunks", x => x.id);
                    table.ForeignKey(
                        name: "fk_knowledge_chunks_knowledge_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "agentic",
                        principalTable: "knowledge_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_memories_tenant_id_expires_at",
                schema: "agentic",
                table: "agent_memories",
                columns: new[] { "tenant_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_memories_tenant_id_scope_owner_user_id",
                schema: "agentic",
                table: "agent_memories",
                columns: new[] { "tenant_id", "scope", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_agent_memories_idempotency",
                schema: "agentic",
                table: "agent_memories",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_document_id",
                schema: "agentic",
                table: "knowledge_chunks",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_tenant_id_document_id_revision_ordinal",
                schema: "agentic",
                table: "knowledge_chunks",
                columns: new[] { "tenant_id", "document_id", "revision", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_tenant_id_attachment_id",
                schema: "agentic",
                table: "knowledge_documents",
                columns: new[] { "tenant_id", "attachment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_tenant_id_status",
                schema: "agentic",
                table: "knowledge_documents",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_documents_external_ref",
                schema: "agentic",
                table: "knowledge_documents",
                columns: new[] { "tenant_id", "source_key", "external_ref" },
                unique: true);

            // Columns the EF model does not map (KnowledgeChunk): the passage vector (untyped, so the embedding model's
            // dimension is not baked into the schema; vectors are compared only within one model) and a generated
            // full-text vector ('simple': no stemming, so Arabic and English behave alike).
            migrationBuilder.Sql("ALTER TABLE agentic.knowledge_chunks ADD COLUMN embedding vector;");
            migrationBuilder.Sql(
                "ALTER TABLE agentic.knowledge_chunks ADD COLUMN search_vector tsvector " +
                "GENERATED ALWAYS AS (to_tsvector('simple', content)) STORED;");
            migrationBuilder.Sql(
                "CREATE INDEX ix_knowledge_chunks_search_vector ON agentic.knowledge_chunks USING gin (search_vector);");

            migrationBuilder.EnableTenantRowLevelSecurity("agentic", "agent_memories");
            migrationBuilder.EnableTenantRowLevelSecurity("agentic", "knowledge_documents");
            migrationBuilder.EnableTenantRowLevelSecurity("agentic", "knowledge_chunks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_memories",
                schema: "agentic");

            migrationBuilder.DropTable(
                name: "knowledge_chunks",
                schema: "agentic");

            migrationBuilder.DropTable(
                name: "knowledge_documents",
                schema: "agentic");

            migrationBuilder.DropColumn(
                name: "content_erased_at",
                schema: "agentic",
                table: "agent_runs");
        }
    }
}
