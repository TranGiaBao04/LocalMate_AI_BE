using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionedAiEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "SubscriptionPlans" IN SHARE MODE;
                LOCK TABLE "SubscriptionPlanVersions" IN ACCESS EXCLUSIVE MODE;
                """);
            migrationBuilder.AddColumn<int>(
                name: "AiDailyCallLimit",
                table: "SubscriptionPlanVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiExplainCallsPerTripLimit",
                table: "SubscriptionPlanVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Versions_AiDaily",
                table: "SubscriptionPlanVersions",
                sql: "\"AiDailyCallLimit\" IS NULL OR \"AiDailyCallLimit\" BETWEEN 0 AND 1000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Versions_AiExplain",
                table: "SubscriptionPlanVersions",
                sql: "\"AiExplainCallsPerTripLimit\" IS NULL OR \"AiExplainCallsPerTripLimit\" BETWEEN 0 AND 20");

            // Only this table's update/delete trigger is suspended inside the migration transaction.
            migrationBuilder.Sql("""
                DROP TRIGGER immutable_version ON "SubscriptionPlanVersions";
                UPDATE "SubscriptionPlanVersions" v
                SET "AiDailyCallLimit" = CASE p."Code"
                        WHEN 'FREE' THEN 3 WHEN 'TRIP_PASS' THEN 15 WHEN 'MEMBERSHIP' THEN 30 END,
                    "AiExplainCallsPerTripLimit" = CASE p."Code"
                        WHEN 'FREE' THEN 1 WHEN 'TRIP_PASS' THEN 3 WHEN 'MEMBERSHIP' THEN 3 END
                FROM "SubscriptionPlans" p
                WHERE v."PlanId" = p."Id" AND p."Code" IN ('FREE','TRIP_PASS','MEMBERSHIP');
                CREATE TRIGGER immutable_version BEFORE UPDATE OR DELETE ON "SubscriptionPlanVersions"
                FOR EACH ROW EXECUTE FUNCTION lm_immutable_subscription_row();

                CREATE FUNCTION lm_require_published_ai_terms() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."Origin" = 'Published' AND
                       (NEW."AiDailyCallLimit" IS NULL OR NEW."AiExplainCallsPerTripLimit" IS NULL) THEN
                        RAISE EXCEPTION 'New published versions require AI entitlement terms' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER require_published_ai_terms BEFORE INSERT ON "SubscriptionPlanVersions"
                FOR EACH ROW EXECUTE FUNCTION lm_require_published_ai_terms();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "SubscriptionPlanVersions" IN ACCESS EXCLUSIVE MODE;
                DROP TRIGGER require_published_ai_terms ON "SubscriptionPlanVersions";
                DROP FUNCTION lm_require_published_ai_terms();
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_Versions_AiDaily",
                table: "SubscriptionPlanVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Versions_AiExplain",
                table: "SubscriptionPlanVersions");

            migrationBuilder.DropColumn(
                name: "AiDailyCallLimit",
                table: "SubscriptionPlanVersions");

            migrationBuilder.DropColumn(
                name: "AiExplainCallsPerTripLimit",
                table: "SubscriptionPlanVersions");
        }
    }
}
