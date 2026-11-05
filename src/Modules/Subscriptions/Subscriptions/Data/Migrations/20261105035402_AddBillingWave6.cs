using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subscriptions.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingWave6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_subscriptions_subscriber_open",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.AddColumn<string>(
                name: "coupon_code",
                schema: "subscriptions",
                table: "subscriptions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "coupon_discount_type",
                schema: "subscriptions",
                table: "subscriptions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "coupon_id",
                schema: "subscriptions",
                table: "subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "coupon_periods_remaining",
                schema: "subscriptions",
                table: "subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "coupon_value",
                schema: "subscriptions",
                table: "subscriptions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_ends_at",
                schema: "subscriptions",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_reminder_sent_at",
                schema: "subscriptions",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_started_at",
                schema: "subscriptions",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "coupon_code",
                schema: "subscriptions",
                table: "subscription_payments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "coupon_discount_amount",
                schema: "subscriptions",
                table: "subscription_payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "coupon_code",
                schema: "subscriptions",
                table: "subscription_invoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "coupon_discount_amount",
                schema: "subscriptions",
                table: "subscription_invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "trial_days",
                schema: "subscriptions",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "limit",
                schema: "subscriptions",
                table: "plan_features",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reset_period",
                schema: "subscriptions",
                table: "plan_features",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_payment_reference",
                schema: "subscriptions",
                table: "online_payment_sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_reference",
                schema: "subscriptions",
                table: "inbound_webhook_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_refund_id",
                schema: "subscriptions",
                table: "inbound_webhook_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "coupons",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    discount_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    duration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    duration_periods = table.Column<int>(type: "integer", nullable: true),
                    max_redemptions = table.Column<int>(type: "integer", nullable: true),
                    redemption_count = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_plan_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coupons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dunning_notices",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sent = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dunning_notices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "online_payment_refunds",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_refund_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    refund_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booked_by_webhook = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_online_payment_refunds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usage_records",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    period_key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "coupon_redemptions",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coupon_redemptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_coupon_redemptions_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalSchema: "subscriptions",
                        principalTable: "coupons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status_trial_ends_at",
                schema: "subscriptions",
                table: "subscriptions",
                columns: new[] { "status", "trial_ends_at" });

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_subscriber_open",
                schema: "subscriptions",
                table: "subscriptions",
                column: "subscriber_id",
                unique: true,
                filter: "\"status\" IN ('PendingApproval', 'Trialing', 'Active', 'PastDue')");

            migrationBuilder.CreateIndex(
                name: "ix_online_payment_sessions_provider_provider_payment_reference",
                schema: "subscriptions",
                table: "online_payment_sessions",
                columns: new[] { "provider", "provider_payment_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemptions_subscription_id",
                schema: "subscriptions",
                table: "coupon_redemptions",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ux_coupon_redemptions_coupon_subscriber",
                schema: "subscriptions",
                table: "coupon_redemptions",
                columns: new[] { "coupon_id", "subscriber_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_coupons_code",
                schema: "subscriptions",
                table: "coupons",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dunning_notices_subscription_id",
                schema: "subscriptions",
                table: "dunning_notices",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ux_dunning_notices_payment_step",
                schema: "subscriptions",
                table: "dunning_notices",
                columns: new[] { "payment_id", "step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_online_payment_refunds_provider_provider_refund_id",
                schema: "subscriptions",
                table: "online_payment_refunds",
                columns: new[] { "provider", "provider_refund_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_online_payment_refunds_refund_request_id",
                schema: "subscriptions",
                table: "online_payment_refunds",
                column: "refund_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_online_payment_refunds_session_id",
                schema: "subscriptions",
                table: "online_payment_refunds",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_online_payment_refunds_subscription_id",
                schema: "subscriptions",
                table: "online_payment_refunds",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ux_usage_records_subscriber_key_period",
                schema: "subscriptions",
                table: "usage_records",
                columns: new[] { "subscriber_id", "key", "period_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coupon_redemptions",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "dunning_notices",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "online_payment_refunds",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "usage_records",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "coupons",
                schema: "subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_subscriptions_status_trial_ends_at",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "ux_subscriptions_subscriber_open",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_online_payment_sessions_provider_provider_payment_reference",
                schema: "subscriptions",
                table: "online_payment_sessions");

            migrationBuilder.DropColumn(
                name: "coupon_code",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "coupon_discount_type",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "coupon_id",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "coupon_periods_remaining",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "coupon_value",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_ends_at",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_reminder_sent_at",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_started_at",
                schema: "subscriptions",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "coupon_code",
                schema: "subscriptions",
                table: "subscription_payments");

            migrationBuilder.DropColumn(
                name: "coupon_discount_amount",
                schema: "subscriptions",
                table: "subscription_payments");

            migrationBuilder.DropColumn(
                name: "coupon_code",
                schema: "subscriptions",
                table: "subscription_invoices");

            migrationBuilder.DropColumn(
                name: "coupon_discount_amount",
                schema: "subscriptions",
                table: "subscription_invoices");

            migrationBuilder.DropColumn(
                name: "trial_days",
                schema: "subscriptions",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "limit",
                schema: "subscriptions",
                table: "plan_features");

            migrationBuilder.DropColumn(
                name: "reset_period",
                schema: "subscriptions",
                table: "plan_features");

            migrationBuilder.DropColumn(
                name: "provider_payment_reference",
                schema: "subscriptions",
                table: "online_payment_sessions");

            migrationBuilder.DropColumn(
                name: "payment_reference",
                schema: "subscriptions",
                table: "inbound_webhook_events");

            migrationBuilder.DropColumn(
                name: "provider_refund_id",
                schema: "subscriptions",
                table: "inbound_webhook_events");

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_subscriber_open",
                schema: "subscriptions",
                table: "subscriptions",
                column: "subscriber_id",
                unique: true,
                filter: "\"status\" IN ('PendingApproval', 'Active', 'PastDue')");
        }
    }
}
