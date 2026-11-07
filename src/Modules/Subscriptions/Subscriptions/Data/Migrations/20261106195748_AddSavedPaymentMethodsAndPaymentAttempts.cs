using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subscriptions.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedPaymentMethodsAndPaymentAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "saved_method",
                schema: "subscriptions",
                table: "inbound_webhook_events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    saved_payment_method_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_payment_methods",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_customer_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provider_method_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    brand = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    exp_month = table.Column<int>(type: "integer", nullable: true),
                    exp_year = table.Column<int>(type: "integer", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_payment_methods", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_idempotency_key",
                schema: "subscriptions",
                table: "payment_attempts",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_subscription_id",
                schema: "subscriptions",
                table: "payment_attempts",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_payment_step",
                schema: "subscriptions",
                table: "payment_attempts",
                columns: new[] { "payment_id", "step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saved_payment_methods_subscriber_id",
                schema: "subscriptions",
                table: "saved_payment_methods",
                column: "subscriber_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_payment_methods_subscriber_id1",
                schema: "subscriptions",
                table: "saved_payment_methods",
                column: "subscriber_id",
                unique: true,
                filter: "is_default AND revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_saved_payment_methods_gateway_method",
                schema: "subscriptions",
                table: "saved_payment_methods",
                columns: new[] { "gateway", "provider_method_ref" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_attempts",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "saved_payment_methods",
                schema: "subscriptions");

            migrationBuilder.DropColumn(
                name: "saved_method",
                schema: "subscriptions",
                table: "inbound_webhook_events");
        }
    }
}
