using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntitlementRepairAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntitlementRepairAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPeriodId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecisionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReconstructionMode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntitlementRepairAudits", x => x.Id);
                    table.CheckConstraint("CK_RepairAudit_Code", "length(\"DecisionCode\") BETWEEN 1 AND 64");
                    table.CheckConstraint("CK_RepairAudit_Outcome", "\"Outcome\" IN ('Repaired','AlreadyGranted','NotEligible','Conflict')");
                    table.CheckConstraint("CK_RepairAudit_Period", "\"Outcome\" NOT IN ('Repaired','AlreadyGranted') OR \"SubscriptionPeriodId\" IS NOT NULL");
                    table.CheckConstraint("CK_RepairAudit_Reason", "length(btrim(\"Reason\")) BETWEEN 1 AND 500 AND \"Reason\" = btrim(\"Reason\") AND \"Reason\" !~ '[[:cntrl:]]'");
                    table.ForeignKey(
                        name: "FK_EntitlementRepairAudits_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntitlementRepairAudits_SubscriptionPeriods_SubscriptionPer~",
                        column: x => x.SubscriptionPeriodId,
                        principalTable: "SubscriptionPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EntitlementRepairAudits_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementRepairAudits_ActorUserId",
                table: "EntitlementRepairAudits",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementRepairAudits_PaymentOrderId",
                table: "EntitlementRepairAudits",
                column: "PaymentOrderId",
                unique: true,
                filter: "\"Outcome\" = 'Repaired'");

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementRepairAudits_PaymentOrderId_OccurredAt_Id",
                table: "EntitlementRepairAudits",
                columns: new[] { "PaymentOrderId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementRepairAudits_SubscriptionPeriodId",
                table: "EntitlementRepairAudits",
                column: "SubscriptionPeriodId");
            migrationBuilder.Sql("""
                CREATE FUNCTION lm_repair_audit_link_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."SubscriptionPeriodId" IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM "SubscriptionPeriods" p JOIN "PaymentOrders" o ON o."Id"=NEW."PaymentOrderId"
                        WHERE p."Id"=NEW."SubscriptionPeriodId" AND p."SourcePaymentOrderId"=o."Id"
                          AND p."UserId"=o."UserId") THEN
                        RAISE EXCEPTION 'Repair audit period must belong to its source order' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER repair_audit_link_guard BEFORE INSERT ON "EntitlementRepairAudits"
                FOR EACH ROW EXECUTE FUNCTION lm_repair_audit_link_guard();
                CREATE FUNCTION lm_protect_repair_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Entitlement repair audit is insert-only' USING ERRCODE='23514';
                END $$;
                CREATE TRIGGER protect_repair_audit BEFORE UPDATE OR DELETE ON "EntitlementRepairAudits"
                FOR EACH ROW EXECUTE FUNCTION lm_protect_repair_audit();
                CREATE TRIGGER protect_repair_audit_truncate BEFORE TRUNCATE ON "EntitlementRepairAudits"
                FOR EACH STATEMENT EXECUTE FUNCTION lm_protect_repair_audit();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "EntitlementRepairAudits") THEN
                        RAISE EXCEPTION 'Unsafe downgrade: retained repair audit exists' USING ERRCODE='23514';
                    END IF;
                END $$;
                DROP TRIGGER repair_audit_link_guard ON "EntitlementRepairAudits";
                DROP TRIGGER protect_repair_audit ON "EntitlementRepairAudits";
                DROP TRIGGER protect_repair_audit_truncate ON "EntitlementRepairAudits";
                DROP FUNCTION lm_repair_audit_link_guard();
                DROP FUNCTION lm_protect_repair_audit();
                """);
            migrationBuilder.DropTable(
                name: "EntitlementRepairAudits");
        }
    }
}
