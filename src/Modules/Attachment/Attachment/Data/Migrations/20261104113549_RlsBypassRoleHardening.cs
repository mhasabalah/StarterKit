using Microsoft.EntityFrameworkCore.Migrations;

using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Attachment.Data.Migrations
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
            migrationBuilder.EnableTenantRowLevelSecurity("attachment", "attachments");
            migrationBuilder.EnableTenantRowLevelSecurity("attachment", "pending_attachments");
            migrationBuilder.EnableTenantRowLevelSecurity("attachment", "outbox_messages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"attachment\".\"attachments\"; CREATE POLICY tenant_isolation ON \"attachment\".\"attachments\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"attachment\".\"pending_attachments\"; CREATE POLICY tenant_isolation ON \"attachment\".\"pending_attachments\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"attachment\".\"outbox_messages\"; CREATE POLICY tenant_isolation ON \"attachment\".\"outbox_messages\" USING ({OLD}) WITH CHECK ({OLD});".Replace("{OLD}", OldPredicate));
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS \"attachment\".tenant_rls_bypass();");
        }
    }
}
