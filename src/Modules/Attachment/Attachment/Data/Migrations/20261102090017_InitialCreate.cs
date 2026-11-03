using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Attachment.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "attachment");

            migrationBuilder.CreateTable(
                name: "attachments",
                schema: "attachment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    file_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    thumbnail_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Ready"),
                    deletion_requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deletion_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_deletion_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pending_attachments",
                schema: "attachment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    file_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    thumbnail_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_associated = table.Column<bool>(type: "boolean", nullable: false),
                    associated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_attachments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_category",
                schema: "attachment",
                table: "attachments",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_content_hash",
                schema: "attachment",
                table: "attachments",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_created_at",
                schema: "attachment",
                table: "attachments",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_entity_type_entity_id",
                schema: "attachment",
                table: "attachments",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_entity_type_entity_id_type",
                schema: "attachment",
                table: "attachments",
                columns: new[] { "entity_type", "entity_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_is_deleted",
                schema: "attachment",
                table: "attachments",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_status_created_at",
                schema: "attachment",
                table: "attachments",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_status_next_deletion_attempt_at",
                schema: "attachment",
                table: "attachments",
                columns: new[] { "status", "next_deletion_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_type",
                schema: "attachment",
                table: "attachments",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_uploaded_by",
                schema: "attachment",
                table: "attachments",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_pending_attachments_created_at",
                schema: "attachment",
                table: "pending_attachments",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_pending_attachments_is_deleted",
                schema: "attachment",
                table: "pending_attachments",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAttachments_ExpiresAt_IsAssociated",
                schema: "attachment",
                table: "pending_attachments",
                columns: new[] { "expires_at", "is_associated" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingAttachments_SessionId",
                schema: "attachment",
                table: "pending_attachments",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAttachments_SessionId_DisplayOrder_Unique",
                schema: "attachment",
                table: "pending_attachments",
                columns: new[] { "session_id", "display_order" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAttachments_SessionId_IsPrimary",
                schema: "attachment",
                table: "pending_attachments",
                columns: new[] { "session_id", "is_primary" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments",
                schema: "attachment");

            migrationBuilder.DropTable(
                name: "pending_attachments",
                schema: "attachment");
        }
    }
}
