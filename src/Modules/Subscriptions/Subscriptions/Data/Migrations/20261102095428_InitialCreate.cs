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
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_action_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_number_counters",
                schema: "subscriptions",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_number_counters", x => x.year);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    refund_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plan_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    payment_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
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
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
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
                    subscriber_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    subscriber_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    complimentary_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    billing_cycle_in_months = table.Column<int>(type: "integer", nullable: false),
                    max_items = table.Column<int>(type: "integer", nullable: true),
                    max_storage_gb = table.Column<int>(type: "integer", nullable: true),
                    max_users = table.Column<int>(type: "integer", nullable: true),
                    features_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    renewal_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    grace_period_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_period_end = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    scheduled_cancellation_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<int>(type: "integer", nullable: true),
                    cancellation_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    deferred_proration_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
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
                    new_plan_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
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
                    subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    credit_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    billing_cycle_in_months = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    buyer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    buyer_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    buyer_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    buyer_tax_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    seller_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    seller_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    seller_tax_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    net_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    prices_include_tax = table.Column<bool>(type: "boolean", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    credited_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    pdf_storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                        name: "fk_subscription_invoices_subscription_invoices_original_invoic",
                        column: x => x.original_invoice_id,
                        principalSchema: "subscriptions",
                        principalTable: "subscription_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                    purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    included_proration_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    original_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    payment_method_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_method_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payment_method_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    payment_method_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    payment_method_description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    proof_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    proof_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    duplicate_proof = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    duplicate_of_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_received = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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

            migrationBuilder.CreateTable(
                name: "subscription_payment_receipts",
                schema: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_payment_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_payment_receipts_subscription_payments_payment",
                        column: x => x.payment_id,
                        principalSchema: "subscriptions",
                        principalTable: "subscription_payments",
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
                name: "ix_admin_action_audit_logs_target_type_target_id",
                schema: "subscriptions",
                table: "admin_action_audit_logs",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_currency_occurred_at",
                schema: "subscriptions",
                table: "ledger_entries",
                columns: new[] { "currency", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_occurred_at",
                schema: "subscriptions",
                table: "ledger_entries",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_payment_id",
                schema: "subscriptions",
                table: "ledger_entries",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_refund_id",
                schema: "subscriptions",
                table: "ledger_entries",
                column: "refund_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_subscriber_id",
                schema: "subscriptions",
                table: "ledger_entries",
                column: "subscriber_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_subscription_id",
                schema: "subscriptions",
                table: "ledger_entries",
                column: "subscription_id");

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
                name: "ix_scheduled_plan_changes_status_effective_at",
                schema: "subscriptions",
                table: "scheduled_plan_changes",
                columns: new[] { "status", "effective_at" });

            migrationBuilder.CreateIndex(
                name: "ux_scheduled_plan_changes_subscription_pending",
                schema: "subscriptions",
                table: "scheduled_plan_changes",
                column: "subscription_id",
                unique: true,
                filter: "\"status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_invoice_number",
                schema: "subscriptions",
                table: "subscription_invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_original_invoice_id",
                schema: "subscriptions",
                table: "subscription_invoices",
                column: "original_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_status",
                schema: "subscriptions",
                table: "subscription_invoices",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_subscriber_id",
                schema: "subscriptions",
                table: "subscription_invoices",
                column: "subscriber_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoices_subscription_id",
                schema: "subscriptions",
                table: "subscription_invoices",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ux_subscription_invoices_payment",
                schema: "subscriptions",
                table: "subscription_invoices",
                column: "payment_id",
                unique: true,
                filter: "\"payment_id\" IS NOT NULL AND \"type\" <> 3");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payment_receipts_payment_id",
                schema: "subscriptions",
                table: "subscription_payment_receipts",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payment_receipts_subscription_id",
                schema: "subscriptions",
                table: "subscription_payment_receipts",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_payment_method_id",
                schema: "subscriptions",
                table: "subscription_payments",
                column: "payment_method_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_proof_content_hash",
                schema: "subscriptions",
                table: "subscription_payments",
                column: "proof_content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_purpose_status_created_at",
                schema: "subscriptions",
                table: "subscription_payments",
                columns: new[] { "purpose", "status", "created_at" });

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
                name: "ix_subscription_payments_subscription_id_status",
                schema: "subscriptions",
                table: "subscription_payments",
                columns: new[] { "subscription_id", "status" });

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
                filter: "\"status\" IN (0, 1, 2, 4)");

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
                name: "ix_subscriptions_plan_id",
                schema: "subscriptions",
                table: "subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status",
                schema: "subscriptions",
                table: "subscriptions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status_grace_period_ends_at",
                schema: "subscriptions",
                table: "subscriptions",
                columns: new[] { "status", "grace_period_ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status_renewal_date",
                schema: "subscriptions",
                table: "subscriptions",
                columns: new[] { "status", "renewal_date" });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_subscriber_email",
                schema: "subscriptions",
                table: "subscriptions",
                column: "subscriber_email");

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_subscriber_open",
                schema: "subscriptions",
                table: "subscriptions",
                column: "subscriber_id",
                unique: true,
                filter: "\"status\" IN ('PendingApproval', 'Active', 'PastDue')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_action_audit_logs",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "invoice_number_counters",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "ledger_entries",
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
                name: "subscription_payment_receipts",
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
                name: "subscription_payments",
                schema: "subscriptions");

            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "subscriptions");
        }
    }
}
