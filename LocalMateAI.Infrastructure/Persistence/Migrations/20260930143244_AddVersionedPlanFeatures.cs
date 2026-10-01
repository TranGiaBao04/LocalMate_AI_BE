using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionedPlanFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FeaturePublicationTransactionId",
                table: "SubscriptionPlanVersions",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");

            migrationBuilder.CreateTable(
                name: "PlanFeatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanFeatures", x => x.Id);
                    table.CheckConstraint("CK_PlanFeatures_Code", "\"Code\" ~ '^[A-Z][A-Z0-9_]{0,63}$'");
                    table.CheckConstraint("CK_PlanFeatures_Name", "length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlanVersionFeatures",
                columns: table => new
                {
                    PlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanVersionFeatures", x => new { x.PlanVersionId, x.FeatureId });
                    table.CheckConstraint("CK_VersionFeatures_SortOrder", "\"SortOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanVersionFeatures_PlanFeatures_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "PlanFeatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanVersionFeatures_SubscriptionPlanVersions_Pl~",
                        column: x => x.PlanVersionId,
                        principalTable: "SubscriptionPlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanFeatures_Code",
                table: "PlanFeatures",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanVersionFeatures_FeatureId",
                table: "SubscriptionPlanVersionFeatures",
                column: "FeatureId");

            migrationBuilder.Sql("""
                INSERT INTO "PlanFeatures" ("Id","Code","Name","Description","IsSystem","CreatedAt","UpdatedAt")
                VALUES ('30000000-0000-0000-0000-000000000001','METRO_GOOGLE_MAPS',
                        'Bản đồ Metro & chỉ đường Google Maps',NULL,true,now(),now());
                INSERT INTO "SubscriptionPlanVersionFeatures" ("PlanVersionId","FeatureId","SortOrder")
                SELECT "Id",'30000000-0000-0000-0000-000000000001'::uuid,0
                FROM "SubscriptionPlanVersions"
                WHERE "Id" IN ('20000000-0000-0000-0000-000000000001',
                               '20000000-0000-0000-0000-000000000002',
                               '20000000-0000-0000-0000-000000000003');

                -- Stamp the creating top-level transaction, including EF savepoint publication.
                -- Clients cannot preassign a future stamp to append features after commit.
                CREATE FUNCTION lm_stamp_feature_publication() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    NEW."FeaturePublicationTransactionId" := pg_current_xact_id()::text::bigint;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER stamp_feature_publication BEFORE INSERT ON "SubscriptionPlanVersions"
                FOR EACH ROW EXECUTE FUNCTION lm_stamp_feature_publication();

                CREATE FUNCTION lm_protect_version_feature() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE publication_transaction bigint;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        RAISE EXCEPTION 'Published version features are immutable' USING ERRCODE='23514';
                    END IF;
                    SELECT "FeaturePublicationTransactionId" INTO publication_transaction
                    FROM "SubscriptionPlanVersions" WHERE "Id"=NEW."PlanVersionId";
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Unknown plan version' USING ERRCODE='23503';
                    END IF;
                    IF publication_transaction <> pg_current_xact_id()::text::bigint THEN
                        RAISE EXCEPTION 'Features must be published with their version' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER protect_version_feature BEFORE INSERT OR UPDATE OR DELETE
                ON "SubscriptionPlanVersionFeatures"
                FOR EACH ROW EXECUTE FUNCTION lm_protect_version_feature();
                CREATE TRIGGER protect_version_feature_truncate BEFORE TRUNCATE
                ON "SubscriptionPlanVersionFeatures"
                FOR EACH STATEMENT EXECUTE FUNCTION lm_protect_version_feature();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER protect_version_feature ON "SubscriptionPlanVersionFeatures";
                DROP TRIGGER protect_version_feature_truncate ON "SubscriptionPlanVersionFeatures";
                DROP FUNCTION lm_protect_version_feature();
                DROP TRIGGER stamp_feature_publication ON "SubscriptionPlanVersions";
                DROP FUNCTION lm_stamp_feature_publication();
                """);
            migrationBuilder.DropTable(
                name: "SubscriptionPlanVersionFeatures");

            migrationBuilder.DropTable(
                name: "PlanFeatures");

            migrationBuilder.DropColumn(
                name: "FeaturePublicationTransactionId",
                table: "SubscriptionPlanVersions");
        }
    }
}
