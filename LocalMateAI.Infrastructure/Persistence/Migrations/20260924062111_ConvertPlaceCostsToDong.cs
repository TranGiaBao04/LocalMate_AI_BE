using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalMateAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConvertPlaceCostsToDong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dữ liệu seed cũ tính bằng nghìn đồng (30 = 30.000đ). Chuyển sang VNĐ đầy đủ.
            // Điều kiện < 10000 để không nhân lại các giá trị đã nhập bằng VNĐ (ví dụ qua Admin API).
            // DB mới toanh: bảng còn rỗng lúc migration chạy, sau đó seed nạp file JSON đã ở VNĐ nên không bị nhân đôi.
            migrationBuilder.Sql("""
                UPDATE "Places"
                SET "EstimatedCostMin" = "EstimatedCostMin" * 1000,
                    "EstimatedCostMax" = "EstimatedCostMax" * 1000
                WHERE "EstimatedCostMax" < 10000;
                """);

            migrationBuilder.Sql("""
                UPDATE "CuratedItineraries"
                SET "EstimatedCostMin" = "EstimatedCostMin" * 1000,
                    "EstimatedCostMax" = "EstimatedCostMax" * 1000
                WHERE "EstimatedCostMax" < 10000;
                """);

            // Ngân sách chặng được sao chép từ giá địa điểm nên cũng đang ở đơn vị cũ.
            // Không đụng Trips.BudgetMin/BudgetMax vì đó là số người dùng nhập, vốn đã là VNĐ.
            migrationBuilder.Sql("""
                UPDATE "ItineraryItems"
                SET "EstimatedBudget" = "EstimatedBudget" * 1000
                WHERE "EstimatedBudget" < 10000;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Places"
                SET "EstimatedCostMin" = "EstimatedCostMin" / 1000,
                    "EstimatedCostMax" = "EstimatedCostMax" / 1000
                WHERE "EstimatedCostMax" >= 1000;
                """);

            migrationBuilder.Sql("""
                UPDATE "CuratedItineraries"
                SET "EstimatedCostMin" = "EstimatedCostMin" / 1000,
                    "EstimatedCostMax" = "EstimatedCostMax" / 1000
                WHERE "EstimatedCostMax" >= 1000;
                """);

            migrationBuilder.Sql("""
                UPDATE "ItineraryItems"
                SET "EstimatedBudget" = "EstimatedBudget" / 1000
                WHERE "EstimatedBudget" >= 1000;
                """);
        }
    }
}
