using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dimes.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited from EF's type default of 0: 0 means "non-admins can't create projects", so
            // backfilling an existing SiteSettings row with it would silently leave upgraded installs
            // disabled while a fresh install (no row, falling back to SiteSettings.DefaultProjectLimit)
            // allowed 3. Keep this in step with that constant.
            migrationBuilder.AddColumn<int>(
                name: "ProjectLimit",
                table: "SiteSettings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByActorId",
                table: "Projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProjectLimit",
                table: "Actors",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CreatedByActorId",
                table: "Projects",
                column: "CreatedByActorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Actors_CreatedByActorId",
                table: "Projects",
                column: "CreatedByActorId",
                principalTable: "Actors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Actors_CreatedByActorId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_CreatedByActorId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProjectLimit",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "CreatedByActorId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProjectLimit",
                table: "Actors");
        }
    }
}
