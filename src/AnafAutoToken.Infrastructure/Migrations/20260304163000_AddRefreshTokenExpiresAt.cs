using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnafAutoToken.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenExpiresAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshTokenExpiresAt",
                table: "TokenRefreshLogs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefreshTokenExpiresAt",
                table: "TokenRefreshLogs");
        }
    }
}
