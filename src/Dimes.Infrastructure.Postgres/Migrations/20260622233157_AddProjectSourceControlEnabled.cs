using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dimes.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectSourceControlEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default existing projects to enabled so the source-control feature stays visible after upgrade.
            migrationBuilder.AddColumn<bool>(
                name: "SourceControlEnabled",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceControlEnabled",
                table: "Projects");
        }
    }
}
