using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleExternalLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "USERS",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "USER_EXTERNAL_LOGINS",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_EXTERNAL_LOGINS", x => new { x.Provider, x.ProviderSubject });
                    table.ForeignKey(
                        name: "FK_USER_EXTERNAL_LOGINS_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_USER_EXTERNAL_LOGINS_UserId_Provider",
                table: "USER_EXTERNAL_LOGINS",
                columns: new[] { "UserId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "USERS"
                        WHERE "PasswordHash" IS NULL
                    ) THEN
                        RAISE EXCEPTION
                            'Cannot rollback AddGoogleExternalLogin: Google-only users with NULL PasswordHash exist. Manual data migration is required before rollback.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropTable(
                name: "USER_EXTERNAL_LOGINS");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "USERS",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
