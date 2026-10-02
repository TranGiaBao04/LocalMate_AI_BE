using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_CreatedAt",
                table: "Users",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_CreatedAt",
                table: "Trips",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PaidAt_Paid",
                table: "PaymentOrders",
                column: "PaidAt",
                filter: "\"Status\" = 'Paid'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_CreatedAt",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Trips_CreatedAt",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_PaidAt_Paid",
                table: "PaymentOrders");
        }
    }
}
