using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dimes.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentChangeRequestId",
                table: "ChangeRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_ParentChangeRequestId",
                table: "ChangeRequests",
                column: "ParentChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_ProjectId_ParentChangeRequestId",
                table: "ChangeRequests",
                columns: new[] { "ProjectId", "ParentChangeRequestId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequests_ChangeRequests_ParentChangeRequestId",
                table: "ChangeRequests",
                column: "ParentChangeRequestId",
                principalTable: "ChangeRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequests_ChangeRequests_ParentChangeRequestId",
                table: "ChangeRequests");

            migrationBuilder.DropIndex(
                name: "IX_ChangeRequests_ParentChangeRequestId",
                table: "ChangeRequests");

            migrationBuilder.DropIndex(
                name: "IX_ChangeRequests_ProjectId_ParentChangeRequestId",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "ParentChangeRequestId",
                table: "ChangeRequests");
        }
    }
}
