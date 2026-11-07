using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subscriptions.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedMethodConsentAndDunningRetryOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_saved_payment_methods_subscriber_id1",
                schema: "subscriptions",
                table: "saved_payment_methods",
                newName: "ux_saved_payment_methods_default_per_subscriber");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consented_at",
                schema: "subscriptions",
                table: "saved_payment_methods",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "auto_retry_outcome",
                schema: "subscriptions",
                table: "dunning_notices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consented_at",
                schema: "subscriptions",
                table: "saved_payment_methods");

            migrationBuilder.DropColumn(
                name: "auto_retry_outcome",
                schema: "subscriptions",
                table: "dunning_notices");

            migrationBuilder.RenameIndex(
                name: "ux_saved_payment_methods_default_per_subscriber",
                schema: "subscriptions",
                table: "saved_payment_methods",
                newName: "ix_saved_payment_methods_subscriber_id1");
        }
    }
}
