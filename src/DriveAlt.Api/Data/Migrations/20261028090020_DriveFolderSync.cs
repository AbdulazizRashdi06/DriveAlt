using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DriveAlt.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DriveFolderSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "drive_sync_folders",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    segments = table.Column<string[]>(type: "text[]", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    available = table.Column<bool>(type: "boolean", nullable: false),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drive_sync_folders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drive_sync_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    folder_id = table.Column<string>(type: "text", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drive_sync_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drive_sync_states",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    scan_requested = table.Column<bool>(type: "boolean", nullable: false),
                    uploader_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    imported = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drive_sync_states", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_drive_sync_folders_parent_id",
                table: "drive_sync_folders",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_drive_sync_items_folder_id",
                table: "drive_sync_items",
                column: "folder_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drive_sync_folders");

            migrationBuilder.DropTable(
                name: "drive_sync_items");

            migrationBuilder.DropTable(
                name: "drive_sync_states");
        }
    }
}
