using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dimes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ArchivedAt",
                table: "Actors",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Actors",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Actors");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Actors");
        }
    }
}
