using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancySessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "last_active_tenant_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "identity",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_active_tenant_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "identity",
                table: "refresh_tokens");
        }
    }
}
