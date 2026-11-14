using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Core.MultiTenancy.Data;

#nullable disable

namespace Shipping.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shipping");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    host_scope = table.Column<bool>(type: "boolean", nullable: false),
                    trace_parent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lock_token = table.Column<Guid>(type: "uuid", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dead_lettered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    origin_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    destination_country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    hs_code = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    declared_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "declaration_drafts",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hs_code = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    customs_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    duty = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submission_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_declaration_drafts", x => x.id);
                    table.ForeignKey(
                        name: "fk_declaration_drafts_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "shipping",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipment_documents",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    last_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modified_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipment_documents_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "shipping",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_declaration_drafts_shipment_id",
                schema: "shipping",
                table: "declaration_drafts",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ux_declaration_drafts_shipment",
                schema: "shipping",
                table: "declaration_drafts",
                columns: new[] { "tenant_id", "shipment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "shipping",
                table: "outbox_messages",
                column: "next_attempt_at",
                filter: "processed_at IS NULL AND dead_lettered_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "shipping",
                table: "outbox_messages",
                column: "processed_at",
                filter: "processed_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "shipping",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_documents_shipment_id",
                schema: "shipping",
                table: "shipment_documents",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_documents_tenant_id_shipment_id",
                schema: "shipping",
                table: "shipment_documents",
                columns: new[] { "tenant_id", "shipment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_created_at",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_shipments_reference",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "tenant_id", "reference" },
                unique: true);

            // Tenant data: forced row-level security on every table (wiki/MultiTenancy).
            migrationBuilder.EnableTenantRowLevelSecurity("shipping", "outbox_messages");
            migrationBuilder.EnableTenantRowLevelSecurity("shipping", "shipments");
            migrationBuilder.EnableTenantRowLevelSecurity("shipping", "shipment_documents");
            migrationBuilder.EnableTenantRowLevelSecurity("shipping", "declaration_drafts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "declaration_drafts",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipment_documents",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "shipping");
        }
    }
}
