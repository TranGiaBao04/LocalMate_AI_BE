using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaceReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaceReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItineraryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    QuickTags = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "ARRAY[]::text[]"),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceReviews", x => x.Id);
                    table.CheckConstraint("CK_PlaceReviews_QuickTags_AllowedValues", "\"QuickTags\" <@ ARRAY['WorthVisiting', 'NearMetro', 'EasyToReach', 'GoodValue', 'NiceAtmosphere', 'GoodForGroups', 'TooCrowded', 'HardToFind', 'Overpriced', 'BelowExpectations', 'InaccurateDescription', 'WantsReplacement']::text[]");
                    table.CheckConstraint("CK_PlaceReviews_QuickTags_MaxThree", "cardinality(\"QuickTags\") <= 3");
                    table.CheckConstraint("CK_PlaceReviews_Rating", "\"Rating\" >= 1 AND \"Rating\" <= 5");
                    table.ForeignKey(
                        name: "FK_PlaceReviews_ItineraryItems_ItineraryItemId",
                        column: x => x.ItineraryItemId,
                        principalTable: "ItineraryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlaceReviews_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlaceReviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceReviews_ItineraryItemId",
                table: "PlaceReviews",
                column: "ItineraryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceReviews_PlaceId",
                table: "PlaceReviews",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "UX_PlaceReviews_UserId_ItineraryItemId",
                table: "PlaceReviews",
                columns: new[] { "UserId", "ItineraryItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaceReviews");
        }
    }
}
