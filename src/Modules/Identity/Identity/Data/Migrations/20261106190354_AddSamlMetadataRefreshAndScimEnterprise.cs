using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSamlMetadataRefreshAndScimEnterprise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "saml_certificates_not_after",
                schema: "identity",
                table: "sso_connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "saml_expiry_alerted_days",
                schema: "identity",
                table: "sso_connections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "saml_expiry_alerted_not_after",
                schema: "identity",
                table: "sso_connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "saml_metadata_last_error",
                schema: "identity",
                table: "sso_connections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "saml_metadata_refreshed_at",
                schema: "identity",
                table: "sso_connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cost_center",
                schema: "identity",
                table: "scim_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "department",
                schema: "identity",
                table: "scim_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "division",
                schema: "identity",
                table: "scim_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "employee_number",
                schema: "identity",
                table: "scim_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "manager_scim_user_id",
                schema: "identity",
                table: "scim_users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "organization",
                schema: "identity",
                table: "scim_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "saml_certificates_not_after",
                schema: "identity",
                table: "sso_connections");

            migrationBuilder.DropColumn(
                name: "saml_expiry_alerted_days",
                schema: "identity",
                table: "sso_connections");

            migrationBuilder.DropColumn(
                name: "saml_expiry_alerted_not_after",
                schema: "identity",
                table: "sso_connections");

            migrationBuilder.DropColumn(
                name: "saml_metadata_last_error",
                schema: "identity",
                table: "sso_connections");

            migrationBuilder.DropColumn(
                name: "saml_metadata_refreshed_at",
                schema: "identity",
                table: "sso_connections");

            migrationBuilder.DropColumn(
                name: "cost_center",
                schema: "identity",
                table: "scim_users");

            migrationBuilder.DropColumn(
                name: "department",
                schema: "identity",
                table: "scim_users");

            migrationBuilder.DropColumn(
                name: "division",
                schema: "identity",
                table: "scim_users");

            migrationBuilder.DropColumn(
                name: "employee_number",
                schema: "identity",
                table: "scim_users");

            migrationBuilder.DropColumn(
                name: "manager_scim_user_id",
                schema: "identity",
                table: "scim_users");

            migrationBuilder.DropColumn(
                name: "organization",
                schema: "identity",
                table: "scim_users");
        }
    }
}
