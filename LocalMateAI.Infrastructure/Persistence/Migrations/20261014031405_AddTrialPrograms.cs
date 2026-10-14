using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialPrograms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrialPrograms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    MaxRecipients = table.Column<int>(type: "integer", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialPrograms", x => x.Id);
                    table.CheckConstraint("CK_TrialPrograms_MaxRecipients", "\"MaxRecipients\" IS NULL OR \"MaxRecipients\" > 0");
                    table.CheckConstraint("CK_TrialPrograms_Range", "\"StartsOn\" <= \"EndsOn\"");
                    table.CheckConstraint("CK_TrialPrograms_TrialDays", "\"TrialDays\" > 0");
                    table.ForeignKey(
                        name: "FK_TrialPrograms_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanGrants_TrialProgramId",
                table: "PlanGrants",
                column: "TrialProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_TrialPrograms_CreatedByUserId",
                table: "TrialPrograms",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlanGrants_TrialPrograms_TrialProgramId",
                table: "PlanGrants",
                column: "TrialProgramId",
                principalTable: "TrialPrograms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlanGrants_TrialPrograms_TrialProgramId",
                table: "PlanGrants");

            migrationBuilder.DropTable(
                name: "TrialPrograms");

            migrationBuilder.DropIndex(
                name: "IX_PlanGrants_TrialProgramId",
                table: "PlanGrants");
        }
    }
}
