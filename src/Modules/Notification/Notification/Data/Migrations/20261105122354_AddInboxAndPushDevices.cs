using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notification.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxAndPushDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "push_enabled",
                schema: "notification",
                table: "user_notification_preference",
                type: "boolean",
                nullable: false,
                // Existing preference rows predate push: keep it ON for them (an opt-out, never a silent opt-in loss).
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "inbox_notification",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    action_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "push_device",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    app_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    locale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_devices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_inbox_notification_expires",
                schema: "notification",
                table: "inbox_notification",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "idx_inbox_notification_tenant",
                schema: "notification",
                table: "inbox_notification",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "idx_inbox_notification_user_created",
                schema: "notification",
                table: "inbox_notification",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_inbox_notification_user_unread",
                schema: "notification",
                table: "inbox_notification",
                columns: new[] { "user_id", "tenant_id" },
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_inbox_notification_user_correlation",
                schema: "notification",
                table: "inbox_notification",
                columns: new[] { "user_id", "correlation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_push_device_last_seen",
                schema: "notification",
                table: "push_device",
                column: "last_seen_at");

            migrationBuilder.CreateIndex(
                name: "idx_push_device_user_last_seen",
                schema: "notification",
                table: "push_device",
                columns: new[] { "user_id", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ux_push_device_token",
                schema: "notification",
                table: "push_device",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_notification",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "push_device",
                schema: "notification");

            migrationBuilder.DropColumn(
                name: "push_enabled",
                schema: "notification",
                table: "user_notification_preference");
        }
    }
}
