using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dimes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TargetActorId",
                table: "Observations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssistConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequesterActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssistantActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Draft = table.Column<string>(type: "TEXT", nullable: true),
                    ObservationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChangeRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistConversations_Actors_AssistantActorId",
                        column: x => x.AssistantActorId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssistConversations_Actors_RequesterActorId",
                        column: x => x.RequesterActorId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssistConversations_ChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "ChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssistConversations_Observations_ObservationId",
                        column: x => x.ObservationId,
                        principalTable: "Observations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssistConversations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sender = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistMessages_Actors_AuthorActorId",
                        column: x => x.AuthorActorId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssistMessages_AssistConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AssistConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_ProjectId_TargetActorId_Status",
                table: "Observations",
                columns: new[] { "ProjectId", "TargetActorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_TargetActorId",
                table: "Observations",
                column: "TargetActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistConversations_AssistantActorId",
                table: "AssistConversations",
                column: "AssistantActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistConversations_ChangeRequestId",
                table: "AssistConversations",
                column: "ChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistConversations_ObservationId",
                table: "AssistConversations",
                column: "ObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistConversations_ProjectId_AssistantActorId_Status",
                table: "AssistConversations",
                columns: new[] { "ProjectId", "AssistantActorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistConversations_RequesterActorId",
                table: "AssistConversations",
                column: "RequesterActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistMessages_AuthorActorId",
                table: "AssistMessages",
                column: "AuthorActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistMessages_ConversationId",
                table: "AssistMessages",
                column: "ConversationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Observations_Actors_TargetActorId",
                table: "Observations",
                column: "TargetActorId",
                principalTable: "Actors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Observations_Actors_TargetActorId",
                table: "Observations");

            migrationBuilder.DropTable(
                name: "AssistMessages");

            migrationBuilder.DropTable(
                name: "AssistConversations");

            migrationBuilder.DropIndex(
                name: "IX_Observations_ProjectId_TargetActorId_Status",
                table: "Observations");

            migrationBuilder.DropIndex(
                name: "IX_Observations_TargetActorId",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "TargetActorId",
                table: "Observations");
        }
    }
}
