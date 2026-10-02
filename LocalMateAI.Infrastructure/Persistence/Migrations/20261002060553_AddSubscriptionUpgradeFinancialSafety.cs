using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionUpgradeFinancialSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "PaymentOrders" WHERE "ProductKind"='SubscriptionPlan' AND "Status"='Pending'
                             GROUP BY "UserId" HAVING count(*)>1) THEN
                    RAISE EXCEPTION 'S3 preflight: duplicate Pending SubscriptionPlan orders require explicit review';
                  END IF;
                  IF EXISTS (SELECT 1 FROM "PaymentOrderCredits" WHERE "ReleasedAt" IS NOT NULL) THEN
                    RAISE EXCEPTION 'S3 preflight: historical released claims lack provider proof; no proof may be fabricated';
                  END IF;
                END $$;
                """);
            migrationBuilder.AddColumn<decimal>(
                name: "ReleaseProviderAmountPaid",
                table: "PaymentOrderCredits",
                type: "numeric(12,0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReleaseProviderAmountRemaining",
                table: "PaymentOrderCredits",
                type: "numeric(12,0)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseProviderCheckedAt",
                table: "PaymentOrderCredits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReleaseProviderRequestedAmount",
                table: "PaymentOrderCredits",
                type: "numeric(12,0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseProviderStatus",
                table: "PaymentOrderCredits",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseReasonCode",
                table: "PaymentOrderCredits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_PaymentOrders_PendingSubscriptionUser",
                table: "PaymentOrders",
                column: "UserId",
                unique: true,
                filter: "\"ProductKind\" = 'SubscriptionPlan' AND \"Status\" = 'Pending'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderCredits_ReleaseProof",
                table: "PaymentOrderCredits",
                sql: "(\"ReleasedAt\" IS NULL AND \"ReleaseProviderCheckedAt\" IS NULL AND \"ReleaseProviderStatus\" IS NULL\n AND \"ReleaseProviderRequestedAmount\" IS NULL AND \"ReleaseProviderAmountPaid\" IS NULL\n AND \"ReleaseProviderAmountRemaining\" IS NULL AND \"ReleaseReasonCode\" IS NULL) OR\n(\"ReleasedAt\" IS NOT NULL AND \"ReleaseProviderCheckedAt\" IS NOT NULL AND \"ReleaseProviderStatus\" IS NOT NULL\n AND \"ReleaseProviderRequestedAmount\" IS NOT NULL AND \"ReleaseProviderAmountPaid\" IS NOT NULL\n AND \"ReleaseProviderAmountRemaining\" IS NOT NULL AND \"ReleaseReasonCode\" IS NOT NULL\n AND \"ReleaseProviderStatus\" = 'Cancelled' AND \"ReleaseProviderRequestedAmount\" > 0\n AND \"ReleaseProviderAmountPaid\" = 0 AND \"ReleaseProviderAmountRemaining\" = \"ReleaseProviderRequestedAmount\"\n AND \"ReleaseReasonCode\" = 'provider_cancelled_no_funds'\n AND \"ReleaseProviderCheckedAt\" <= \"ReleasedAt\"\n AND \"ReleasedAt\" - \"ReleaseProviderCheckedAt\" <= interval '1 minute')");
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION lm_upgrade_credit_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='INSERT' THEN
                    IF NEW."ReleasedAt" IS NOT NULL OR NOT EXISTS (
                      SELECT 1 FROM "PaymentOrders" o WHERE o."Id"=NEW."OrderId" AND o."UserId"=NEW."UserId"
                        AND o."ProductKind"='SubscriptionPlan' AND o."Type"='Upgrade' AND o."Status"='Pending') THEN
                      RAISE EXCEPTION 'Only owned Pending Upgrade orders may reserve periods' USING ERRCODE='23514';
                    END IF;
                    IF NOT EXISTS (
                      SELECT 1 FROM "SubscriptionPeriods" p JOIN "SubscriptionPlanVersions" v ON v."Id"=p."PlanVersionId"
                      WHERE p."Id"=NEW."PeriodId" AND p."UserId"=NEW."UserId"
                        AND p."EndsAt"=NEW."OriginalEndsAt" AND p."TerminatedAt" IS NULL
                        AND NEW."RemainingDays" <= CASE WHEN NEW."CalculatedCreditAmount"=0 THEN
                          GREATEST(1, ((p."EndsAt"-interval '1 microsecond') AT TIME ZONE 'Asia/Ho_Chi_Minh')::date
                            - (p."StartsAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date + 1)
                          ELSE v."DurationDays" END) THEN
                      RAISE EXCEPTION 'Credit snapshot does not match source period' USING ERRCODE='23514';
                    END IF;
                  ELSE
                    IF (NEW."OrderId",NEW."PeriodId",NEW."UserId",NEW."OriginalEndsAt",NEW."RemainingDays",NEW."CalculatedCreditAmount")
                      IS DISTINCT FROM
                      (OLD."OrderId",OLD."PeriodId",OLD."UserId",OLD."OriginalEndsAt",OLD."RemainingDays",OLD."CalculatedCreditAmount")
                      OR OLD."ReleasedAt" IS NOT NULL OR NEW."ReleasedAt" IS NULL
                      OR NOT EXISTS (SELECT 1 FROM "PaymentOrders" o WHERE o."Id"=OLD."OrderId"
                        AND o."UserId"=OLD."UserId" AND o."ProductKind"='SubscriptionPlan' AND o."Type"='Upgrade'
                        AND o."Status" IN ('Failed','Expired') AND o."PaidAt" IS NULL
                        AND o."Amount"=NEW."ReleaseProviderRequestedAmount")
                      OR EXISTS (SELECT 1 FROM "SubscriptionPeriods" WHERE "Id"=OLD."PeriodId" AND "TerminatedAt" IS NOT NULL)
                    THEN RAISE EXCEPTION 'Immutable credit release requires terminal no-funds proof and unused sources' USING ERRCODE='23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "PaymentOrderCredits" WHERE "ReleasedAt" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot downgrade: durable credit release proof exists';
                  END IF;
                END $$;
                CREATE OR REPLACE FUNCTION lm_upgrade_credit_guard() RETURNS trigger LANGUAGE plpgsql AS $$
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
                """);
            migrationBuilder.DropIndex(
                name: "UX_PaymentOrders_PendingSubscriptionUser",
                table: "PaymentOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderCredits_ReleaseProof",
                table: "PaymentOrderCredits");

            migrationBuilder.DropColumn(
                name: "ReleaseProviderAmountPaid",
                table: "PaymentOrderCredits");

            migrationBuilder.DropColumn(
                name: "ReleaseProviderAmountRemaining",
                table: "PaymentOrderCredits");

            migrationBuilder.DropColumn(
                name: "ReleaseProviderCheckedAt",
                table: "PaymentOrderCredits");

            migrationBuilder.DropColumn(
                name: "ReleaseProviderRequestedAmount",
                table: "PaymentOrderCredits");

            migrationBuilder.DropColumn(
                name: "ReleaseProviderStatus",
                table: "PaymentOrderCredits");

            migrationBuilder.DropColumn(
                name: "ReleaseReasonCode",
                table: "PaymentOrderCredits");
        }
    }
}
