using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Trips",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NoteApplied",
                table: "Trips",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "NoteApplied",
                table: "Trips");
        }
    }
}
