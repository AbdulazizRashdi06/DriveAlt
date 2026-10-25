using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DriveAlt.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "more",
                table: "messages",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "options",
                table: "messages",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "more",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "options",
                table: "messages");
        }
    }
}
