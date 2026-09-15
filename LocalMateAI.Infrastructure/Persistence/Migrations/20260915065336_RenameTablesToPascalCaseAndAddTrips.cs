using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameTablesToPascalCaseAndAddTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_USER_EXTERNAL_LOGINS_USERS_UserId",
                table: "USER_EXTERNAL_LOGINS");

            migrationBuilder.DropForeignKey(
                name: "FK_USER_PREFERENCE_TAGS_TAGS_TagId",
                table: "USER_PREFERENCE_TAGS");

            migrationBuilder.DropForeignKey(
                name: "FK_USER_PREFERENCE_TAGS_USERS_UserId",
                table: "USER_PREFERENCE_TAGS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_USERS",
                table: "USERS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TAGS",
                table: "TAGS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_USER_PREFERENCE_TAGS",
                table: "USER_PREFERENCE_TAGS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_USER_EXTERNAL_LOGINS",
                table: "USER_EXTERNAL_LOGINS");

            migrationBuilder.RenameTable(
                name: "USERS",
                newName: "Users");

            migrationBuilder.RenameTable(
                name: "TAGS",
                newName: "Tags");

            migrationBuilder.RenameTable(
                name: "USER_PREFERENCE_TAGS",
                newName: "UserPreferenceTags");

            migrationBuilder.RenameTable(
                name: "USER_EXTERNAL_LOGINS",
                newName: "UserExternalLogins");

            migrationBuilder.RenameIndex(
                name: "UX_USERS_Email",
                table: "Users",
                newName: "UX_Users_Email");

            migrationBuilder.RenameIndex(
                name: "UX_TAGS_Type_Name",
                table: "Tags",
                newName: "UX_Tags_Type_Name");

            migrationBuilder.RenameIndex(
                name: "IX_USER_PREFERENCE_TAGS_TagId",
                table: "UserPreferenceTags",
                newName: "IX_UserPreferenceTags_TagId");

            migrationBuilder.RenameIndex(
                name: "UX_USER_EXTERNAL_LOGINS_UserId_Provider",
                table: "UserExternalLogins",
                newName: "UX_UserExternalLogins_UserId_Provider");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Users",
                table: "Users",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Tags",
                table: "Tags",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserPreferenceTags",
                table: "UserPreferenceTags",
                columns: new[] { "UserId", "TagId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserExternalLogins",
                table: "UserExternalLogins",
                columns: new[] { "Provider", "ProviderSubject" });

            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartLatitude = table.Column<double>(type: "double precision", nullable: false),
                    StartLongitude = table.Column<double>(type: "double precision", nullable: false),
                    DurationHours = table.Column<int>(type: "integer", nullable: false),
                    BudgetMin = table.Column<decimal>(type: "numeric(12,0)", nullable: false),
                    BudgetMax = table.Column<decimal>(type: "numeric(12,0)", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trips_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItineraryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    ScheduledTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    EstimatedBudget = table.Column<decimal>(type: "numeric(12,0)", nullable: false),
                    Reasoning = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItineraryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItineraryItems_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItineraryItems_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripTags",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripTags", x => new { x.TripId, x.TagId });
                    table.ForeignKey(
                        name: "FK_TripTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TripTags_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryItems_PlaceId",
                table: "ItineraryItems",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryItems_TripId_OrderIndex",
                table: "ItineraryItems",
                columns: new[] { "TripId", "OrderIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_UserId",
                table: "Trips",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TripTags_TagId",
                table: "TripTags",
                column: "TagId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserExternalLogins_Users_UserId",
                table: "UserExternalLogins",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPreferenceTags_Tags_TagId",
                table: "UserPreferenceTags",
                column: "TagId",
                principalTable: "Tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPreferenceTags_Users_UserId",
                table: "UserPreferenceTags",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserExternalLogins_Users_UserId",
                table: "UserExternalLogins");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPreferenceTags_Tags_TagId",
                table: "UserPreferenceTags");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPreferenceTags_Users_UserId",
                table: "UserPreferenceTags");

            migrationBuilder.DropTable(
                name: "ItineraryItems");

            migrationBuilder.DropTable(
                name: "TripTags");

            migrationBuilder.DropTable(
                name: "Trips");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Users",
                table: "Users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Tags",
                table: "Tags");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserPreferenceTags",
                table: "UserPreferenceTags");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserExternalLogins",
                table: "UserExternalLogins");

            migrationBuilder.RenameTable(
                name: "Users",
                newName: "USERS");

            migrationBuilder.RenameTable(
                name: "Tags",
                newName: "TAGS");

            migrationBuilder.RenameTable(
                name: "UserPreferenceTags",
                newName: "USER_PREFERENCE_TAGS");

            migrationBuilder.RenameTable(
                name: "UserExternalLogins",
                newName: "USER_EXTERNAL_LOGINS");

            migrationBuilder.RenameIndex(
                name: "UX_Users_Email",
                table: "USERS",
                newName: "UX_USERS_Email");

            migrationBuilder.RenameIndex(
                name: "UX_Tags_Type_Name",
                table: "TAGS",
                newName: "UX_TAGS_Type_Name");

            migrationBuilder.RenameIndex(
                name: "IX_UserPreferenceTags_TagId",
                table: "USER_PREFERENCE_TAGS",
                newName: "IX_USER_PREFERENCE_TAGS_TagId");

            migrationBuilder.RenameIndex(
                name: "UX_UserExternalLogins_UserId_Provider",
                table: "USER_EXTERNAL_LOGINS",
                newName: "UX_USER_EXTERNAL_LOGINS_UserId_Provider");

            migrationBuilder.AddPrimaryKey(
                name: "PK_USERS",
                table: "USERS",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TAGS",
                table: "TAGS",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_USER_PREFERENCE_TAGS",
                table: "USER_PREFERENCE_TAGS",
                columns: new[] { "UserId", "TagId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_USER_EXTERNAL_LOGINS",
                table: "USER_EXTERNAL_LOGINS",
                columns: new[] { "Provider", "ProviderSubject" });

            migrationBuilder.AddForeignKey(
                name: "FK_USER_EXTERNAL_LOGINS_USERS_UserId",
                table: "USER_EXTERNAL_LOGINS",
                column: "UserId",
                principalTable: "USERS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_USER_PREFERENCE_TAGS_TAGS_TagId",
                table: "USER_PREFERENCE_TAGS",
                column: "TagId",
                principalTable: "TAGS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_USER_PREFERENCE_TAGS_USERS_UserId",
                table: "USER_PREFERENCE_TAGS",
                column: "UserId",
                principalTable: "USERS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
