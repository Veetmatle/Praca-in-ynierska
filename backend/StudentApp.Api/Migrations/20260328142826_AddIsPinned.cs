using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsPinned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_UserId_UpdatedAt",
                table: "ChatSessions");

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "ChatSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_IsPinned_UpdatedAt",
                table: "ChatSessions",
                columns: new[] { "UserId", "IsPinned", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_UserId_IsPinned_UpdatedAt",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "ChatSessions");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_UpdatedAt",
                table: "ChatSessions",
                columns: new[] { "UserId", "UpdatedAt" });
        }
    }
}
