using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionedSubscriptionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_UserId",
                table: "PaymentOrders");

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPeriodId",
                table: "UsageEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PlanCode",
                table: "PaymentOrders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                table: "PaymentOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanVersionBinding",
                table: "PaymentOrders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PlanVersionId",
                table: "PaymentOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PaymentOrders_Id_UserId",
                table: "PaymentOrders",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateTable(
                name: "SubscriptionPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourcePaymentOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    LegacyUserSubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPeriods", x => x.Id);
                    table.UniqueConstraint("AK_SubscriptionPeriods_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_Periods_Range", "\"StartsAt\" < \"EndsAt\"");
                    table.CheckConstraint("CK_Periods_Source", "(\"SourcePaymentOrderId\" IS NOT NULL) <> (\"LegacyUserSubscriptionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_PaymentOrders_SourcePaymentOrderId_User~",
                        columns: x => new { x.SourcePaymentOrderId, x.UserId },
                        principalTable: "PaymentOrders",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_UserSubscriptions_LegacyUserSubscriptio~",
                        column: x => x.LegacyUserSubscriptionId,
                        principalTable: "UserSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntitlementPriority = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                    table.CheckConstraint("CK_Plans_Code", "\"Code\" ~ '^[A-Z][A-Z0-9_]{0,63}$'");
                    table.CheckConstraint("CK_Plans_Free", "\"Code\" <> 'FREE' OR (\"IsActive\" AND \"IsSystem\" AND \"EntitlementPriority\" = 0)");
                    table.CheckConstraint("CK_Plans_Priority", "\"EntitlementPriority\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlanVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(12,0)", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    GenerateLimit = table.Column<int>(type: "integer", nullable: true),
                    SavedTripLimit = table.Column<int>(type: "integer", nullable: true),
                    Origin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanVersions", x => x.Id);
                    table.UniqueConstraint("AK_SubscriptionPlanVersions_PlanId_Id", x => new { x.PlanId, x.Id });
                    table.CheckConstraint("CK_Versions_Duration", "\"DurationDays\" IS NULL OR \"DurationDays\" > 0");
                    table.CheckConstraint("CK_Versions_Generate", "\"GenerateLimit\" IS NULL OR \"GenerateLimit\" >= 0");
                    table.CheckConstraint("CK_Versions_Number", "\"VersionNumber\" > 0");
                    table.CheckConstraint("CK_Versions_Origin", "\"Origin\" IN ('Published','LegacyBaseline','LegacyReconstructed') AND (\"Origin\" <> 'Published' OR \"PublishedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_Versions_Price", "\"Price\" >= 0");
                    table.CheckConstraint("CK_Versions_Saved", "\"SavedTripLimit\" IS NULL OR \"SavedTripLimit\" >= 0");
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanVersions_SubscriptionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_SubscriptionPeriodId_Type",
                table: "UsageEvents",
                columns: new[] { "SubscriptionPeriodId", "Type" },
                filter: "\"SubscriptionPeriodId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_SubscriptionPeriodId_UserId",
                table: "UsageEvents",
                columns: new[] { "SubscriptionPeriodId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PlanId_PlanVersionId",
                table: "PaymentOrders",
                columns: new[] { "PlanId", "PlanVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_UserId_PlanId_Type_Status",
                table: "PaymentOrders",
                columns: new[] { "UserId", "PlanId", "Type", "Status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_NativeBinding",
                table: "PaymentOrders",
                sql: "\"PlanVersionBinding\" <> 'Native' OR (\"PlanId\" IS NOT NULL AND \"PlanVersionId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_LegacyUserSubscriptionId",
                table: "SubscriptionPeriods",
                column: "LegacyUserSubscriptionId",
                unique: true,
                filter: "\"LegacyUserSubscriptionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_PlanId_PlanVersionId",
                table: "SubscriptionPeriods",
                columns: new[] { "PlanId", "PlanVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_SourcePaymentOrderId",
                table: "SubscriptionPeriods",
                column: "SourcePaymentOrderId",
                unique: true,
                filter: "\"SourcePaymentOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_SourcePaymentOrderId_UserId",
                table: "SubscriptionPeriods",
                columns: new[] { "SourcePaymentOrderId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_UserId_PlanId_EndsAt",
                table: "SubscriptionPeriods",
                columns: new[] { "UserId", "PlanId", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_UserId_StartsAt_EndsAt",
                table: "SubscriptionPeriods",
                columns: new[] { "UserId", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_Code",
                table: "SubscriptionPlans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_EntitlementPriority",
                table: "SubscriptionPlans",
                column: "EntitlementPriority",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_Id_CurrentVersionId",
                table: "SubscriptionPlans",
                columns: new[] { "Id", "CurrentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_IsActive_EntitlementPriority",
                table: "SubscriptionPlans",
                columns: new[] { "IsActive", "EntitlementPriority" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanVersions_PlanId_VersionNumber",
                table: "SubscriptionPlanVersions",
                columns: new[] { "PlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentOrders_SubscriptionPlanVersions_PlanId_PlanVersionId",
                table: "PaymentOrders",
                columns: new[] { "PlanId", "PlanVersionId" },
                principalTable: "SubscriptionPlanVersions",
                principalColumns: new[] { "PlanId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentOrders_SubscriptionPlans_PlanId",
                table: "PaymentOrders",
                column: "PlanId",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsageEvents_SubscriptionPeriods_SubscriptionPeriodId_UserId",
                table: "UsageEvents",
                columns: new[] { "SubscriptionPeriodId", "UserId" },
                principalTable: "SubscriptionPeriods",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPeriods_SubscriptionPlanVersions_PlanId_PlanVer~",
                table: "SubscriptionPeriods",
                columns: new[] { "PlanId", "PlanVersionId" },
                principalTable: "SubscriptionPlanVersions",
                principalColumns: new[] { "PlanId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPeriods_SubscriptionPlans_PlanId",
                table: "SubscriptionPeriods",
                column: "PlanId",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPlans_SubscriptionPlanVersions_Id_CurrentVersio~",
                table: "SubscriptionPlans",
                columns: new[] { "Id", "CurrentVersionId" },
                principalTable: "SubscriptionPlanVersions",
                principalColumns: new[] { "PlanId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                -- Fail atomically on unsupported legacy aggregates rather than silently omit entitlement.
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "UserSubscriptions"
                    WHERE "PlanCode" NOT IN ('TripPass','Membership') OR "StartsAt" >= "EndsAt")
                  THEN RAISE EXCEPTION 'Legacy subscription requires explicit review before import'; END IF;
                END $$;
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                ALTER TABLE "SubscriptionPeriods" ADD CONSTRAINT "EX_Periods_UserPlan_NoOverlap"
                EXCLUDE USING gist ("UserId" WITH =, "PlanId" WITH =, tstzrange("StartsAt", "EndsAt", '[)') WITH &&);

                INSERT INTO "SubscriptionPlans" ("Id","Code","Name","IsSystem","IsActive","CurrentVersionId","EntitlementPriority","CreatedAt","UpdatedAt") VALUES
                ('10000000-0000-0000-0000-000000000001','FREE','Free',true,true,NULL,0,now(),now()),
                ('10000000-0000-0000-0000-000000000002','TRIP_PASS','Trip Pass',true,true,NULL,100,now(),now()),
                ('10000000-0000-0000-0000-000000000003','MEMBERSHIP','Membership',true,true,NULL,200,now(),now());
                INSERT INTO "SubscriptionPlanVersions" ("Id","PlanId","VersionNumber","Price","DurationDays","GenerateLimit","SavedTripLimit","Origin","PublishedAt","CreatedAt","UpdatedAt") VALUES
                ('20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001',1,0,NULL,1,1,'LegacyBaseline',NULL,now(),now()),
                ('20000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000002',1,19000,7,NULL,3,'LegacyBaseline',NULL,now(),now()),
                ('20000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000003',1,59000,30,NULL,NULL,'LegacyBaseline',NULL,now(),now());
                UPDATE "SubscriptionPlans" p SET "CurrentVersionId" = v."Id"
                FROM "SubscriptionPlanVersions" v WHERE v."PlanId" = p."Id";
                -- A matching price is not evidence of historical commercial terms. Preserve every financial field.
                UPDATE "PaymentOrders" o SET "PlanId" = p."Id", "PlanVersionBinding" = 'LegacyUnresolved'
                FROM "SubscriptionPlans" p WHERE p."Code" = CASE o."PlanCode"
                WHEN 'Free' THEN 'FREE' WHEN 'TripPass' THEN 'TRIP_PASS' WHEN 'Membership' THEN 'MEMBERSHIP' END;
                UPDATE "PaymentOrders" SET "PlanVersionBinding" = 'LegacyUnresolved'
                WHERE "PlanVersionBinding" = '';
                -- Import one whole legacy aggregate interval, never inferred individual renewals.
                INSERT INTO "SubscriptionPeriods" ("Id","UserId","PlanId","PlanVersionId","StartsAt","EndsAt",
                "SourcePaymentOrderId","LegacyUserSubscriptionId","CreatedAt","UpdatedAt")
                SELECT s."Id",s."UserId",p."Id",p."CurrentVersionId",s."StartsAt",s."EndsAt",
                NULL,s."Id",s."CreatedAt",s."UpdatedAt"
                FROM "UserSubscriptions" s JOIN "SubscriptionPlans" p ON p."Code" =
                CASE s."PlanCode" WHEN 'TripPass' THEN 'TRIP_PASS' WHEN 'Membership' THEN 'MEMBERSHIP' END;

                CREATE FUNCTION lm_immutable_subscription_row() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Purchased periods and plan versions are immutable' USING ERRCODE = '23514'; END $$;
                CREATE TRIGGER immutable_version BEFORE UPDATE OR DELETE ON "SubscriptionPlanVersions"
                FOR EACH ROW EXECUTE FUNCTION lm_immutable_subscription_row();
                CREATE TRIGGER immutable_period BEFORE UPDATE OR DELETE ON "SubscriptionPeriods"
                FOR EACH ROW EXECUTE FUNCTION lm_immutable_subscription_row();

                CREATE FUNCTION lm_plan_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF TG_OP = 'DELETE' THEN
                   IF OLD."Code" = 'FREE' OR OLD."CurrentVersionId" IS NOT NULL THEN
                      RAISE EXCEPTION 'Published plan cannot be deleted' USING ERRCODE='23514';
                   END IF;
                   RETURN OLD;
                 END IF;
                 IF NEW."Code" <> OLD."Code" OR NEW."IsSystem" <> OLD."IsSystem" OR
                    (NEW."EntitlementPriority" <> OLD."EntitlementPriority" AND
                     EXISTS (SELECT 1 FROM "SubscriptionPlanVersions" WHERE "PlanId" = OLD."Id")) THEN
                    RAISE EXCEPTION 'Plan identity and published priority are immutable' USING ERRCODE='23514';
                 END IF;
                 IF OLD."CurrentVersionId" IS NOT NULL AND NEW."CurrentVersionId" IS NULL THEN
                    RAISE EXCEPTION 'Published pointer cannot be cleared' USING ERRCODE='23514';
                 END IF;
                 IF NEW."CurrentVersionId" IS DISTINCT FROM OLD."CurrentVersionId" AND
                    NOT EXISTS(SELECT 1 FROM "SubscriptionPlanVersions" v WHERE v."Id"=NEW."CurrentVersionId"
                      AND v."PlanId"=NEW."Id" AND v."Origin"='Published') THEN
                    RAISE EXCEPTION 'Current pointer must target a published version' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER protect_plan BEFORE UPDATE OR DELETE ON "SubscriptionPlans"
                FOR EACH ROW EXECUTE FUNCTION lm_plan_guard();

                CREATE FUNCTION lm_version_terms_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE code text;
                BEGIN
                 SELECT "Code" INTO code FROM "SubscriptionPlans" WHERE "Id"=NEW."PlanId";
                 IF (code='FREE' AND (NEW."Price"<>0 OR NEW."DurationDays" IS NOT NULL)) OR
                    (code<>'FREE' AND (NEW."Price"<=0 OR NEW."DurationDays" IS NULL)) THEN
                    RAISE EXCEPTION 'Invalid free/paid version terms' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER protect_version_terms BEFORE INSERT ON "SubscriptionPlanVersions"
                FOR EACH ROW EXECUTE FUNCTION lm_version_terms_guard();

                CREATE FUNCTION lm_order_version_guard() RETURNS trigger LANGUAGE plpgsql AS $$
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
                CREATE TRIGGER protect_order_version BEFORE INSERT OR UPDATE ON "PaymentOrders"
                FOR EACH ROW EXECUTE FUNCTION lm_order_version_guard();

                CREATE FUNCTION lm_period_source_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF EXISTS(SELECT 1 FROM "SubscriptionPlans" WHERE "Id"=NEW."PlanId" AND "Code"='FREE') THEN
                   RAISE EXCEPTION 'Free does not have purchased periods' USING ERRCODE='23514';
                 END IF;
                 IF NEW."SourcePaymentOrderId" IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM "PaymentOrders" o JOIN "SubscriptionPlanVersions" v ON v."Id"=o."PlanVersionId"
                    WHERE o."Id"=NEW."SourcePaymentOrderId" AND o."UserId"=NEW."UserId"
                    AND o."PlanId"=NEW."PlanId" AND o."PlanVersionId"=NEW."PlanVersionId"
                    AND o."PlanVersionBinding"<>'LegacyUnresolved'
                    AND NEW."EndsAt"=NEW."StartsAt" + make_interval(days=>v."DurationDays")) THEN
                   RAISE EXCEPTION 'Period source/version/duration mismatch' USING ERRCODE='23514';
                 END IF;
                 IF NEW."LegacyUserSubscriptionId" IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM "UserSubscriptions" s JOIN "SubscriptionPlans" p ON p."Code" =
                       CASE s."PlanCode" WHEN 'TripPass' THEN 'TRIP_PASS' WHEN 'Membership' THEN 'MEMBERSHIP' END
                    WHERE s."Id"=NEW."LegacyUserSubscriptionId" AND s."UserId"=NEW."UserId"
                      AND p."Id"=NEW."PlanId" AND s."StartsAt"=NEW."StartsAt" AND s."EndsAt"=NEW."EndsAt") THEN
                   RAISE EXCEPTION 'Legacy period does not match aggregate source' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END $$;
                CREATE TRIGGER protect_period_source BEFORE INSERT ON "SubscriptionPeriods"
                FOR EACH ROW EXECUTE FUNCTION lm_period_source_guard();

                -- Check the atomic financial/entitlement result at commit, independent of EF write ordering.
                CREATE FUNCTION lm_period_paid_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF NEW."SourcePaymentOrderId" IS NOT NULL AND NOT EXISTS (
                   SELECT 1 FROM "PaymentOrders" WHERE "Id"=NEW."SourcePaymentOrderId"
                   AND "Status"='Paid' AND "PaidAt" IS NOT NULL) THEN
                   RAISE EXCEPTION 'Native entitlement requires a paid source' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER period_requires_paid_source AFTER INSERT ON "SubscriptionPeriods"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION lm_period_paid_guard();

                CREATE FUNCTION lm_paid_order_period_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                 IF NEW."PlanVersionBinding"='Native' AND NEW."Status"='Paid' AND
                   (NEW."PaidAt" IS NULL OR NOT EXISTS (
                     SELECT 1 FROM "SubscriptionPeriods" WHERE "SourcePaymentOrderId"=NEW."Id")) THEN
                   RAISE EXCEPTION 'Paid native order requires its entitlement period' USING ERRCODE='23514';
                 END IF;
                 RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER paid_order_requires_period AFTER INSERT OR UPDATE ON "PaymentOrders"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION lm_paid_order_period_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM "PaymentOrders" WHERE "PlanVersionBinding"='Native')
                     OR EXISTS(SELECT 1 FROM "SubscriptionPeriods" WHERE "SourcePaymentOrderId" IS NOT NULL)
                     OR EXISTS(SELECT 1 FROM "SubscriptionPlans" WHERE NOT "IsSystem")
                  THEN RAISE EXCEPTION 'Unsafe downgrade: native versioned data exists'; END IF;
                END $$;
                DROP FUNCTION lm_immutable_subscription_row() CASCADE;
                DROP FUNCTION lm_plan_guard() CASCADE;
                DROP FUNCTION lm_version_terms_guard() CASCADE;
                DROP FUNCTION lm_order_version_guard() CASCADE;
                DROP FUNCTION lm_period_source_guard() CASCADE;
                DROP FUNCTION lm_period_paid_guard() CASCADE;
                DROP FUNCTION lm_paid_order_period_guard() CASCADE;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentOrders_SubscriptionPlanVersions_PlanId_PlanVersionId",
                table: "PaymentOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentOrders_SubscriptionPlans_PlanId",
                table: "PaymentOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_UsageEvents_SubscriptionPeriods_SubscriptionPeriodId_UserId",
                table: "UsageEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionPlans_SubscriptionPlanVersions_Id_CurrentVersio~",
                table: "SubscriptionPlans");

            migrationBuilder.DropTable(
                name: "SubscriptionPeriods");

            migrationBuilder.DropTable(
                name: "SubscriptionPlanVersions");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropIndex(
                name: "IX_UsageEvents_SubscriptionPeriodId_Type",
                table: "UsageEvents");

            migrationBuilder.DropIndex(
                name: "IX_UsageEvents_SubscriptionPeriodId_UserId",
                table: "UsageEvents");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PaymentOrders_Id_UserId",
                table: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_PlanId_PlanVersionId",
                table: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_UserId_PlanId_Type_Status",
                table: "PaymentOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_NativeBinding",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "SubscriptionPeriodId",
                table: "UsageEvents");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "PlanVersionBinding",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "PlanVersionId",
                table: "PaymentOrders");

            migrationBuilder.AlterColumn<string>(
                name: "PlanCode",
                table: "PaymentOrders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_UserId",
                table: "PaymentOrders",
                column: "UserId");
        }
    }
}
