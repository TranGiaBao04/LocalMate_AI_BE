using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSingleItineraryPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PlanVersionBinding",
                table: "PaymentOrders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckoutAttemptId",
                table: "PaymentOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductKind",
                table: "PaymentOrders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "SubscriptionPlan");

            migrationBuilder.AddColumn<Guid>(
                name: "SingleItineraryProductVersionId",
                table: "PaymentOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SingleItineraryProductVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(12,0)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SingleItineraryProductVersions", x => x.Id);
                    table.CheckConstraint("CK_SingleProduct_Price", "\"Price\" > 0");
                    table.CheckConstraint("CK_SingleProduct_Version", "\"VersionNumber\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "SingleItineraryEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePaymentOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SingleItineraryProductVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedTripId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SingleItineraryEntitlements", x => x.Id);
                    table.CheckConstraint("CK_SingleEntitlement_Consumption", "(\"ConsumedAt\" IS NULL AND \"ConsumedTripId\" IS NULL) OR (\"ConsumedAt\" IS NOT NULL AND \"ConsumedTripId\" IS NOT NULL AND \"ConsumedAt\" >= \"GrantedAt\")");
                    table.ForeignKey(
                        name: "FK_SingleItineraryEntitlements_PaymentOrders_SourcePaymentOrde~",
                        columns: x => new { x.SourcePaymentOrderId, x.UserId },
                        principalTable: "PaymentOrders",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SingleItineraryEntitlements_SingleItineraryProductVersions_~",
                        column: x => x.SingleItineraryProductVersionId,
                        principalTable: "SingleItineraryProductVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SingleItineraryEntitlements_Trips_ConsumedTripId",
                        column: x => x.ConsumedTripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SingleItineraryEntitlements_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SingleItineraryProductVersions",
                columns: new[] { "Id", "CreatedAt", "Price", "PublishedAt", "VersionNumber" },
                values: new object[] { new Guid("30000000-0000-0000-0000-000000000001"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 29000m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_SingleItineraryProductVersionId",
                table: "PaymentOrders",
                column: "SingleItineraryProductVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_UserId_CheckoutAttemptId",
                table: "PaymentOrders",
                columns: new[] { "UserId", "CheckoutAttemptId" },
                unique: true,
                filter: "\"ProductKind\" = 'SingleItinerary'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_Product",
                table: "PaymentOrders",
                sql: "(\"ProductKind\" = 'SubscriptionPlan' AND \"PlanVersionBinding\" IS NOT NULL AND \"SingleItineraryProductVersionId\" IS NULL AND \"CheckoutAttemptId\" IS NULL) OR (\"ProductKind\" = 'SingleItinerary' AND \"Type\" = 'Purchase' AND \"SingleItineraryProductVersionId\" IS NOT NULL AND \"CheckoutAttemptId\" IS NOT NULL AND \"PlanCode\" IS NULL AND \"PlanId\" IS NULL AND \"PlanVersionId\" IS NULL AND \"PlanVersionBinding\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_SingleItineraryEntitlements_ConsumedTripId",
                table: "SingleItineraryEntitlements",
                column: "ConsumedTripId",
                unique: true,
                filter: "\"ConsumedTripId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SingleItineraryEntitlements_SingleItineraryProductVersionId",
                table: "SingleItineraryEntitlements",
                column: "SingleItineraryProductVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SingleItineraryEntitlements_SourcePaymentOrderId",
                table: "SingleItineraryEntitlements",
                column: "SourcePaymentOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SingleItineraryEntitlements_SourcePaymentOrderId_UserId",
                table: "SingleItineraryEntitlements",
                columns: new[] { "SourcePaymentOrderId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SingleItineraryEntitlements_UserId_ConsumedAt_GrantedAt_Id",
                table: "SingleItineraryEntitlements",
                columns: new[] { "UserId", "ConsumedAt", "GrantedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SingleItineraryProductVersions_VersionNumber",
                table: "SingleItineraryProductVersions",
                column: "VersionNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentOrders_SingleItineraryProductVersions_SingleItinerar~",
                table: "PaymentOrders",
                column: "SingleItineraryProductVersionId",
                principalTable: "SingleItineraryProductVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
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
                CREATE OR REPLACE FUNCTION lm_paid_order_period_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                IF NEW."ProductKind"='SingleItinerary' AND NEW."Status"='Paid' AND
                (NEW."PaidAt" IS NULL OR NOT EXISTS (SELECT 1 FROM "SingleItineraryEntitlements" e
                WHERE e."SourcePaymentOrderId"=NEW."Id" AND e."UserId"=NEW."UserId"
                AND e."SingleItineraryProductVersionId"=NEW."SingleItineraryProductVersionId")) THEN
                RAISE EXCEPTION 'Paid Single order requires its entitlement' USING ERRCODE='23514';
                END IF;
                IF NEW."ProductKind"='SubscriptionPlan' AND NEW."PlanVersionBinding"='Native' AND NEW."Status"='Paid' AND
                (NEW."PaidAt" IS NULL OR NOT EXISTS (
                SELECT 1 FROM "SubscriptionPeriods" WHERE "SourcePaymentOrderId"=NEW."Id")) THEN
                RAISE EXCEPTION 'Paid native order requires its entitlement period' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
                END $$;
                CREATE FUNCTION lm_single_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Single contract and grant evidence are permanent' USING ERRCODE='23514'; END $$;
                CREATE TRIGGER immutable_single_version BEFORE UPDATE OR DELETE ON "SingleItineraryProductVersions"
                FOR EACH ROW EXECUTE FUNCTION lm_single_immutable();
                CREATE TRIGGER no_truncate_single_versions BEFORE TRUNCATE ON "SingleItineraryProductVersions"
                FOR EACH STATEMENT EXECUTE FUNCTION lm_single_immutable();
                CREATE TRIGGER no_delete_single_grant BEFORE DELETE ON "SingleItineraryEntitlements"
                FOR EACH ROW EXECUTE FUNCTION lm_single_immutable();
                CREATE TRIGGER no_truncate_single_grants BEFORE TRUNCATE ON "SingleItineraryEntitlements"
                FOR EACH STATEMENT EXECUTE FUNCTION lm_single_immutable();

                CREATE FUNCTION lm_single_grant_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                IF TG_OP='INSERT' AND (NEW."ConsumedAt" IS NOT NULL OR NEW."ConsumedTripId" IS NOT NULL) THEN
                RAISE EXCEPTION 'Single grants must start unbound' USING ERRCODE='23514';
                END IF;
                IF TG_OP='UPDATE' THEN
                IF (NEW."Id",NEW."UserId",NEW."SourcePaymentOrderId",NEW."SingleItineraryProductVersionId",NEW."GrantedAt")
                IS DISTINCT FROM (OLD."Id",OLD."UserId",OLD."SourcePaymentOrderId",OLD."SingleItineraryProductVersionId",OLD."GrantedAt")
                OR OLD."ConsumedAt" IS NOT NULL OR OLD."ConsumedTripId" IS NOT NULL
                OR NEW."ConsumedAt" IS NULL OR NEW."ConsumedTripId" IS NULL THEN
                RAISE EXCEPTION 'Grant provenance and consumed target are immutable' USING ERRCODE='23514';
                END IF;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM "PaymentOrders" o WHERE o."Id"=NEW."SourcePaymentOrderId"
                AND o."UserId"=NEW."UserId" AND o."ProductKind"='SingleItinerary' AND o."Type"='Purchase'
                AND o."SingleItineraryProductVersionId"=NEW."SingleItineraryProductVersionId") THEN
                RAISE EXCEPTION 'Single grant source mismatch' USING ERRCODE='23514';
                END IF;
                IF NEW."ConsumedAt" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "Trips" t WHERE t."Id"=NEW."ConsumedTripId"
                AND t."UserId"=NEW."UserId" AND t."Status"='Finalized' AND t."DeletedAt" IS NULL) THEN
                RAISE EXCEPTION 'Consumed target must be an owned finalized trip' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
                END $$;
                CREATE TRIGGER protect_single_grant BEFORE INSERT OR UPDATE ON "SingleItineraryEntitlements"
                FOR EACH ROW EXECUTE FUNCTION lm_single_grant_guard();
                CREATE FUNCTION lm_single_grant_paid_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                IF NOT EXISTS (SELECT 1 FROM "PaymentOrders" o WHERE o."Id"=NEW."SourcePaymentOrderId"
                AND o."Status"='Paid' AND o."PaidAt"=NEW."GrantedAt") THEN
                RAISE EXCEPTION 'Single grant requires an atomic paid source' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER single_requires_paid_source AFTER INSERT ON "SingleItineraryEntitlements"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION lm_single_grant_paid_guard();
                CREATE FUNCTION lm_single_trip_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                IF (NEW."UserId",NEW."Status") IS DISTINCT FROM (OLD."UserId",OLD."Status")
                AND EXISTS (SELECT 1 FROM "SingleItineraryEntitlements" e WHERE e."ConsumedTripId"=OLD."Id") THEN
                RAISE EXCEPTION 'Purchased finalized trip cannot change owner or state' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
                END $$;
                CREATE TRIGGER protect_single_trip BEFORE UPDATE ON "Trips" FOR EACH ROW EXECUTE FUNCTION lm_single_trip_guard();

                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                IF EXISTS(SELECT 1 FROM "PaymentOrders" WHERE "ProductKind"='SingleItinerary')
                OR EXISTS(SELECT 1 FROM "SingleItineraryEntitlements") THEN
                RAISE EXCEPTION 'Cannot remove permanent Single purchase evidence' USING ERRCODE='23514';
                END IF;
                END $$;
                DROP FUNCTION lm_single_trip_guard() CASCADE;
                DROP FUNCTION lm_single_grant_paid_guard() CASCADE;
                DROP FUNCTION lm_single_grant_guard() CASCADE;
                DROP FUNCTION lm_single_immutable() CASCADE;
                CREATE OR REPLACE FUNCTION lm_order_version_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
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
                CREATE OR REPLACE FUNCTION lm_paid_order_period_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                IF NEW."PlanVersionBinding"='Native' AND NEW."Status"='Paid' AND
                (NEW."PaidAt" IS NULL OR NOT EXISTS (
                SELECT 1 FROM "SubscriptionPeriods" WHERE "SourcePaymentOrderId"=NEW."Id")) THEN
                RAISE EXCEPTION 'Paid native order requires its entitlement period' USING ERRCODE='23514';
                END IF;
                RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentOrders_SingleItineraryProductVersions_SingleItinerar~",
                table: "PaymentOrders");

            migrationBuilder.DropTable(
                name: "SingleItineraryEntitlements");

            migrationBuilder.DropTable(
                name: "SingleItineraryProductVersions");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_SingleItineraryProductVersionId",
                table: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_UserId_CheckoutAttemptId",
                table: "PaymentOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_Product",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "CheckoutAttemptId",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "ProductKind",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "SingleItineraryProductVersionId",
                table: "PaymentOrders");

            migrationBuilder.AlterColumn<string>(
                name: "PlanVersionBinding",
                table: "PaymentOrders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);
        }
    }
}
