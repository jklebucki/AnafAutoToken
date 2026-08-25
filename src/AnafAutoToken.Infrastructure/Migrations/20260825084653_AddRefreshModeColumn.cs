using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnafAutoToken.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshModeColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Odswiezenie",
                table: "TokenRefreshLogs",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                // Wszystkie dotychczasowe wpisy pochodza z automatycznego odswiezenia.
                defaultValue: "Auto");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Odswiezenie",
                table: "TokenRefreshLogs");
        }
    }
}
