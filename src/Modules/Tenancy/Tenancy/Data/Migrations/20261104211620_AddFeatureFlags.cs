using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tenancy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feature_flags",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    rollout_percent = table.Column<int>(type: "integer", nullable: false),
                    kill_switch = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flags", x => x.id);
                    table.CheckConstraint("ck_feature_flags_rollout", "rollout_percent BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "feature_flag_overrides",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_flag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flag_overrides", x => x.id);
                    table.CheckConstraint("ck_feature_flag_overrides_one_target", "(tenant_id IS NULL) <> (user_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_feature_flag_overrides_feature_flags_feature_flag_id",
                        column: x => x.feature_flag_id,
                        principalSchema: "tenancy",
                        principalTable: "feature_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_feature_flag_overrides_tenant",
                schema: "tenancy",
                table: "feature_flag_overrides",
                columns: new[] { "feature_flag_id", "tenant_id" },
                unique: true,
                filter: "tenant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_feature_flag_overrides_user",
                schema: "tenancy",
                table: "feature_flag_overrides",
                columns: new[] { "feature_flag_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_feature_flags_key",
                schema: "tenancy",
                table: "feature_flags",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_flag_overrides",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "feature_flags",
                schema: "tenancy");
        }
    }
}
