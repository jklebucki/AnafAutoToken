using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnafAutoToken.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenCheckLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TokenCheckLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<int>(type: "INTEGER", nullable: false),
                    AccessTokenExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenCheckLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TokenCheckLogs_CheckedAt",
                table: "TokenCheckLogs",
                column: "CheckedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TokenCheckLogs");
        }
    }
}
