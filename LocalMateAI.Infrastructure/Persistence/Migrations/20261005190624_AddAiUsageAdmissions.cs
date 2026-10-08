using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageAdmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiUsageAdmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripIdSnapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VietnamUsageDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AdmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReservedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DispatchAuthorizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecoveryAfter = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    LlmCallLogId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmittedDailyLimit = table.Column<int>(type: "integer", nullable: false),
                    AdmittedExplainLimit = table.Column<int>(type: "integer", nullable: true),
                    FencingToken = table.Column<Guid>(type: "uuid", nullable: false),
                    FencingGeneration = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageAdmissions", x => x.Id);
                    table.CheckConstraint("CK_AiAdmissions_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"FencingToken\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"FencingGeneration\" > 0 AND (\"TripId\" IS NULL OR \"TripId\"=\"TripIdSnapshot\")");
                    table.CheckConstraint("CK_AiAdmissions_Kind", "\"Kind\" IN ('ParseRequest','Explain')");
                    table.CheckConstraint("CK_AiAdmissions_State", "(\"State\"='Reserved' AND \"DispatchAuthorizedAt\" IS NULL AND \"CompletedAt\" IS NULL AND \"Outcome\" IS NULL AND \"LlmCallLogId\" IS NULL AND \"RecoveryAfter\" IS NULL) OR (\"State\"='DispatchAuthorized' AND \"DispatchAuthorizedAt\" IS NOT NULL AND \"CompletedAt\" IS NULL AND \"Outcome\" IS NULL AND \"LlmCallLogId\" IS NULL) OR (\"State\"='Completed' AND \"DispatchAuthorizedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL AND \"Outcome\" IS NOT NULL AND \"Outcome\" IN ('Succeeded','ProviderFailed','InvalidOutput') AND \"LlmCallLogId\" IS NOT NULL AND \"RecoveryAfter\" IS NULL) OR (\"State\"='Released' AND \"DispatchAuthorizedAt\" IS NULL AND \"CompletedAt\" IS NOT NULL AND \"Outcome\" IS NULL AND \"LlmCallLogId\" IS NULL AND \"RecoveryAfter\" IS NULL) OR (\"State\"='Abandoned' AND \"DispatchAuthorizedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL AND \"Outcome\" IS NULL AND \"LlmCallLogId\" IS NULL AND \"RecoveryAfter\" IS NULL)");
                    table.CheckConstraint("CK_AiAdmissions_Terms", "\"AdmittedDailyLimit\" BETWEEN 0 AND 1000 AND ((\"Kind\"='ParseRequest' AND \"TripId\" IS NULL AND \"TripIdSnapshot\" IS NULL AND \"AdmittedExplainLimit\" IS NULL) OR (\"Kind\"='Explain' AND \"TripIdSnapshot\" IS NOT NULL AND \"AdmittedExplainLimit\" IS NOT NULL AND \"AdmittedExplainLimit\" BETWEEN 0 AND 20))");
                    table.CheckConstraint("CK_AiAdmissions_Time", "\"ReservedUntil\"=\"AdmittedAt\" + interval '30 seconds' AND \"VietnamUsageDate\"=((\"AdmittedAt\" AT TIME ZONE 'UTC') + interval '7 hours')::date AND (\"DispatchAuthorizedAt\" IS NULL OR \"DispatchAuthorizedAt\">=\"AdmittedAt\") AND (\"CompletedAt\" IS NULL OR \"CompletedAt\">=COALESCE(\"DispatchAuthorizedAt\",\"AdmittedAt\")) AND (\"RecoveryAfter\" IS NULL OR \"RecoveryAfter\">=\"DispatchAuthorizedAt\")");
                    table.ForeignKey(
                        name: "FK_AiUsageAdmissions_LlmCallLogs_LlmCallLogId",
                        column: x => x.LlmCallLogId,
                        principalTable: "LlmCallLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiUsageAdmissions_SubscriptionPlanVersions_ResolvedPlanVers~",
                        column: x => x.ResolvedPlanVersionId,
                        principalTable: "SubscriptionPlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiUsageAdmissions_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AiUsageAdmissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageAdmissions_LlmCallLogId",
                table: "AiUsageAdmissions",
                column: "LlmCallLogId",
                unique: true,
                filter: "\"LlmCallLogId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageAdmissions_RecoveryAfter_State",
                table: "AiUsageAdmissions",
                columns: new[] { "RecoveryAfter", "State" },
                filter: "\"RecoveryAfter\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageAdmissions_ReservedUntil_State",
                table: "AiUsageAdmissions",
                columns: new[] { "ReservedUntil", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageAdmissions_ResolvedPlanVersionId",
                table: "AiUsageAdmissions",
                column: "ResolvedPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageAdmissions_TripId",
                table: "AiUsageAdmissions",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageAdmissions_TripIdSnapshot_Kind_State",
                table: "AiUsageAdmissions",
                columns: new[] { "TripIdSnapshot", "Kind", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageAdmissions_UserId_VietnamUsageDate_State",
                table: "AiUsageAdmissions",
                columns: new[] { "UserId", "VietnamUsageDate", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsageAdmissions");
        }
    }
}
