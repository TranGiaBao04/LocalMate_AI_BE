using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripAiExplainedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AiExplainedAt",
                table: "Trips",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiExplainedAt",
                table: "Trips");
        }
    }
}
