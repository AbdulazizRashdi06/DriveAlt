using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DriveAlt.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PredraftChatReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "more_commentary",
                table: "messages",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "more_commentary",
                table: "messages");
        }
    }
}
