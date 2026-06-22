using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dimes.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Projects");
        }
    }
}
