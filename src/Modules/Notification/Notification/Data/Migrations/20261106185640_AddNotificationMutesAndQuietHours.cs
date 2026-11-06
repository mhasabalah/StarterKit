using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notification.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationMutesAndQuietHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category_mutes",
                schema: "notification",
                table: "user_notification_preference",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "quiet_hours_end",
                schema: "notification",
                table: "user_notification_preference",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "quiet_hours_start",
                schema: "notification",
                table: "user_notification_preference",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                schema: "notification",
                table: "user_notification_preference",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category_mutes",
                schema: "notification",
                table: "user_notification_preference");

            migrationBuilder.DropColumn(
                name: "quiet_hours_end",
                schema: "notification",
                table: "user_notification_preference");

            migrationBuilder.DropColumn(
                name: "quiet_hours_start",
                schema: "notification",
                table: "user_notification_preference");

            migrationBuilder.DropColumn(
                name: "time_zone",
                schema: "notification",
                table: "user_notification_preference");
        }
    }
}
