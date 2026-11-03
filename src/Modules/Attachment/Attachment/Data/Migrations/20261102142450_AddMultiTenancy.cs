using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Attachment.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PendingAttachments_SessionId_DisplayOrder_Unique",
                schema: "attachment",
                table: "pending_attachments");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "attachment",
                table: "pending_attachments",
                type: "uuid",
                nullable: false,
                defaultValue: TenantDefaults.DefaultTenantId);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "attachment",
                table: "attachments",
                type: "uuid",
                nullable: false,
                defaultValue: TenantDefaults.DefaultTenantId);

            migrationBuilder.CreateIndex(
                name: "IX_PendingAttachments_SessionId_DisplayOrder_Unique",
                schema: "attachment",
                table: "pending_attachments",
                columns: new[] { "tenant_id", "session_id", "display_order" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_tenant_id",
                schema: "attachment",
                table: "attachments",
                column: "tenant_id");

            // Existing rows were backfilled to the Default tenant (single-tenant data stays the Default tenant's data).
            // Drop the column defaults so nothing can be inserted without an explicit tenant.
            migrationBuilder.Sql("ALTER TABLE \"attachment\".\"attachments\" ALTER COLUMN tenant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"attachment\".\"pending_attachments\" ALTER COLUMN tenant_id DROP DEFAULT;");

            // Database-level isolation (second layer under the EF Tenant filter).
            migrationBuilder.EnableTenantRowLevelSecurity("attachment", "attachments");
            migrationBuilder.EnableTenantRowLevelSecurity("attachment", "pending_attachments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "attachments", "pending_attachments" })
            {
                migrationBuilder.DisableTenantRowLevelSecurity("attachment", table);
            }


            migrationBuilder.DropIndex(
                name: "IX_PendingAttachments_SessionId_DisplayOrder_Unique",
                schema: "attachment",
                table: "pending_attachments");

            migrationBuilder.DropIndex(
                name: "ix_attachments_tenant_id",
                schema: "attachment",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "attachment",
                table: "pending_attachments");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "attachment",
                table: "attachments");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAttachments_SessionId_DisplayOrder_Unique",
                schema: "attachment",
                table: "pending_attachments",
                columns: new[] { "session_id", "display_order" },
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
