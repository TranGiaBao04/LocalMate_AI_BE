using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLockedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LockedByUserId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_LockedByUserId",
                table: "Users",
                column: "LockedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_LockedByUserId",
                table: "Users",
                column: "LockedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_LockedByUserId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_LockedByUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockedByUserId",
                table: "Users");
        }
    }
}
