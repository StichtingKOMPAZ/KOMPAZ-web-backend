using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kompaz.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformOrganizationFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPlatform",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // A database that already has platform administrators already has a platform organization; it just has
            // not been told which one it is, and the flag defaults to false, which would leave nobody able to
            // appoint another platform administrator. Adopt the organization most of them sit in, breaking a tie on
            // the identifier so every instance running this picks the same row. A fresh database matches nothing
            // here and gets the flag from the seeder instead.
            migrationBuilder.Sql(
                """
                UPDATE "Organizations" SET "IsPlatform" = TRUE WHERE "Id" = (
                    SELECT "OrganizationId" FROM "Users"
                    WHERE "Role" = 'PlatformAdministrator'
                    GROUP BY "OrganizationId"
                    ORDER BY COUNT(*) DESC, "OrganizationId"
                    LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_IsPlatform",
                table: "Organizations",
                column: "IsPlatform",
                unique: true,
                filter: "\"IsPlatform\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_IsPlatform",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "IsPlatform",
                table: "Organizations");
        }
    }
}
