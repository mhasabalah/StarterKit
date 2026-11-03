using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tenancy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantMove : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "move_target_connection_secret_ref",
                schema: "tenancy",
                table: "tenants",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "move_target_isolation",
                schema: "tenancy",
                table: "tenants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status_before_move",
                schema: "tenancy",
                table: "tenants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "move_target_connection_secret_ref",
                schema: "tenancy",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "move_target_isolation",
                schema: "tenancy",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "status_before_move",
                schema: "tenancy",
                table: "tenants");
        }
    }
}
