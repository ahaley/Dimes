using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dimes.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeKeyAndNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "ChangeRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Key",
                table: "Projects",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_ProjectId_Number",
                table: "ChangeRequests",
                columns: new[] { "ProjectId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Projects_Key",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_ChangeRequests_ProjectId_Number",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "ChangeRequests");
        }
    }
}
