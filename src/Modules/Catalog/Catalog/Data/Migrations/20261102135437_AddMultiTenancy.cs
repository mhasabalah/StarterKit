using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_wishlist_items_user_product",
                schema: "catalog",
                table: "wishlist_items");

            migrationBuilder.DropIndex(
                name: "ux_tags_name_en",
                schema: "catalog",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "ux_products_sku",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_product_tags_product_id_tag_id",
                schema: "catalog",
                table: "product_tags");

            migrationBuilder.DropIndex(
                name: "ix_product_images_product_id_attachment_id",
                schema: "catalog",
                table: "product_images");

            migrationBuilder.DropIndex(
                name: "ix_product_attributes_product_id_attribute_type_id",
                schema: "catalog",
                table: "product_attributes");

            migrationBuilder.DropIndex(
                name: "ux_categories_slug",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "ux_attribute_types_name_en",
                schema: "catalog",
                table: "attribute_types");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "wishlist_items",
                type: "uuid",
                nullable: false,
                defaultValue: TenantDefaults.DefaultTenantId);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "tags",
                type: "uuid",
                nullable: false,
                defaultValue: TenantDefaults.DefaultTenantId);

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "catalog",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "products",
                type: "uuid",
                nullable: false,
                defaultValue: TenantDefaults.DefaultTenantId);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "product_tags",
                type: "uuid",
                nullable: false,
                defaultValue: TenantDefaults.DefaultTenantId);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "product_images",
                type: "uuid",
                nullable: false,
                defaultValue: TenantDefaults.DefaultTenantId);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "product_attributes",
                type: "uuid",
                nullable: false,
                defaultValue: TenantDefaults.DefaultTenantId);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "categories",
                type: "uuid",
                nullable: false,
                defaultValue: TenantDefaults.DefaultTenantId);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "attribute_types",
                type: "uuid",
                nullable: false,
                defaultValue: TenantDefaults.DefaultTenantId);

            migrationBuilder.CreateIndex(
                name: "ux_wishlist_items_user_product",
                schema: "catalog",
                table: "wishlist_items",
                columns: new[] { "tenant_id", "user_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tags_tenant_id",
                schema: "catalog",
                table: "tags",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_branch_id",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_products_sku",
                schema: "catalog",
                table: "products",
                columns: new[] { "tenant_id", "sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_tags_product_id",
                schema: "catalog",
                table: "product_tags",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_tags_tenant_id_product_id_tag_id",
                schema: "catalog",
                table: "product_tags",
                columns: new[] { "tenant_id", "product_id", "tag_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_images_tenant_id_product_id_attachment_id",
                schema: "catalog",
                table: "product_images",
                columns: new[] { "tenant_id", "product_id", "attachment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_attributes_product_id",
                schema: "catalog",
                table: "product_attributes",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_attributes_tenant_id_product_id_attribute_type_id",
                schema: "catalog",
                table: "product_attributes",
                columns: new[] { "tenant_id", "product_id", "attribute_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_categories_slug",
                schema: "catalog",
                table: "categories",
                columns: new[] { "tenant_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attribute_types_tenant_id",
                schema: "catalog",
                table: "attribute_types",
                column: "tenant_id");

            // Existing rows were backfilled to the Default tenant (single-tenant data stays the Default tenant's data).
            // Drop the column defaults so nothing can be inserted without an explicit tenant.
            migrationBuilder.Sql("ALTER TABLE \"catalog\".\"wishlist_items\" ALTER COLUMN tenant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"catalog\".\"tags\" ALTER COLUMN tenant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"catalog\".\"products\" ALTER COLUMN tenant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"catalog\".\"product_tags\" ALTER COLUMN tenant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"catalog\".\"product_images\" ALTER COLUMN tenant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"catalog\".\"product_attributes\" ALTER COLUMN tenant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"catalog\".\"categories\" ALTER COLUMN tenant_id DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"catalog\".\"attribute_types\" ALTER COLUMN tenant_id DROP DEFAULT;");

            // Owned-type (LocalizedText) names: unique per tenant. Raw SQL because an owned index cannot include the
            // owner's tenant_id; same index names, so IsUniqueViolation(NameIndexName) keeps working.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_tags_name_en ON \"catalog\".\"tags\" (tenant_id, name_en);");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_attribute_types_name_en ON \"catalog\".\"attribute_types\" (tenant_id, name_en);");

            // Database-level isolation (second layer under the EF Tenant filter).
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "wishlist_items");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "tags");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "products");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "product_tags");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "product_images");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "product_attributes");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "categories");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "attribute_types");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "wishlist_items", "tags", "products", "product_tags", "product_images", "product_attributes", "categories", "attribute_types" })
            {
                migrationBuilder.DisableTenantRowLevelSecurity("catalog", table);
            }
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"catalog\".ux_tags_name_en;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"catalog\".ux_attribute_types_name_en;");

            migrationBuilder.DropIndex(
                name: "ux_wishlist_items_user_product",
                schema: "catalog",
                table: "wishlist_items");

            migrationBuilder.DropIndex(
                name: "ix_tags_tenant_id",
                schema: "catalog",
                table: "tags");

            migrationBuilder.DropIndex(
                name: "ix_products_tenant_id_branch_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ux_products_sku",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_product_tags_product_id",
                schema: "catalog",
                table: "product_tags");

            migrationBuilder.DropIndex(
                name: "ix_product_tags_tenant_id_product_id_tag_id",
                schema: "catalog",
                table: "product_tags");

            migrationBuilder.DropIndex(
                name: "ix_product_images_tenant_id_product_id_attachment_id",
                schema: "catalog",
                table: "product_images");

            migrationBuilder.DropIndex(
                name: "ix_product_attributes_product_id",
                schema: "catalog",
                table: "product_attributes");

            migrationBuilder.DropIndex(
                name: "ix_product_attributes_tenant_id_product_id_attribute_type_id",
                schema: "catalog",
                table: "product_attributes");

            migrationBuilder.DropIndex(
                name: "ux_categories_slug",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "ix_attribute_types_tenant_id",
                schema: "catalog",
                table: "attribute_types");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "wishlist_items");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "product_tags");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "product_images");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "product_attributes");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "attribute_types");

            migrationBuilder.CreateIndex(
                name: "ux_wishlist_items_user_product",
                schema: "catalog",
                table: "wishlist_items",
                columns: new[] { "user_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tags_name_en",
                schema: "catalog",
                table: "tags",
                column: "name_en",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_products_sku",
                schema: "catalog",
                table: "products",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_tags_product_id_tag_id",
                schema: "catalog",
                table: "product_tags",
                columns: new[] { "product_id", "tag_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_images_product_id_attachment_id",
                schema: "catalog",
                table: "product_images",
                columns: new[] { "product_id", "attachment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_attributes_product_id_attribute_type_id",
                schema: "catalog",
                table: "product_attributes",
                columns: new[] { "product_id", "attribute_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_categories_slug",
                schema: "catalog",
                table: "categories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_attribute_types_name_en",
                schema: "catalog",
                table: "attribute_types",
                column: "name_en",
                unique: true);
        }
    }
}
