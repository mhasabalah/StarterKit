using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subscriptions.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "subscriptions");

            migrationBuilder.CreateSequence(
                name: "invoice_number_seq",
                schema: "subscriptions");

            migrationBuilder.CreateTable(
                name: "admin_action_audit_logs",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_action_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_instructions",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    support_whats_app = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    support_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_instructions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    metadata = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_methods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_feature_definitions",
                schema: "subscriptions",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description_ar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_feature_definitions", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    seed_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description_ar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    billing_cycle_in_months = table.Column<int>(type: "integer", nullable: false),
                    max_items = table.Column<int>(type: "integer", nullable: true),
                    max_storage_gb = table.Column<int>(type: "integer", nullable: true),
                    max_users = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pending_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    price_effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    price_change_announced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_refund_requests",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    billing_cycle_in_months = table.Column<int>(type: "integer", nullable: false),
                    subscription_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    renewal_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    refund_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    external_transaction_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("pk_subscription_refund_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_transition_logs",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_transition_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    billing_cycle_in_months = table.Column<int>(type: "integer", nullable: false),
                    max_items = table.Column<int>(type: "integer", nullable: true),
                    max_storage_gb = table.Column<int>(type: "integer", nullable: true),
                    max_users = table.Column<int>(type: "integer", nullable: true),
                    features_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    idempotency_token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    renewal_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    grace_period_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_period_end = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    scheduled_cancellation_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<int>(type: "integer", nullable: true),
                    cancellation_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_features",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_features", x => x.id);
                    table.ForeignKey(
                        name: "fk_plan_features_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "subscriptions",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_plan_changes",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_plan_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    new_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    new_billing_cycle_in_months = table.Column<int>(type: "integer", nullable: false),
                    new_max_items = table.Column<int>(type: "integer", nullable: true),
                    new_max_storage_gb = table.Column<int>(type: "integer", nullable: true),
                    new_max_users = table.Column<int>(type: "integer", nullable: true),
                    new_features_json = table.Column<string>(type: "jsonb", nullable: false),
                    change_type = table.Column<int>(type: "integer", nullable: false),
                    scheduled_by = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_plan_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheduled_plan_changes_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "subscriptions",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_invoices",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false, defaultValue: 0.15m),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_deferred = table.Column<bool>(type: "boolean", nullable: false),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false),
                    pdf_storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_invoices_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "subscriptions",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_payments",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_method_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payment_method_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    payment_method_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    payment_method_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    proof_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    idempotency_token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_payments_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "subscriptions",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_action_audit_logs_admin_id",
                schema: "subscriptions",
                table: "admin_action_audit_logs",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_action_audit_logs_occurred_at",
                schema: "subscriptions",
                table: "admin_action_audit_logs",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_admin_action_audit_logs_subscriber_id",
                schema: "subscriptions",
                table: "admin_action_audit_logs",
                column: "subscriber_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_sort_order",
                schema: "subscriptions",
                table: "payment_methods",
                column: "sort_order");

            migrationBuilder.CreateIndex(
                name: "ux_plan_features_plan_key",
                schema: "subscriptions",
                table: "plan_features",
                columns: new[] { "plan_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_seed_key",
                schema: "subscriptions",
                table: "plans",
                column: "seed_key",
                unique: true,
                filter: "\"seed_key\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_plan_changes_subscription_id",
                schema: "subscriptions",
                table: "scheduled_plan_changes",
                column: "subscription_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_plan_changes_subscription_id_status",
                schema: "subscriptions",
                table: "scheduled_plan_changes",
                columns: new[] { "subscription_id", "status" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_invoice_number",
                schema: "subscriptions",
                table: "subscription_invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_status_is_voided",
                schema: "subscriptions",
                table: "subscription_invoices",
                columns: new[] { "status", "is_voided" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_subscription_id",
                schema: "subscriptions",
                table: "subscription_invoices",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_payment_method_id",
                schema: "subscriptions",
                table: "subscription_payments",
                column: "payment_method_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_status",
                schema: "subscriptions",
                table: "subscription_payments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_subscription_id",
                schema: "subscriptions",
                table: "subscription_payments",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_refund_requests_status",
                schema: "subscriptions",
                table: "subscription_refund_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_refund_requests_subscriber_id",
                schema: "subscriptions",
                table: "subscription_refund_requests",
                column: "subscriber_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_refund_requests_subscription_active",
                schema: "subscriptions",
                table: "subscription_refund_requests",
                column: "subscription_id",
                unique: true,
                filter: "\"status\" IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_transition_logs_subscriber_id",
                schema: "subscriptions",
                table: "subscription_transition_logs",
                column: "subscriber_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_transition_logs_subscription_id",
                schema: "subscriptions",
                table: "subscription_transition_logs",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_grace_period_ends_at",
                schema: "subscriptions",
                table: "subscriptions",
                column: "grace_period_ends_at");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_renewal_date",
                schema: "subscriptions",
                table: "subscriptions",
                column: "renewal_date");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status",
                schema: "subscriptions",
                table: "subscriptions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status_cancelled_at_period_end_scheduled_canc",
                schema: "subscriptions",
                table: "subscriptions",
                columns: new[] { "status", "cancelled_at_period_end", "scheduled_cancellation_date" },
                filter: "\"cancelled_at_period_end\" = true");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_subscriber_id",
                schema: "subscriptions",
                table: "subscriptions",
                column: "subscriber_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_action_audit_logs",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "payment_instructions",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "plan_feature_definitions",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "plan_features",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "scheduled_plan_changes",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "subscription_invoices",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "subscription_payments",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "subscription_refund_requests",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "subscription_transition_logs",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "subscriptions");

            migrationBuilder.DropSequence(
                name: "invoice_number_seq",
                schema: "subscriptions");
        }
    }
}
