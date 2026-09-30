using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// BE-82/BE-131. Sửa tay so với bản EF sinh (bản gốc xoá cột Role trước khi chép dữ liệu và thêm RoleId
    /// với Guid rỗng ⇒ mất quyền Admin + lỗi khoá ngoại): chèn 2 role hệ thống bằng SQL (Id do Postgres sinh),
    /// thêm RoleId tạm cho phép NULL, chép từ cột Role cũ theo tên role, rồi mới đặt NOT NULL và xoá cột Role.
    /// Check constraint cũ xoá bằng SQL theo cả tên "CK_USERS_Role" (tên thật trong DB) lẫn "CK_Users_Role" (tên trong model).
    /// </remarks>
    public partial class AddRbacAndUserStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Bảng Roles, RolePermissions.
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Roles_NormalizedName",
                table: "Roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.Permission });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 2. Hai role hệ thống. Code nhận ra chúng bằng NormalizedName (SystemRoles), không dùng Id.
            migrationBuilder.Sql("""
                INSERT INTO "Roles" ("Id", "Name", "NormalizedName", "Description", "IsSystem", "CreatedAt", "UpdatedAt")
                VALUES
                    (gen_random_uuid(), 'User', 'USER', 'Người dùng thường, mặc định khi đăng ký.', TRUE, now(), now()),
                    (gen_random_uuid(), 'Admin', 'ADMIN', 'Quản trị viên, có mọi quyền.', TRUE, now(), now());
                """);

            // 3. RoleId tạm cho phép NULL, chép từ cột Role cũ, rồi mới bắt buộc.
            migrationBuilder.AddColumn<Guid>(
                name: "RoleId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Users"
                SET "RoleId" = (SELECT "Id" FROM "Roles" WHERE "NormalizedName" = 'ADMIN')
                WHERE "Role" = 'Admin';

                UPDATE "Users"
                SET "RoleId" = (SELECT "Id" FROM "Roles" WHERE "NormalizedName" = 'USER')
                WHERE "RoleId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "Users",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // 4. Cột Role cũ chỉ xoá sau khi đã chép xong.
            // DB thật mang tên cũ "CK_USERS_Role" (tạo ở InitialUsers, không được đổi khi đổi tên bảng),
            // còn model ghi "CK_Users_Role" ⇒ xoá theo cả hai tên, có tên nào thì xoá tên đó.
            migrationBuilder.Sql("""
                ALTER TABLE "Users" DROP CONSTRAINT IF EXISTS "CK_USERS_Role";
                ALTER TABLE "Users" DROP CONSTRAINT IF EXISTS "CK_Users_Role";
                """);

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 5. BE-131: trạng thái khoá tài khoản.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockReason",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Status",
                table: "Users",
                sql: "\"Status\" IN ('Active', 'Locked')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Chép lại tên role vào cột Role cũ TRƯỚC khi xoá RoleId/Roles.
            // Role tuỳ chỉnh (không phải Admin) quay về 'User'.
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.Sql("""
                UPDATE "Users"
                SET "Role" = 'Admin'
                WHERE "RoleId" = (SELECT "Id" FROM "Roles" WHERE "NormalizedName" = 'ADMIN');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Role",
                table: "Users",
                sql: "\"Role\" IN ('User', 'Admin')");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_RoleId",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Status",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Users");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
