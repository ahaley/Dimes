using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dimes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActorProjectOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProjectOrderJson",
                table: "Actors",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProjectOrderJson",
                table: "Actors");
        }
    }
}
