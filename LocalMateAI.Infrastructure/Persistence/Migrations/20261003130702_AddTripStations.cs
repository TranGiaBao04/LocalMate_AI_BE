using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestinationStationId",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StartStationId",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DestinationStationId",
                table: "Trips",
                column: "DestinationStationId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_StartStationId",
                table: "Trips",
                column: "StartStationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_MetroStations_DestinationStationId",
                table: "Trips",
                column: "DestinationStationId",
                principalTable: "MetroStations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_MetroStations_StartStationId",
                table: "Trips",
                column: "StartStationId",
                principalTable: "MetroStations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trips_MetroStations_DestinationStationId",
                table: "Trips");

            migrationBuilder.DropForeignKey(
                name: "FK_Trips_MetroStations_StartStationId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_DestinationStationId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_StartStationId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DestinationStationId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "StartStationId",
                table: "Trips");
        }
    }
}
