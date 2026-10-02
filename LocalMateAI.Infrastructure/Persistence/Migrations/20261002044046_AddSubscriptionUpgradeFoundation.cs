using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionUpgradeFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TerminatedAt",
                table: "SubscriptionPeriods",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TerminatedByOrderId",
                table: "SubscriptionPeriods",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditAmount",
                table: "PaymentOrders",
                type: "numeric(12,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "PaymentOrderCredits",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemainingDays = table.Column<int>(type: "integer", nullable: false),
                    CalculatedCreditAmount = table.Column<decimal>(type: "numeric(12,0)", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOrderCredits", x => new { x.OrderId, x.PeriodId });
                    table.CheckConstraint("CK_OrderCredits_Amount", "\"CalculatedCreditAmount\" >= 0");
                    table.CheckConstraint("CK_OrderCredits_Days", "\"RemainingDays\" >= 0");
                    table.ForeignKey(
                        name: "FK_PaymentOrderCredits_PaymentOrders_OrderId_UserId",
                        columns: x => new { x.OrderId, x.UserId },
                        principalTable: "PaymentOrders",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentOrderCredits_SubscriptionPeriods_PeriodId_UserId",
                        columns: x => new { x.PeriodId, x.UserId },
                        principalTable: "SubscriptionPeriods",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_TerminatedByOrderId_UserId",
                table: "SubscriptionPeriods",
                columns: new[] { "TerminatedByOrderId", "UserId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Periods_Termination",
                table: "SubscriptionPeriods",
                sql: "(\"TerminatedAt\" IS NULL) = (\"TerminatedByOrderId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_Credit",
                table: "PaymentOrders",
                sql: "\"CreditAmount\" >= 0 AND (\"Type\" = 'Upgrade' OR \"CreditAmount\" = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_Upgrade",
                table: "PaymentOrders",
                sql: "\"Type\" <> 'Upgrade' OR (\"ProductKind\" = 'SubscriptionPlan' AND \"PlanVersionBinding\" = 'Native' AND \"Amount\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrderCredits_OrderId_UserId",
                table: "PaymentOrderCredits",
                columns: new[] { "OrderId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrderCredits_PeriodId_UserId",
                table: "PaymentOrderCredits",
                columns: new[] { "PeriodId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "UX_OrderCredits_UnreleasedPeriod",
                table: "PaymentOrderCredits",
                column: "PeriodId",
                unique: true,
                filter: "\"ReleasedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPeriods_PaymentOrders_TerminatedByOrderId_UserId",
                table: "SubscriptionPeriods",
                columns: new[] { "TerminatedByOrderId", "UserId" },
                principalTable: "PaymentOrders",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION lm_order_version_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                IF TG_OP='UPDATE' AND NEW."CreditAmount" IS DISTINCT FROM OLD."CreditAmount" THEN
                RAISE EXCEPTION 'Credit financial snapshot is immutable' USING ERRCODE='23514';
                END IF;
                IF TG_OP='UPDATE' AND NEW."ProductKind" IS DISTINCT FROM OLD."ProductKind" THEN
                RAISE EXCEPTION 'Product identity is immutable' USING ERRCODE='23514';
                END IF;
                IF NEW."ProductKind"='SingleItinerary' THEN
                IF TG_OP='UPDATE' AND (NEW."UserId",NEW."Amount",NEW."ProviderOrderCode",NEW."Type",
                NEW."SingleItineraryProductVersionId",NEW."CheckoutAttemptId",NEW."PlanId",NEW."PlanVersionId",
                NEW."PlanCode",NEW."PlanVersionBinding",NEW."ExpiresAt") IS DISTINCT FROM
                (OLD."UserId",OLD."Amount",OLD."ProviderOrderCode",OLD."Type",
                OLD."SingleItineraryProductVersionId",OLD."CheckoutAttemptId",OLD."PlanId",OLD."PlanVersionId",
                OLD."PlanCode",OLD."PlanVersionBinding",OLD."ExpiresAt") THEN
                RAISE EXCEPTION 'Single purchase snapshot is immutable' USING ERRCODE='23514';
                END IF;
                IF TG_OP='UPDATE' AND OLD."Status"='Paid' AND
                (NEW."Status",NEW."PaidAt") IS DISTINCT FROM (OLD."Status",OLD."PaidAt") THEN
                RAISE EXCEPTION 'Paid purchase is terminal' USING ERRCODE='23514';
                END IF;
                IF NEW."Type"<>'Purchase' OR NEW."CreditAmount"<>0 OR NOT EXISTS (
                SELECT 1 FROM "SingleItineraryProductVersions" v
                WHERE v."Id"=NEW."SingleItineraryProductVersionId" AND v."Price"=NEW."Amount") THEN
                RAISE EXCEPTION 'Single order does not match purchased contract' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
                END IF;
                IF NEW."PlanVersionBinding" NOT IN ('Native','LegacyUnresolved','LegacyVerified','LegacyApprovedBaseline') THEN
                RAISE EXCEPTION 'Unknown version binding' USING ERRCODE='23514';
                END IF;
                IF TG_OP='UPDATE' AND OLD."PlanVersionBinding"='Native' AND
                (NEW."UserId",NEW."PlanId",NEW."PlanVersionId",NEW."Amount",NEW."PlanVersionBinding",NEW."PlanCode",NEW."Type",NEW."ProviderOrderCode")
                IS DISTINCT FROM
                (OLD."UserId",OLD."PlanId",OLD."PlanVersionId",OLD."Amount",OLD."PlanVersionBinding",OLD."PlanCode",OLD."Type",OLD."ProviderOrderCode") THEN
                RAISE EXCEPTION 'Native purchase snapshot is immutable' USING ERRCODE='23514';
                END IF;
                IF NEW."PlanVersionBinding"<>'LegacyUnresolved' AND NOT EXISTS (
                SELECT 1 FROM "SubscriptionPlanVersions" v JOIN "SubscriptionPlans" p ON p."Id"=v."PlanId"
                WHERE v."Id"=NEW."PlanVersionId" AND v."PlanId"=NEW."PlanId" AND v."Price"=NEW."Amount"+NEW."CreditAmount"
                AND p."Code"<>'FREE') THEN
                RAISE EXCEPTION 'Order does not match purchased version' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
                END $$;

                CREATE FUNCTION lm_upgrade_credit_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='INSERT' THEN
                    IF NEW."ReleasedAt" IS NOT NULL OR NOT EXISTS (
                      SELECT 1 FROM "PaymentOrders" o WHERE o."Id"=NEW."OrderId" AND o."UserId"=NEW."UserId"
                        AND o."ProductKind"='SubscriptionPlan' AND o."Type"='Upgrade') THEN
                      RAISE EXCEPTION 'Only owned Upgrade orders may reserve periods' USING ERRCODE='23514';
                    END IF;
                    IF NOT EXISTS (
                      SELECT 1 FROM "SubscriptionPeriods" p JOIN "SubscriptionPlanVersions" v ON v."Id"=p."PlanVersionId"
                      WHERE p."Id"=NEW."PeriodId" AND p."UserId"=NEW."UserId"
                        AND p."EndsAt"=NEW."OriginalEndsAt" AND p."TerminatedAt" IS NULL
                        AND NEW."RemainingDays"<=v."DurationDays") THEN
                      RAISE EXCEPTION 'Credit snapshot does not match source period' USING ERRCODE='23514';
                    END IF;
                  ELSE
                    IF (NEW."OrderId",NEW."PeriodId",NEW."UserId",NEW."OriginalEndsAt",NEW."RemainingDays",NEW."CalculatedCreditAmount")
                      IS DISTINCT FROM
                      (OLD."OrderId",OLD."PeriodId",OLD."UserId",OLD."OriginalEndsAt",OLD."RemainingDays",OLD."CalculatedCreditAmount")
                      OR OLD."ReleasedAt" IS NOT NULL OR NEW."ReleasedAt" IS NULL
                      OR EXISTS (SELECT 1 FROM "PaymentOrders" WHERE "Id"=OLD."OrderId" AND "Status"='Paid')
                      OR EXISTS (SELECT 1 FROM "SubscriptionPeriods" WHERE "Id"=OLD."PeriodId" AND "TerminatedByOrderId"=OLD."OrderId") THEN
                      RAISE EXCEPTION 'Credit snapshots are immutable and consumed claims cannot be released' USING ERRCODE='23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER protect_upgrade_credit BEFORE INSERT OR UPDATE ON "PaymentOrderCredits"
                FOR EACH ROW EXECUTE FUNCTION lm_upgrade_credit_guard();

                CREATE FUNCTION lm_upgrade_evidence_no_delete() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Upgrade/period evidence cannot be deleted or truncated' USING ERRCODE='23514'; END $$;
                CREATE TRIGGER no_delete_credit BEFORE DELETE ON "PaymentOrderCredits"
                FOR EACH ROW EXECUTE FUNCTION lm_upgrade_evidence_no_delete();
                CREATE TRIGGER no_truncate_credit BEFORE TRUNCATE ON "PaymentOrderCredits"
                FOR EACH STATEMENT EXECUTE FUNCTION lm_upgrade_evidence_no_delete();

                DROP TRIGGER immutable_period ON "SubscriptionPeriods";
                CREATE FUNCTION lm_period_termination_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='INSERT' THEN
                    IF NEW."TerminatedAt" IS NOT NULL OR NEW."TerminatedByOrderId" IS NOT NULL THEN
                      RAISE EXCEPTION 'New periods cannot be pre-terminated' USING ERRCODE='23514';
                    END IF;
                  ELSE
                    IF (NEW."Id",NEW."UserId",NEW."PlanId",NEW."PlanVersionId",NEW."StartsAt",NEW."EndsAt",
                        NEW."SourcePaymentOrderId",NEW."LegacyUserSubscriptionId",NEW."CreatedAt",NEW."UpdatedAt")
                      IS DISTINCT FROM
                      (OLD."Id",OLD."UserId",OLD."PlanId",OLD."PlanVersionId",OLD."StartsAt",OLD."EndsAt",
                        OLD."SourcePaymentOrderId",OLD."LegacyUserSubscriptionId",OLD."CreatedAt",OLD."UpdatedAt")
                      OR OLD."TerminatedAt" IS NOT NULL OR OLD."TerminatedByOrderId" IS NOT NULL
                      OR NEW."TerminatedAt" IS NULL OR NEW."TerminatedByOrderId" IS NULL THEN
                      RAISE EXCEPTION 'Only one-way period termination is allowed' USING ERRCODE='23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER protect_period_termination BEFORE INSERT OR UPDATE ON "SubscriptionPeriods"
                FOR EACH ROW EXECUTE FUNCTION lm_period_termination_guard();
                CREATE TRIGGER no_delete_period BEFORE DELETE ON "SubscriptionPeriods"
                FOR EACH ROW EXECUTE FUNCTION lm_upgrade_evidence_no_delete();
                CREATE TRIGGER no_truncate_period BEFORE TRUNCATE ON "SubscriptionPeriods"
                FOR EACH STATEMENT EXECUTE FUNCTION lm_upgrade_evidence_no_delete();

                -- Deferred so termination and the future Paid grant may be written in either order.
                CREATE FUNCTION lm_period_termination_commit_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM "PaymentOrders" o JOIN "PaymentOrderCredits" c ON c."OrderId"=o."Id"
                    WHERE o."Id"=NEW."TerminatedByOrderId" AND o."UserId"=NEW."UserId"
                      AND o."ProductKind"='SubscriptionPlan' AND o."Type"='Upgrade'
                      AND o."Status"='Paid' AND o."PaidAt" IS NOT NULL
                      AND c."PeriodId"=NEW."Id" AND c."ReleasedAt" IS NULL) THEN
                    RAISE EXCEPTION 'Termination requires an owned Paid Upgrade with an unreleased claim' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER termination_requires_paid_upgrade AFTER UPDATE ON "SubscriptionPeriods"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION lm_period_termination_commit_guard();

                ALTER TABLE "SubscriptionPeriods" DROP CONSTRAINT "EX_Periods_UserPlan_NoOverlap";
                ALTER TABLE "SubscriptionPeriods" ADD CONSTRAINT "EX_Periods_UserPlan_NoOverlap"
                EXCLUDE USING gist ("UserId" WITH =, "PlanId" WITH =,
                  tstzrange("StartsAt",GREATEST("StartsAt",LEAST("EndsAt",COALESCE("TerminatedAt","EndsAt"))),'[)') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "PaymentOrders" WHERE "CreditAmount"<>0 OR "Type"='Upgrade')
                    OR EXISTS (SELECT 1 FROM "PaymentOrderCredits")
                    OR EXISTS (SELECT 1 FROM "SubscriptionPeriods" WHERE "TerminatedAt" IS NOT NULL)
                  THEN RAISE EXCEPTION 'Cannot downgrade: Upgrade financial or termination evidence exists'; END IF;
                END $$;
                DROP FUNCTION lm_upgrade_credit_guard() CASCADE;
                DROP FUNCTION lm_period_termination_commit_guard() CASCADE;
                DROP FUNCTION lm_period_termination_guard() CASCADE;
                DROP FUNCTION lm_upgrade_evidence_no_delete() CASCADE;
                CREATE TRIGGER immutable_period BEFORE UPDATE OR DELETE ON "SubscriptionPeriods"
                FOR EACH ROW EXECUTE FUNCTION lm_immutable_subscription_row();
                ALTER TABLE "SubscriptionPeriods" DROP CONSTRAINT "EX_Periods_UserPlan_NoOverlap";
                ALTER TABLE "SubscriptionPeriods" ADD CONSTRAINT "EX_Periods_UserPlan_NoOverlap"
                EXCLUDE USING gist ("UserId" WITH =,"PlanId" WITH =,tstzrange("StartsAt","EndsAt",'[)') WITH &&);
                CREATE OR REPLACE FUNCTION lm_order_version_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                IF TG_OP='UPDATE' AND NEW."ProductKind" IS DISTINCT FROM OLD."ProductKind" THEN
                RAISE EXCEPTION 'Product identity is immutable' USING ERRCODE='23514';
                END IF;
                IF NEW."ProductKind"='SingleItinerary' THEN
                IF TG_OP='UPDATE' AND (NEW."UserId",NEW."Amount",NEW."ProviderOrderCode",NEW."Type",
                NEW."SingleItineraryProductVersionId",NEW."CheckoutAttemptId",NEW."PlanId",NEW."PlanVersionId",
                NEW."PlanCode",NEW."PlanVersionBinding",NEW."ExpiresAt") IS DISTINCT FROM
                (OLD."UserId",OLD."Amount",OLD."ProviderOrderCode",OLD."Type",
                OLD."SingleItineraryProductVersionId",OLD."CheckoutAttemptId",OLD."PlanId",OLD."PlanVersionId",
                OLD."PlanCode",OLD."PlanVersionBinding",OLD."ExpiresAt") THEN
                RAISE EXCEPTION 'Single purchase snapshot is immutable' USING ERRCODE='23514';
                END IF;
                IF TG_OP='UPDATE' AND OLD."Status"='Paid' AND
                (NEW."Status",NEW."PaidAt") IS DISTINCT FROM (OLD."Status",OLD."PaidAt") THEN
                RAISE EXCEPTION 'Paid purchase is terminal' USING ERRCODE='23514';
                END IF;
                IF NEW."Type"<>'Purchase' OR NOT EXISTS (
                SELECT 1 FROM "SingleItineraryProductVersions" v
                WHERE v."Id"=NEW."SingleItineraryProductVersionId" AND v."Price"=NEW."Amount") THEN
                RAISE EXCEPTION 'Single order does not match purchased contract' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
                END IF;
                IF NEW."PlanVersionBinding" NOT IN ('Native','LegacyUnresolved','LegacyVerified','LegacyApprovedBaseline') THEN
                RAISE EXCEPTION 'Unknown version binding' USING ERRCODE='23514';
                END IF;
                IF TG_OP='UPDATE' AND OLD."PlanVersionBinding"='Native' AND
                (NEW."UserId",NEW."PlanId",NEW."PlanVersionId",NEW."Amount",NEW."PlanVersionBinding",NEW."PlanCode",NEW."Type",NEW."ProviderOrderCode")
                IS DISTINCT FROM
                (OLD."UserId",OLD."PlanId",OLD."PlanVersionId",OLD."Amount",OLD."PlanVersionBinding",OLD."PlanCode",OLD."Type",OLD."ProviderOrderCode") THEN
                RAISE EXCEPTION 'Native purchase snapshot is immutable' USING ERRCODE='23514';
                END IF;
                IF NEW."PlanVersionBinding"<>'LegacyUnresolved' AND NOT EXISTS (
                SELECT 1 FROM "SubscriptionPlanVersions" v JOIN "SubscriptionPlans" p ON p."Id"=v."PlanId"
                WHERE v."Id"=NEW."PlanVersionId" AND v."PlanId"=NEW."PlanId" AND v."Price"=NEW."Amount"
                AND p."Code"<>'FREE') THEN
                RAISE EXCEPTION 'Order does not match purchased version' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionPeriods_PaymentOrders_TerminatedByOrderId_UserId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropTable(
                name: "PaymentOrderCredits");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPeriods_TerminatedByOrderId_UserId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Periods_Termination",
                table: "SubscriptionPeriods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_Credit",
                table: "PaymentOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_Upgrade",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "TerminatedAt",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "TerminatedByOrderId",
                table: "SubscriptionPeriods");

            migrationBuilder.DropColumn(
                name: "CreditAmount",
                table: "PaymentOrders");
        }
    }
}
