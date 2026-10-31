using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DriveAlt.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DriveSyncFolderStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "recursive",
                table: "drive_sync_folders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "drive_sync_seen_files",
                columns: table => new
                {
                    folder_id = table.Column<string>(type: "text", nullable: false),
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    importable = table.Column<bool>(type: "boolean", nullable: false),
                    seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drive_sync_seen_files", x => new { x.folder_id, x.id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drive_sync_seen_files");

            migrationBuilder.DropColumn(
                name: "recursive",
                table: "drive_sync_folders");
        }
    }
}
