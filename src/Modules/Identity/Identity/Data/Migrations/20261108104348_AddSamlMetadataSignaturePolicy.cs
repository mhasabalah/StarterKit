using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSamlMetadataSignaturePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "saml_metadata_signing_certificates",
                schema: "identity",
                table: "sso_connections",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'"); // existing connections have no pinned metadata key

            migrationBuilder.AddColumn<bool>(
                name: "saml_require_signed_metadata",
                schema: "identity",
                table: "sso_connections",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "saml_metadata_signing_certificates",
                schema: "identity",
                table: "sso_connections");

            migrationBuilder.DropColumn(
                name: "saml_require_signed_metadata",
                schema: "identity",
                table: "sso_connections");
        }
    }
}
