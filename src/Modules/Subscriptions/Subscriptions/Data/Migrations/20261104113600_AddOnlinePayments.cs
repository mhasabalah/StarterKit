using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subscriptions.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlinePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbound_webhook_events",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_webhook_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "online_payment_sessions",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_online_payment_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_webhook_events_provider_event_id",
                schema: "subscriptions",
                table: "inbound_webhook_events",
                columns: new[] { "provider", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbound_webhook_events_status_attempts_last_attempt_at",
                schema: "subscriptions",
                table: "inbound_webhook_events",
                columns: new[] { "status", "attempts", "last_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_online_payment_sessions_payment_id",
                schema: "subscriptions",
                table: "online_payment_sessions",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_online_payment_sessions_provider_provider_reference",
                schema: "subscriptions",
                table: "online_payment_sessions",
                columns: new[] { "provider", "provider_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_webhook_events",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "online_payment_sessions",
                schema: "subscriptions");
        }
    }
}
