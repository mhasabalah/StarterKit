using Microsoft.EntityFrameworkCore.Migrations;

using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class RlsBypassRoleHardening : Migration
    {
        private const string OldPredicate =
            "current_setting('app.bypass_rls', true) = 'on' OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Recreates the tenant_isolation policies on the bypass helper function (tenant_rls_bypass()), which
            // honors app.bypass_rls only for members of the optional tenant_rls_bypass role (hardened role mode).
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "wishlist_items");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "tags");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "products");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "product_tags");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "product_images");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "product_attributes");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "categories");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "attribute_types");
            migrationBuilder.EnableTenantRowLevelSecurity("catalog", "outbox_messages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"catalog\".\"wishlist_items\"; CREATE POLICY tenant_isolation ON \"catalog\".\"wishlist_items\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"catalog\".\"tags\"; CREATE POLICY tenant_isolation ON \"catalog\".\"tags\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"catalog\".\"products\"; CREATE POLICY tenant_isolation ON \"catalog\".\"products\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"catalog\".\"product_tags\"; CREATE POLICY tenant_isolation ON \"catalog\".\"product_tags\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"catalog\".\"product_images\"; CREATE POLICY tenant_isolation ON \"catalog\".\"product_images\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"catalog\".\"product_attributes\"; CREATE POLICY tenant_isolation ON \"catalog\".\"product_attributes\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"catalog\".\"categories\"; CREATE POLICY tenant_isolation ON \"catalog\".\"categories\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"catalog\".\"attribute_types\"; CREATE POLICY tenant_isolation ON \"catalog\".\"attribute_types\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"catalog\".\"outbox_messages\"; CREATE POLICY tenant_isolation ON \"catalog\".\"outbox_messages\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS \"catalog\".tenant_rls_bypass();");
        }
    }
}
