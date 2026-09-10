using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kompaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_OrganizationId_Status",
                table: "Users");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_OrganizationId_DeletedUtc_Status",
                table: "Users",
                columns: new[] { "OrganizationId", "DeletedUtc", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_OrganizationId_DeletedUtc_Status",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_OrganizationId_Status",
                table: "Users",
                columns: new[] { "OrganizationId", "Status" });
        }
    }
}
